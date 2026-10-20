# C4 Level 2 — Container Diagram (OpsBridge)

```mermaid
flowchart TD
    Operator["Kho staff (Operator)"]
    Finance["Kế toán (FinanceReviewer)"]
    Auditor["Kiểm soát viên (Auditor)"]
    Admin["Quản lý vận hành (Admin)"]
    FactoryFlow["FactoryFlow (ngoại bộ)"]
    FlashSale["FlashSale / Order API (ngoại ranh ngữ cảnh)"]

    subgraph OpsBridge["OpsBridge (modular monolith)"]
        API["OpsBridge API (ASP.NET Core minimal endpoints)"]
        App["Application layer (use cases + repository ports)"]
        Dom["Domain layer (entities, value objects, domain exceptions)"]
        Infra["Infrastructure layer (EF Core adapters, RabbitMQ, HMAC)"]
        DB[("PostgreSQL OpsBridgeDb<br/>Outbox, Inbox, DLQ, PO/GR/Invoice, Reconciliation, Audit")]
        Broker[("RabbitMQ<br/>topic exchange + DLX/DLQ")]
        Web["React dashboard (OpsBridge.Web)"]
    end

    Operator -->|JWT Bearer| API
    Finance -->|JWT Bearer| API
    Auditor -->|JWT Bearer| API
    Admin -->|JWT Bearer| API
    Web -->|JWT Bearer| API

    FactoryFlow -->|POST /webhooks/factoryflow<br/>HMAC/Svix signed| API
    API --> App
    App --> Dom
    App --> Infra
    Infra --> DB
    Infra --> Broker
    Broker -.->|consumer host| App

    API -.->|không trực tiếp ghi| FlashSale
    style FlashSale fill:#f9f,stroke:#333,stroke-dasharray: 5 5
```

**Chú ý kỹ thuật:** `API` không trực tiếp tham chiếu `OpsBridgeDbContext` (guard test `ApiEndpoints_MustNotDependOnInfrastructureDbContext`); persistence chạy qua Application repository ports + Infrastructure EF Core adapters trong một `IUnitOfWork` transaction.
