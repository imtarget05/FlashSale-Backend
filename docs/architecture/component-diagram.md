# C4 Level 3 — Component Diagram (OpsBridge bounded context)

```mermaid
flowchart TD
    subgraph Api["OpsBridge.Api"]
        AuthE["AuthEndpoints"]
        WebhookE["WebhookEndpoints (factoryflow)"]
        ConnE["ConnectorEndpoints"]
        IngestE["IngestionEndpoints"]
        ReconcE["ReconciliationEndpoints"]
        ReplayE["ReplayEndpoints"]
    end

    subgraph App["OpsBridge.Application"]
        ReceiveWU["ReceiveWebhookUseCase"]
        ReplayU["ReplayDeadLetterEventUseCase"]
        ReconcileU["ReconcileInvoiceUseCase"]
        ReviewU["ReviewDiscrepancyUseCase"]
        MatchingE["ThreeWayMatchingEngine"]
        Ports["Repository ports (IOutboxRepository, IInboxRepository,<br/>IDeadLetterEventRepository, IReconciliationRepository, IAuditEventRepository, IUnitOfWork)"]
    end

    subgraph Dom["OpsBridge.Domain"]
        DLEvent["DeadLetterEvent (DLQ)"]
        Inbox["InboxMessage / ConsumerProcessingState"]
        Outbox["OutboxMessage"]
        PO["PurchaseOrder"]; GR["GoodsReceipt"]; Inv["SupplierInvoice"]
        Disc["Discrepancy"]; ReviewD["ReviewDecision"]; Audit["AuditEvent"]
    end

    subgraph Infra["OpsBridge.Infrastructure"]
        EfDLQ["EfDeadLetterEventRepository (conditional UPDATE<br/>IsReplayed = true)"]
        EfOutbox["EfOutboxRepository"]
        EfInbox["EfInboxRepository"]
        EfUnit["EfUnitOfWork (1 PostgreSQL txn)"]
        OpsCtx[("OpsBridgeDbContext<br/>EF Core + Npgsql")]
        Pub["OutboxPublisherHostedService<br/>(RabbitMQ Publisher Confirms)"]
        Consumer["GoodsReceiptConsumerHost<br/>(manual ACK, retry, poison→DLQ)"]
        Verifier["HmacWebhookVerifier (Svix)"]
        Issuer["OpsBridgeJwtTokenIssuer"]
    end

    AuthE --> App
    WebhookE --> ReceiveWU
    ReconcE --> ReconcileU
    ReconcE --> ReviewU
    ReplayE --> ReplayU
    IngestE --> App

    ReceiveWU --> Ports
    ReplayU --> Ports
    ReconcileU --> Ports
    ReviewU --> Ports
    MatchingE --> Dom

    Ports -.->|implemented by| Infra
    EfDLQ --> OpsCtx
    EfOutbox --> OpsCtx
    EfInbox --> OpsCtx
    EfUnit --> OpsCtx
    Pub --> OpsCtx
    Pub --> Broker[("RabbitMQ")]
    Consumer --> Broker
    Consumer --> OpsCtx
    Verifier --> WebhookE
    Issuer --> Api
```

**Ranh giới thành phần:**
- `Dom` không tham chiếu bất kỳ gói infrastructure nào (guard test `OpsBridge_Domain_MustNotReference_AnyInfrastructurePackage`).
- `App` chỉ phụ thuộc `Dom` (`OpsBridge_Application_MustDependOn_Domain`).
- `Infra` không tham chiếu `FlashSale` assembly (`OpsBridge_Infrastructure_MustNotReference_FlashSale`).
- Thứ tự tham chiếu dự án: `Domain → Application → Infrastructure → API` (`ProjectReferences_MustFollowCleanArchitectureDirection`).
