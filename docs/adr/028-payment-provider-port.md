# ADR-028: Payment Provider Port Abstraction

## Status
Accepted (2026-11-06)

## Context
The payment service previously embedded PSP simulation logic (cents .01/.02/.03 rules) directly in `PaymentService`. This made it impossible to swap in a real PSP without modifying business logic, and made the "no real PSP" boundary unclear.

## Decision
Introduce a `PaymentProvider` port interface (`com.flashsale.payment.provider.PaymentProvider`) with a single `process(ProcessPaymentRequest)` method. Two adapters:
- `SimulatedPaymentProvider` — deterministic local demo (`.01`→Succeeded, `.02`→Declined, `.03`→TimedOut). Active when `stripe.api.key` is unset/empty.
- `StripePaymentProvider` — calls Stripe PaymentIntents REST API. Active when `stripe.api.key` is set (non-empty).

Selection is via Spring `@ConditionalOnExpression` on `stripe.api.key`, so exactly one bean is registered at runtime.

## Consequences
- Business logic (`PaymentService`) no longer knows about PSP details.
- A new PSP (e.g., PayPal, Adyen) can be added as a new adapter implementing `PaymentProvider` + `@ConditionalOnProperty`, without touching `PaymentService`.
- `PaymentService` receives `PaymentProvider` via constructor injection (Spring picks the active bean).

## Trade-offs
- Slightly more indirection.
- Stripe adapter uses `java.net.http.HttpClient` (JDK 11+) to avoid adding the Stripe SDK dependency; JSON parsing is hand-rolled (simple field extraction). For production, consider the official Stripe SDK.

## Out of scope
- Real Stripe credentials / live transactions are not exercised in this repo (no cloud secrets). The adapter is structurally ready.
