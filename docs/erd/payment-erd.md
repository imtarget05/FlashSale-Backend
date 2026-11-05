# Payment Service — ERD (Entity Relationship Diagram)

## Table: payment_records

Owned exclusively by the Java Payment Service. No cross-service reads.

```mermaid
erDiagram
    payment_records {
        string idempotency_key PK "UK, varchar(128)"
        uuid payment_id "not null"
        integer order_id "not null"
        numeric amount "precision(12,2), not null"
        string currency "varchar(8), not null"
        boolean success "not null"
        string status "varchar(32), not null"
        string transaction_id "varchar(64), nullable"
        string failure_reason "varchar(256), nullable"
        boolean is_transient_error "not null"
        timestamp with time zone processed_at "not null"
    }
```

## Column Definitions

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| `idempotency_key` | varchar(128) | PK, not null | Client-supplied idempotency key; primary key enforces deduplication at DB level |
| `payment_id` | uuid | not null | Internal payment identifier |
| `order_id` | integer | not null | Reference to order in Order service (logical only, no FK) |
| `amount` | numeric(12,2) | not null | Payment amount |
| `currency` | varchar(8) | not null | ISO currency code (e.g. USD, VND) |
| `success` | boolean | not null | Whether payment succeeded |
| `status` | varchar(32) | not null | Human-readable status: Succeeded, Declined, TimedOut |
| `transaction_id` | varchar(64) | nullable | Gateway transaction ID (null on failure) |
| `failure_reason` | varchar(256) | nullable | Reason for decline/timeout |
| `is_transient_error` | boolean | not null | Whether error is transient (retryable) |
| `processed_at` | timestamptz | not null | Processing timestamp |

## Indexes

| Index | Columns | Purpose |
|-------|---------|---------|
| `PK_payment_records` | `idempotency_key` | Primary key; enforces idempotency |
| (none additional) | — | Single-table service; queries by PK only |

## Design Rationale

- **Idempotency key as PK**: Database-enforced deduplication prevents double-charges under concurrent replays.
- **No foreign keys to order table**: Microservice boundary — payment service never reads order tables.
- **Single table**: Payment service owns only `payment_records`; no cross-table joins needed.
