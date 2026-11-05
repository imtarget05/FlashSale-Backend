# Payment Service — Sequence Diagrams

## 1. Successful Payment Flow

```mermaid
sequenceDiagram
    participant Client
    participant Java as Payment Service Java
    participant PG as PostgreSQL
    participant RMQ as RabbitMQ

    Client->>Java: POST /api/payments<br/>{idempotencyKey, amount, ...}
    Java->>PG: SELECT ... WHERE idempotency_key = ?
    PG-->>Java: null (not found)
    Java->>Java: Simulate payment (cent rules)
    Java->>PG: INSERT payment_records<br/>(idempotency_key = PK)
    PG-->>Java: OK (1 row)
    Java->>RMQ: Publish payment.completed
    RMQ-->>Java: Ack
    Java-->>Client: 200 OK {status: "Succeeded", ...}
```

## 2. Idempotent Replay (Same Key)

```mermaid
sequenceDiagram
    participant Client
    participant Java as Payment Service Java
    participant PG as PostgreSQL

    Client->>Java: POST /api/payments<br/>{idempotencyKey: "idem-1", ...}
    Java->>PG: SELECT ... WHERE idempotency_key = "idem-1"
    PG-->>Java: Existing record (Succeeded)
    Java-->>Client: 200 OK {status: "Succeeded", ...}<br/>No INSERT performed
```

## 3. Declined Payment (Cent Value .02)

```mermaid
sequenceDiagram
    participant Client
    participant Java as Payment Service Java
    participant PG as PostgreSQL

    Client->>Java: POST /api/payments<br/>{amount: 10.02}
    Java->>Java: cents == 2 → Declined
    Java->>PG: INSERT payment_records<br/>(success: false, status: "Declined")
    PG-->>Java: OK
    Java-->>Client: 200 OK {status: "Declined", ...}
```

## 4. Transient Timeout (Cent Value .03)

```mermaid
sequenceDiagram
    participant Client
    participant Java as Payment Service Java
    participant PG as PostgreSQL
    participant RMQ as RabbitMQ

    Client->>Java: POST /api/payments<br/>{amount: 10.03}
    Java->>Java: cents == 3 → TimedOut
    Java->>PG: INSERT payment_records<br/>(status: "TimedOut", isTransientError: true)
    PG-->>Java: OK
    Java-->>Client: 504 Gateway Timeout<br/>{status: "TimedOut", isTransientError: true}
    Client->>Java: POST /api/payments<br/>{idempotencyKey: same}
    Java->>PG: SELECT ... WHERE idempotency_key = ?
    PG-->>Java: Existing record (TimedOut)
    Java-->>Client: 200 OK {status: "TimedOut", ...}<br/>Client retries on same signal
```

## 5. Idempotency Race (Concurrent Requests)

```mermaid
sequenceDiagram
    participant Client A
    participant Client B
    participant Java as Payment Service Java
    participant PG as PostgreSQL

    Client A->>Java: POST /api/payments<br/>{idempotencyKey: "idem-race"}
    Client B->>Java: POST /api/payments<br/>{idempotencyKey: "idem-race"}
    Java->>PG: INSERT ... idempotency_key = "idem-race"
    PG-->>Java: OK (A wins PK race)
    Java->>PG: INSERT ... idempotency_key = "idem-race"
    PG-->>Java: ERROR 23505 (duplicate key)
    Java->>PG: SELECT ... WHERE idempotency_key = "idem-race"
    PG-->>Java: A's record
    Java-->>Client B: 200 OK {status: A's status}<br/>Returns winner's result
```

## 6. Authentication Flow

```mermaid
sequenceDiagram
    participant Client
    participant Java as Payment Service Java

    Client->>Java: POST /api/auth/login<br/>{email, password}
    Java->>Java: Validate credentials<br/>(BCrypt password check)
    Java->>Java: Generate JWT (HS256, signed)
    Java-->>Client: 200 OK {token: "JWT", type: "Bearer"}
    Client->>Java: GET /api/payments/{key}<br/>Authorization: Bearer JWT
    Java->>Java: Validate JWT signature & expiry
    Java->>Java: Check RBAC (CUSTOMER/STAFF/ADMIN)
    Java-->>Client: 200 OK {payment data}
```
