import 'package:flutter/material.dart';

import '../../app/app_shell.dart';
import '../../core/widgets/state_views.dart';
import '../../data/models/dtos.dart';
import '../../data/repositories/bankops_repository.dart';

/// Màn yêu cầu — tạo mới và gửi duyệt phiếu kho (stock) + phiếu quỹ (cash).
class RequestsScreen extends StatefulWidget {
  const RequestsScreen({super.key});

  @override
  State<RequestsScreen> createState() => _RequestsScreenState();
}

class _RequestsScreenState extends State<RequestsScreen> {
  Future<_RequestsData>? _future;
  bool _busy = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final repo = RepositoryScope.of(context);
    _future ??= _load(repo);
  }

  void _reload() {
    final repo = RepositoryScope.of(context);
    setState(() {
      _future = _load(repo);
    });
  }

  Future<void> _submitStock(String id) async {
    final repo = RepositoryScope.of(context);
    setState(() => _busy = true);
    try {
      await repo.submitStockRequest(id);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Đã gửi duyệt phiếu kho $id')),
      );
      _reload();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Lỗi gửi duyệt: $e')),
      );
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _submitCash(String id) async {
    final repo = RepositoryScope.of(context);
    setState(() => _busy = true);
    try {
      await repo.submitCashRequest(id);
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Đã gửi duyệt phiếu quỹ $id')),
      );
      _reload();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Lỗi gửi duyệt: $e')),
      );
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _openCreateStockDialog(_RequestsData data) async {
    if (data.locations.isEmpty || data.items.isEmpty) return;
    final repo = RepositoryScope.of(context);
    RequestKind kind = RequestKind.receipt;
    String locationId = data.locations.first.id;
    final nonSerialItems = data.items.where((i) => !i.trackedSerial).toList();
    String itemId =
        (nonSerialItems.isNotEmpty ? nonSerialItems.first : data.items.first).id;
    final qtyCtrl = TextEditingController(text: '25');
    final reasonCtrl = TextEditingController(text: 'Bổ sung vật tư định kỳ');

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setDialogState) => AlertDialog(
          title: const Text('Tạo phiếu kho mới'),
          content: SizedBox(
            width: 420,
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  DropdownButtonFormField<RequestKind>(
                    key: const Key('stock-kind-dropdown'),
                    initialValue: kind,
                    decoration: const InputDecoration(labelText: 'Loại phiếu'),
                    items: const [
                      DropdownMenuItem(
                        value: RequestKind.receipt,
                        child: Text('Nhập kho (receipt)'),
                      ),
                      DropdownMenuItem(
                        value: RequestKind.payment,
                        child: Text('Xuất kho (issue)'),
                      ),
                    ],
                    onChanged: (v) {
                      if (v != null) setDialogState(() => kind = v);
                    },
                  ),
                  const SizedBox(height: 12),
                  DropdownButtonFormField<String>(
                    key: const Key('stock-location-dropdown'),
                    initialValue: locationId,
                    decoration: const InputDecoration(labelText: 'Kho'),
                    items: [
                      for (final loc in data.locations)
                        DropdownMenuItem(
                          value: loc.id,
                          child: Text('${loc.code} — ${loc.name}'),
                        ),
                    ],
                    onChanged: (v) {
                      if (v != null) setDialogState(() => locationId = v);
                    },
                  ),
                  const SizedBox(height: 12),
                  DropdownButtonFormField<String>(
                    key: const Key('stock-item-dropdown'),
                    initialValue: itemId,
                    decoration: const InputDecoration(labelText: 'Vật tư'),
                    items: [
                      for (final item in data.items)
                        DropdownMenuItem(
                          value: item.id,
                          child: Text('${item.sku} — ${item.name} (${item.unit})'),
                        ),
                    ],
                    onChanged: (v) {
                      if (v != null) setDialogState(() => itemId = v);
                    },
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    key: const Key('stock-qty-input'),
                    controller: qtyCtrl,
                    decoration: const InputDecoration(labelText: 'Số lượng'),
                    keyboardType: TextInputType.number,
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    key: const Key('stock-reason-input'),
                    controller: reasonCtrl,
                    decoration: const InputDecoration(labelText: 'Lý do'),
                  ),
                ],
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(ctx).pop(false),
              child: const Text('Huỷ'),
            ),
            FilledButton(
              key: const Key('stock-dialog-save-btn'),
              onPressed: () => Navigator.of(ctx).pop(true),
              child: const Text('Tạo phiếu nháp'),
            ),
          ],
        ),
      ),
    );

    if (confirmed != true || !mounted) return;
    setState(() => _busy = true);
    try {
      final created = await repo.createStockRequest(
        kind: kind,
        sourceLocationId: kind == RequestKind.payment ? locationId : null,
        destLocationId: kind == RequestKind.receipt ? locationId : null,
        lines: [
          StockRequestLine(
            itemId: itemId,
            quantity: qtyCtrl.text.trim().isEmpty ? '1' : qtyCtrl.text.trim(),
          ),
        ],
        reason: reasonCtrl.text.trim(),
      );
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Đã tạo phiếu kho ${created.id}')),
      );
      _reload();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Lỗi tạo phiếu kho: $e')),
      );
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _openCreateCashDialog(_RequestsData data) async {
    if (data.cashboxes.isEmpty) return;
    final repo = RepositoryScope.of(context);
    RequestKind kind = RequestKind.receipt;
    String cashboxId = data.cashboxes.first.id;
    final amountCtrl = TextEditingController(text: '500000');
    final reasonCtrl = TextEditingController(text: 'Thu quỹ mô phỏng giao dịch');

    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => StatefulBuilder(
        builder: (ctx, setDialogState) => AlertDialog(
          title: const Text('Tạo phiếu quỹ mô phỏng'),
          content: SizedBox(
            width: 420,
            child: SingleChildScrollView(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  DropdownButtonFormField<RequestKind>(
                    key: const Key('cash-kind-dropdown'),
                    initialValue: kind,
                    decoration: const InputDecoration(labelText: 'Loại giao dịch'),
                    items: const [
                      DropdownMenuItem(
                        value: RequestKind.receipt,
                        child: Text('Thu quỹ (receipt)'),
                      ),
                      DropdownMenuItem(
                        value: RequestKind.payment,
                        child: Text('Chi quỹ (payment)'),
                      ),
                    ],
                    onChanged: (v) {
                      if (v != null) setDialogState(() => kind = v);
                    },
                  ),
                  const SizedBox(height: 12),
                  DropdownButtonFormField<String>(
                    key: const Key('cash-box-dropdown'),
                    initialValue: cashboxId,
                    decoration: const InputDecoration(labelText: 'Quỹ mô phỏng'),
                    items: [
                      for (final box in data.cashboxes)
                        DropdownMenuItem(
                          value: box.id,
                          child: Text('${box.code} — ${box.name}'),
                        ),
                    ],
                    onChanged: (v) {
                      if (v != null) setDialogState(() => cashboxId = v);
                    },
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    key: const Key('cash-amount-input'),
                    controller: amountCtrl,
                    decoration: const InputDecoration(labelText: 'Số tiền (VND)'),
                    keyboardType: TextInputType.number,
                  ),
                  const SizedBox(height: 12),
                  TextField(
                    key: const Key('cash-reason-input'),
                    controller: reasonCtrl,
                    decoration: const InputDecoration(labelText: 'Lý do'),
                  ),
                ],
              ),
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(ctx).pop(false),
              child: const Text('Huỷ'),
            ),
            FilledButton(
              key: const Key('cash-dialog-save-btn'),
              onPressed: () => Navigator.of(ctx).pop(true),
              child: const Text('Tạo phiếu nháp'),
            ),
          ],
        ),
      ),
    );

    if (confirmed != true || !mounted) return;
    setState(() => _busy = true);
    try {
      final created = await repo.createCashRequest(
        cashboxId: cashboxId,
        kind: kind,
        amount: amountCtrl.text.trim().isEmpty ? '100000' : amountCtrl.text.trim(),
        reason: reasonCtrl.text.trim(),
      );
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Đã tạo phiếu quỹ ${created.id}')),
      );
      _reload();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Lỗi tạo phiếu quỹ: $e')),
      );
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<_RequestsData>(
      future: _future,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Center(child: LoadingView());
        }
        if (snapshot.hasError) {
          return Center(
            child: ErrorView(
              message: '${snapshot.error}',
              onRetry: _reload,
            ),
          );
        }
        final data = snapshot.data!;
        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Wrap(
              alignment: WrapAlignment.spaceBetween,
              crossAxisAlignment: WrapCrossAlignment.center,
              spacing: 12,
              runSpacing: 8,
              children: [
                Text('Yêu cầu', style: Theme.of(context).textTheme.headlineSmall),
                Wrap(
                  spacing: 8,
                  children: [
                    FilledButton.icon(
                      key: const Key('btn-create-stock-request'),
                      onPressed: _busy ? null : () => _openCreateStockDialog(data),
                      icon: const Icon(Icons.add_box_outlined, size: 18),
                      label: const Text('Tạo phiếu kho'),
                    ),
                    FilledButton.tonalIcon(
                      key: const Key('btn-create-cash-request'),
                      onPressed: _busy ? null : () => _openCreateCashDialog(data),
                      icon: const Icon(Icons.payments_outlined, size: 18),
                      label: const Text('Tạo phiếu quỹ'),
                    ),
                  ],
                ),
              ],
            ),
            const SizedBox(height: 16),
            Text('Phiếu kho (${data.stockRequests.length})',
                style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 8),
            for (final r in data.stockRequests)
              Card(
                key: Key('stock-request-card-${r.id}'),
                child: ListTile(
                  leading: const Icon(Icons.inventory_2_outlined),
                  title: Text('${r.id} — ${r.kind.name} [${r.status.name}]'),
                  subtitle: Text(
                    'Người tạo: ${r.createdByName}'
                    '${r.reason.isNotEmpty ? ' · Lý do: ${r.reason}' : ''}',
                  ),
                  trailing: r.status == RequestStatus.draft
                      ? FilledButton.tonal(
                          key: Key('btn-submit-stock-${r.id}'),
                          onPressed: _busy ? null : () => _submitStock(r.id),
                          child: const Text('Gửi duyệt'),
                        )
                      : Chip(label: Text(r.status.name)),
                ),
              ),
            const SizedBox(height: 16),
            Text('Phiếu quỹ mô phỏng (${data.cashRequests.length})',
                style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 8),
            for (final r in data.cashRequests)
              Card(
                key: Key('cash-request-card-${r.id}'),
                child: ListTile(
                  leading: const Icon(Icons.savings_outlined),
                  title: Text(
                    '${r.id} — ${r.kind.name} (${r.amount} ${r.currency}) [${r.status.name}]',
                  ),
                  subtitle: Text(
                    'Người tạo: ${r.createdByName}'
                    '${r.reason.isNotEmpty ? ' · Lý do: ${r.reason}' : ''}',
                  ),
                  trailing: r.status == RequestStatus.draft
                      ? FilledButton.tonal(
                          key: Key('btn-submit-cash-${r.id}'),
                          onPressed: _busy ? null : () => _submitCash(r.id),
                          child: const Text('Gửi duyệt'),
                        )
                      : Chip(label: Text(r.status.name)),
                ),
              ),
          ],
        );
      },
    );
  }

  Future<_RequestsData> _load(BankOpsRepository repo) async {
    final results = await Future.wait([
      repo.getStockRequests(),
      repo.getCashRequests(),
      repo.getLocations(),
      repo.getItems(),
      repo.getCashboxes(),
    ]);
    return _RequestsData(
      stockRequests: results[0] as List<StockRequest>,
      cashRequests: results[1] as List<CashRequest>,
      locations: results[2] as List<InventoryLocation>,
      items: results[3] as List<InventoryItem>,
      cashboxes: results[4] as List<Cashbox>,
    );
  }
}

class _RequestsData {
  const _RequestsData({
    required this.stockRequests,
    required this.cashRequests,
    required this.locations,
    required this.items,
    required this.cashboxes,
  });

  final List<StockRequest> stockRequests;
  final List<CashRequest> cashRequests;
  final List<InventoryLocation> locations;
  final List<InventoryItem> items;
  final List<Cashbox> cashboxes;
}
