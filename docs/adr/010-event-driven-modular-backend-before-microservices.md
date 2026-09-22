# ADR-010: Event-Driven Modular Backend Before Microservices

## Status
Accepted

## Date
2026-09-20

## Context
The FlashSale Backend must support a high-concurrency retail flash-sale workload where a large number of purchase attempts can arrive within a short period of time.

The main engineering problems currently demonstrated by the project are:
- traffic bursts;
- inventory overselling;
- concurrent updates;
- asynchronous order processing;
- duplicate message delivery;
- idempotency;
- backpressure;
- Redis failure;
- message broker failure;
- database consistency.

The current runtime contains two primary application components:
- `Order.Api`
- `Order.Worker`

They communicate asynchronously through a message broker.
PostgreSQL remains the authoritative business database.
Redis is used as a fast inventory-reservation layer but is not the business source of truth.
RabbitMQ is used locally as the asynchronous order queue.

Although the API and Worker are separately deployable processes, they currently implement one cohesive Order bounded context and operate around the same business model and source of truth.
Therefore, describing the current system as a full microservices architecture would overstate its actual domain decomposition.

## Problem
Microservices could split the system into components such as:
- Order Service;
- Inventory Service;
- Payment Service;
- Promotion Service;
- Notification Service;
- Fulfillment Service.

However, introducing these boundaries now would also introduce additional distributed-system complexity before the business requirements justify it.
That complexity includes:
- network communication;
- independent data ownership;
- distributed consistency;
- asynchronous integration contracts;
- duplicate event handling;
- out-of-order events;
- service discovery;
- distributed tracing;
- independent deployment;
- failure propagation;
- Saga/compensation;
- operational overhead.

The project currently does not have evidence that these additional service boundaries are required.
The immediate business problem is not:
"How do we create many microservices?"

It is:
"How do we accept and correctly process a flash-sale traffic burst without overselling inventory or losing business operations?"

## Decision
The current architecture will be classified as an:
**Event-Driven Modular Backend**
rather than a Microservices Architecture.

The current logical architecture is:
```text
Client 
  |
  v 
Order API 
  |
  +---- Redis | Inventory reservation / acceleration
  |
  v 
RabbitMQ 
  |
  v 
Order Worker 
  |
  v 
PostgreSQL Business source of truth
```

`Order.Api` and `Order.Worker` are treated as two runtime components belonging to the same Order bounded context.
Asynchronous messaging is introduced because it solves demonstrated workload problems:
- absorbs traffic bursts;
- decouples request acceptance from slower order processing;
- provides backpressure;
- permits independent Worker scaling;
- allows retry/redelivery behavior.

Microservices will not be introduced simply because they are common in large-scale systems.
Future service extraction requires a concrete business or operational trigger.

### Why Event-Driven Architecture Is Used Now
The synchronous baseline demonstrated a mismatch between incoming flash-sale traffic and downstream processing capacity.
The queue creates temporal decoupling:
```text
Traffic Burst | v Order API | v Queue / Buffer | v Order Workers | v PostgreSQL
```
This allows incoming work to be accepted independently from the rate at which downstream processing occurs.
The architecture therefore uses asynchronous messaging to solve an observed system problem rather than to introduce messaging technology for portfolio purposes.

### Why Microservices Are Not Used Yet
Service boundaries should represent meaningful business capabilities.
The project currently lacks evidence for boundaries that need:
- different teams;
- different release cycles;
- different scaling characteristics;
- different security/compliance requirements;
- different availability requirements;
- independently owned databases;
- independent domain evolution.

Splitting these capabilities prematurely would increase operational complexity without demonstrated business value.

## Alternatives Considered

### Option 1 — Synchronous Monolith
`Client | Order API | PostgreSQL`

**Advantages**
- lowest operational complexity;
- simple transactions;
- easy local development;
- easy debugging.

**Disadvantages**
- incoming traffic directly pressures PostgreSQL;
- poor burst absorption;
- request latency is tied to downstream processing;
- limited asynchronous scaling.

**Decision**
Rejected as the final flash-sale processing architecture because concurrency/load experiments demonstrated limitations that justified asynchronous processing.
It remains important as the project's original baseline.

### Option 2 — Event-Driven Modular Backend
`Order API | RabbitMQ | Order Worker | PostgreSQL`

**Advantages**
- solves current traffic-spike problem;
- supports asynchronous processing;
- provides backpressure;
- preserves relatively simple domain boundaries;
- retains straightforward data consistency;
- avoids premature distributed transactions;
- API and Worker can scale independently.

**Disadvantages**
- introduces broker operations;
- introduces at-least-once message delivery;
- requires idempotency;
- introduces retry/DLQ handling;
- creates more operational complexity than the synchronous baseline.

**Decision**
Selected.
This option provides sufficient decoupling for the current workload without introducing unnecessary domain decomposition.

### Option 3 — Microservices from the Beginning
Example:
```text
API Gateway 
| 
+-----------------+-----------------+ 
|                 |                 | 
v                 v                 v 
Order Service Inventory Service Payment Service 
|                 |                 | 
Order DB      Inventory DB      Payment DB 
|                 |                 | 
+----------- Event Bus ------------+
```

