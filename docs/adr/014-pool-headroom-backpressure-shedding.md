# ADR-014: Pool Headroom and Backpressure Shedding (80/100 + Bounded Queues)

- **Status:** Accepted
- **Date:** 2026-09-27

## Context

Phase-4 high-load runs reproduced connection-pool saturation: the API consumed
the whole Postgres pool under hype traffic, locking out operators (no lane for
diagnosis or resync) mid-incident. Separately, the `InMemory` queue fallback
needed a defined behaviour when full — unbounded growth OOMs the process.
Neither point is covered by ADRs 001–013 (003 covers Redis Lua reservation,
005 covers provider selection — this covers capacity shedding).

## Decision

Cap the API pool at 80 against a server max of 100 (operator headroom is a
standing invariant, not a tuning accident); bound the `InMemory` queue at 5,000
with HTTP 503 backpressure when full; queue-full on the Redis path releases
the reservation via Redis-only compensation (the DB was never touched).

## Consequences

- Positive: saturation degrades as honest fast-fail (503/queue-full), never as
  operator lockout or silent drop; recovery needs no restart, just drain.
- Negative: peak accepted throughput is deliberately below hardware max —
  quoted latency/throughput numbers must state the cap or they mislead.

## Alternatives

- Pool = server max for peak throughput: wins benchmarks, loses incidents —
  rejected after the phase-4 lockout reproduction.
- Unbounded queues + block-on-full: backpressure propagates as thread
  exhaustion instead of load-shedding; strictly worse tail latency.
