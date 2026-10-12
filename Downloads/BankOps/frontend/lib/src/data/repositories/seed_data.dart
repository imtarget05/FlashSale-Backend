part of 'fake_repository.dart';

/// Seed data cho demo — tạo dữ liệu mẫu trong bộ nhớ.
/// Các hàm này được fake_repository.dart gọi qua part directive.

List<AppUser> _seedUsers() => const [
  AppUser(id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1', username: 'demo_maker', displayName: 'Nguyễn An (giả)', roles: ['staff']),
  AppUser(id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa2', username: 'demo_checker', displayName: 'Trần Bình (giả)', roles: ['approver']),
  AppUser(id: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa3', username: 'demo_auditor', displayName: 'Lê Chi (giả)', roles: ['auditor']),
];

List<InventoryLocation> _seedLocations() => const [
  InventoryLocation(id: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb1', code: 'DEMO-HN', name: 'Kho demo Hà Nội'),
  InventoryLocation(id: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2', code: 'DEMO-HCM', name: 'Kho demo TP.HCM'),
];

List<InventoryItem> _seedItems() => const [
  InventoryItem(id: 'cccccccc-cccc-4ccc-8ccc-ccccccccccc1', sku: 'DEMO-PAPER-A4', name: 'Giấy A4 giả lập', unit: 'ram', trackedSerial: false),
  InventoryItem(id: 'cccccccc-cccc-4ccc-8ccc-ccccccccccc2', sku: 'DEMO-LAPTOP', name: 'Laptop mẫu', unit: 'cái', trackedSerial: true),
];

List<StockBalance> _seedBalances() => const [
  StockBalance(locationId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb1', itemId: 'cccccccc-cccc-4ccc-8ccc-ccccccccccc1', quantity: '100.000'),
];

List<AssetRecord> _seedAssets() => const [
  AssetRecord(serial: 'DEMO-ASSET-0001', itemId: 'cccccccc-cccc-4ccc-8ccc-ccccccccccc2', locationId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb1', status: AssetStatus.available),
];

List<StockMovement> _seedMovements() => [
  StockMovement(id: 'mmmmmmmm-0001', itemId: 'cccccccc-cccc-4ccc-8ccc-ccccccccccc1', locationId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb1', quantity: '100.000', kind: 'receipt', createdAt: DateTime.utc(2026, 10, 1)),
];

List<StockRequest> _seedStockRequests() => [
  StockRequest(id: 'sr-0001', kind: RequestKind.receipt, status: RequestStatus.draft, version: 1, createdBy: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1', createdByName: 'Nguyễn An (giả)', locationId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb1', items: const [StockRequestLine(itemId: 'cccccccc-cccc-4ccc-8ccc-ccccccccccc1', quantity: '10.000')], reason: 'Nạp kho demo', createdAt: DateTime.utc(2026, 10, 5)),
  StockRequest(id: 'sr-0002', kind: RequestKind.payment, status: RequestStatus.pending, version: 1, createdBy: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa2', createdByName: 'Trần Bình (giả)', locationId: 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbb2', items: const [StockRequestLine(itemId: 'cccccccc-cccc-4ccc-8ccc-ccccccccccc2', quantity: '1.000')], reason: 'Xuất kho laptop', createdAt: DateTime.utc(2026, 10, 6)),
];

List<Cashbox> _seedCashboxes() => const [
  Cashbox(id: '11111111-1111-4111-8111-111111111111', code: 'DEMO-CASH-01', name: 'Quỹ thử nghiệm', openingBalance: '0.00'),
];

List<CashBalance> _seedCashBalances() => const [
  CashBalance(cashboxId: '11111111-1111-4111-8111-111111111111', amount: '750000.00', currency: 'VND'),
];

List<JournalEntry> _seedJournals() => [
  JournalEntry(id: 'je-0001', cashboxId: '11111111-1111-4111-8111-111111111111', kind: 'receipt', amount: '1000000.00', currency: 'VND', reason: 'Nạp quỹ', createdAt: DateTime.utc(2026, 10, 1)),
  JournalEntry(id: 'je-0002', cashboxId: '11111111-1111-4111-8111-111111111111', kind: 'payment', amount: '250000.00', currency: 'VND', reason: 'Chi phí tiếp khách', createdAt: DateTime.utc(2026, 10, 3)),
];

List<CashRequest> _seedCashRequests() => [
  CashRequest(id: 'cr-0001', kind: RequestKind.receipt, status: RequestStatus.draft, version: 1, cashboxId: '11111111-1111-4111-8111-111111111111', amount: '1000000.00', currency: 'VND', reason: 'Nạp quỹ mô phỏng', createdBy: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa1', createdByName: 'Nguyễn An (giả)', createdAt: DateTime.utc(2026, 10, 5)),
  CashRequest(id: 'cr-0002', kind: RequestKind.payment, status: RequestStatus.pending, version: 1, cashboxId: '11111111-1111-4111-8111-111111111111', amount: '250000.00', currency: 'VND', reason: 'Chi phí tiếp khách', createdBy: 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaa2', createdByName: 'Trần Bình (giả)', createdAt: DateTime.utc(2026, 10, 6)),
];

List<ActivityEvent> _seedActivity() => [
  ActivityEvent(id: 'ae-0001', occurredAt: DateTime.utc(2026, 10, 8, 9, 0), subjectId: 'sr-0001', status: 'created', messageKey: 'stock_request_created', processed: 1, total: 1),
  ActivityEvent(id: 'ae-0002', occurredAt: DateTime.utc(2026, 10, 8, 10, 30), subjectId: 'cr-0002', status: 'pending', messageKey: 'cash_request_pending'),
];

List<IntegrationConnection> _seedConnections() => const [
  IntegrationConnection(id: 'ic-0001', name: 'SAP sandbox', endpoint: 'https://sap.example/api', status: 'connected'),
];

List<IntegrationRun> _seedRuns() => [
  IntegrationRun(id: 'ir-0001', connectionId: 'ic-0001', status: 'succeeded', startedAt: DateTime.utc(2026, 10, 8, 8, 0), processed: 42, total: 42),
];

List<ReconciliationRun> _seedReconciliations() => [
  ReconciliationRun(id: 'rr-0001', status: 'completed', startedAt: DateTime.utc(2026, 10, 8, 7, 0), exceptions: const ['DEMO-ASSET-0001']),
];

List<InventoryBucket> _seedInventoryDaily() => const [
  InventoryBucket(bucket: '2026-10-01', inboundQty: '100', outboundQty: '15', netQty: '85', receiptCount: 2, issueCount: 1),
  InventoryBucket(bucket: '2026-10-03', inboundQty: '40', outboundQty: '25', netQty: '15', receiptCount: 1, issueCount: 2),
  InventoryBucket(bucket: '2026-10-05', inboundQty: '60', outboundQty: '10', netQty: '50', receiptCount: 2, issueCount: 1),
  InventoryBucket(bucket: '2026-10-08', inboundQty: '30', outboundQty: '20', netQty: '10', receiptCount: 1, issueCount: 1),
];

List<InventoryBucket> _seedInventoryMonthly() => const [
  InventoryBucket(bucket: '2026-08', inboundQty: '180', outboundQty: '95', netQty: '85', receiptCount: 5, issueCount: 4),
  InventoryBucket(bucket: '2026-09', inboundQty: '210', outboundQty: '130', netQty: '80', receiptCount: 6, issueCount: 5),
  InventoryBucket(bucket: '2026-10', inboundQty: '230', outboundQty: '70', netQty: '160', receiptCount: 6, issueCount: 5),
];

List<CashflowBucket> _seedCashflowDaily() => const [
  CashflowBucket(bucket: '2026-10-01', totalIn: '1000000.00', totalOut: '0.00', net: '1000000.00', journalCount: 1),
  CashflowBucket(bucket: '2026-10-03', totalIn: '0.00', totalOut: '250000.00', net: '-250000.00', journalCount: 1),
  CashflowBucket(bucket: '2026-10-06', totalIn: '500000.00', totalOut: '120000.00', net: '380000.00', journalCount: 2),
  CashflowBucket(bucket: '2026-10-09', totalIn: '350000.00', totalOut: '150000.00', net: '200000.00', journalCount: 2),
];

List<CashflowBucket> _seedCashflowMonthly() => const [
  CashflowBucket(bucket: '2026-08', totalIn: '2400000.00', totalOut: '900000.00', net: '1500000.00', journalCount: 5),
  CashflowBucket(bucket: '2026-09', totalIn: '3100000.00', totalOut: '1450000.00', net: '1650000.00', journalCount: 7),
  CashflowBucket(bucket: '2026-10', totalIn: '1850000.00', totalOut: '520000.00', net: '1330000.00', journalCount: 6),
];

List<StagnationItem> _seedStagnation() => const [
  StagnationItem(
    itemId: 'cccccccc-cccc-4ccc-8ccc-ccccccccccc2',
    sku: 'DEMO-LAPTOP',
    name: 'Laptop mẫu',
    quantity: '4.000',
    daysSinceLastIssue: 98,
    severity: 'critical',
  ),
  StagnationItem(
    itemId: 'cccccccc-cccc-4ccc-8ccc-ccccccccccc1',
    sku: 'DEMO-PAPER-A4',
    name: 'Giấy A4 giả lập',
    quantity: '100.000',
    daysSinceLastIssue: 45,
    severity: 'watch',
  ),
];