**Advantages**
- independent deployment;
- independent scaling;
- clearer ownership when bounded contexts are mature;
- service-specific availability/security requirements;
- separate data ownership.

**Disadvantages**
- distributed transactions;
- eventual consistency;
- higher infrastructure cost;
- larger observability surface;
- additional deployment pipelines;
- more complex local development;
- network failure handling;
- event schema/versioning;
- Saga/compensation requirements;
- difficult service-boundary changes.

**Decision**
Rejected for the current business stage.
The project does not yet have evidence that the additional complexity is justified.

## Data Ownership Decision
At the current stage:
`Order bounded context | v PostgreSQL`

PostgreSQL remains one authoritative transactional boundary.
Redis is reconstructable derived state.
RabbitMQ transports work but does not own business truth.
This allows important order/inventory operations to preserve simple database-level correctness.

## Evolution Strategy
Architecture evolution must follow business evidence.

### V1 — Synchronous Baseline
`Client | Order API | PostgreSQL`
**Purpose:** Establish the simplest working system and expose concurrency/performance limitations.

### V2 — Event-Driven Modular Backend
`Client | Order API | Redis | RabbitMQ | Order Worker | PostgreSQL`
**Purpose:** Handle burst traffic, asynchronous processing, idempotency and backpressure. (Current architecture).

### V3 — Selective Microservice Extraction
The first potential candidate is: **Inventory**
But extraction will happen only when a demonstrated trigger exists.

Possible triggers:
- Inventory traffic scales very differently from Order traffic;
- Inventory requires an independent release cycle;
- Inventory becomes owned by a separate team;
- multiple sales channels require Inventory independently;
- inventory availability requirements differ materially from Order;
- the Order database becomes a coordination bottleneck;
- an independent inventory domain model emerges.

Potential architecture:
`Order | OrderRequested | v Inventory Service | Inventory DB`

At this point, Order and Inventory no longer share one ACID transaction. That introduces eventual consistency.

### V4 — Distributed Business Workflow
If the business later introduces independent:
- Order;
- Inventory;
- Payment;
bounded contexts, a purchase workflow could become:
`Create Order | Reserve Inventory | Authorize Payment | Confirm Order`

Failure may require compensation:
`Payment Failed | v Release Inventory | v Cancel Order`

At that point patterns such as Saga, Transactional Outbox, Inbox/idempotent consumer, event versioning, compensation, distributed tracing become candidates because the business workflow justifies them. They are explicitly not required by the current architecture.

## Microservice Extraction Gate
A module should not become a microservice unless at least one meaningful requirement demonstrates independent lifecycle needs.

Evaluate:
- **Business**: Does it represent an independent business capability? Does it have a clear bounded context? Does it have distinct business rules?
- **Scaling**: Does it need to scale independently?
- **Deployment**: Does it require an independent release lifecycle?
- **Team Ownership**: Would an independent team own it?
- **Reliability**: Does it require a different SLO or failure boundary?
- **Security**: Does it require different access/compliance controls?
- **Data**: Can it own its data without excessive cross-service transactions?
- **Operations**: Is the operational complexity justified?

If these answers do not justify extraction, keep the functionality modular within the existing bounded context.

## Consequences

### Positive
The project remains easier to understand, test, back up, restore, operate, observe, deploy, and explain during interviews.
The architecture still demonstrates real distributed-system concepts:
- asynchronous messaging;
- at-least-once delivery;
- idempotency;
- retry;
- DLQ;
- backpressure;
- cache/database drift;
- concurrency;
- failure fallback.

The system can evolve toward microservices without claiming that decomposition is already necessary.

### Negative
The Order bounded context remains a larger deployment/domain boundary.
Some components share the same authoritative database.
Independent domain scaling is limited compared with fully separated services.

Future extraction may require:
- data migration;
- API contracts;
- event contracts;
- new CI/CD pipelines;
- Saga/Outbox;
- additional observability.

These costs are intentionally deferred until business requirements justify them.

## Portfolio / Interview Positioning
Describe the current system as:
**Event-Driven Flash-Sale Order Processing Platform**
or:
**Event-Driven Modular Backend**

Do not currently describe it as:
**Microservices Architecture**

A concise interview explanation is:
> "We started with the simplest synchronous design and reproduced its concurrency and traffic limitations. We introduced asynchronous messaging because it solved an observed flash-sale problem. We deliberately kept Order API and Worker inside one bounded context rather than prematurely splitting services. If Inventory later develops an independent scaling, ownership or availability lifecycle, that becomes a concrete trigger to extract it as a microservice. At that point we would also accept the additional consistency and operational costs that follow."

## Revisit This Decision When
Review this ADR if any of these become true:
- Inventory requires independent scaling;
- separate teams own Order and Inventory;
- Payment becomes part of the transaction;
- multiple applications consume Inventory independently;
- a shared database prevents independent delivery;
- different SLO/security/compliance requirements appear;
- business growth makes the current bounded context too coupled;
- measured operational benefits exceed the microservice complexity premium.

Until then:
**Prefer modularity + asynchronous decoupling over premature service decomposition.**
