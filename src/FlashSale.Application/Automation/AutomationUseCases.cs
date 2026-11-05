namespace FlashSale.Application.Automation;

using FlashSale.Application.Inventory;
using FlashSale.Application.Payment;
using FlashSale.Application.Persistence;
using FlashSale.Application.Outbox;
using Microsoft.Extensions.Logging;

public sealed class RecordPaymentUseCase
{
    private readonly IPaymentRepository _paymentRepo;
    private readonly IOrderReadModel _readModel;
    private readonly IAutomationRunRepository _runRepo;
    private readonly IWriteTransaction _transaction;
    private readonly OutboxEventWriter _outbox;
    private readonly ILogger<RecordPaymentUseCase> _logger;

    public RecordPaymentUseCase(
        IPaymentRepository paymentRepo,
        IOrderReadModel readModel,
        IAutomationRunRepository runRepo,
        IWriteTransaction transaction,
        OutboxEventWriter outbox,
        ILogger<RecordPaymentUseCase> logger)
    {
        _paymentRepo = paymentRepo;
        _readModel = readModel;
        _runRepo = runRepo;
        _transaction = transaction;
        _outbox = outbox;
        _logger = logger;
    }

    public Task ExecuteAsync(string idempotencyKey, CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class PaymentAutomationUseCase
{
    private readonly IOrderReadModel _readModel;
    private readonly IPaymentRepository _paymentRepo;
    private readonly IStockReservationGateway _gateway;
    private readonly IAutomationRunRepository _runRepo;
    private readonly IWriteTransaction _transaction;
    private readonly OutboxEventWriter _outbox;
    private readonly PaymentAutomationOptions _options;
    private readonly ILogger<PaymentAutomationUseCase> _logger;

    public PaymentAutomationUseCase(
        IOrderReadModel readModel,
        IPaymentRepository paymentRepo,
        IStockReservationGateway gateway,
        IAutomationRunRepository runRepo,
        IWriteTransaction transaction,
        OutboxEventWriter outbox,
        PaymentAutomationOptions options,
        ILogger<PaymentAutomationUseCase> logger)
    {
        _readModel = readModel;
        _paymentRepo = paymentRepo;
        _gateway = gateway;
        _runRepo = runRepo;
        _transaction = transaction;
        _outbox = outbox;
        _options = options;
        _logger = logger;
    }

    public Task ExecuteAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public sealed class LowStockAlertUseCase
{
    private readonly IOrderReadModel _readModel;
    private readonly IAutomationRunRepository _runRepo;
    private readonly IWriteTransaction _transaction;
    private readonly OutboxEventWriter _outbox;
    private readonly InventoryAutomationOptions _options;
    private readonly ILogger<LowStockAlertUseCase> _logger;

    public LowStockAlertUseCase(
        IOrderReadModel readModel,
        IAutomationRunRepository runRepo,
        IWriteTransaction transaction,
        OutboxEventWriter outbox,
        InventoryAutomationOptions options,
        ILogger<LowStockAlertUseCase> logger)
    {
        _readModel = readModel;
        _runRepo = runRepo;
        _transaction = transaction;
        _outbox = outbox;
        _options = options;
        _logger = logger;
    }

    public Task ExecuteAsync(CancellationToken ct = default) => Task.CompletedTask;
}
