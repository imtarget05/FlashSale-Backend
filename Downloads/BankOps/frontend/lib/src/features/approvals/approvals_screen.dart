import 'package:flutter/material.dart';

import '../../app/app_shell.dart';
import '../../core/widgets/state_views.dart';
import '../../data/models/dtos.dart';

/// Màn duyệt — approval inbox (Maker != Checker).
class ApprovalsScreen extends StatefulWidget {
  const ApprovalsScreen({super.key});

  @override
  State<ApprovalsScreen> createState() => _ApprovalsScreenState();
}

class _ApprovalsScreenState extends State<ApprovalsScreen> {
  Future<List<ApprovalItem>>? _future;
  bool _busy = false;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final repo = RepositoryScope.of(context);
    _future ??= repo.getApprovalInbox();
  }

  void _reload() {
    final repo = RepositoryScope.of(context);
    setState(() {
      _future = repo.getApprovalInbox();
    });
  }

  bool _isCash(ApprovalItem item) =>
      item.kind.toLowerCase() == 'cash' ||
      item.kind.toLowerCase().contains('quỹ');

  Future<void> _approve(ApprovalItem item) async {
    final repo = RepositoryScope.of(context);
    setState(() => _busy = true);
    try {
      if (_isCash(item)) {
        await repo.approveCashRequest(item.id);
      } else {
        await repo.approveStockRequest(item.id);
      }
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Đã duyệt & ghi sổ phiếu ${item.id}')),
      );
      _reload();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Không thể duyệt: $e')),
      );
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _reject(ApprovalItem item) async {
    final repo = RepositoryScope.of(context);
    final reasonCtrl = TextEditingController(text: 'Chưa đủ chứng từ đối chiếu');
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: Text('Từ chối phiếu ${item.id}'),
        content: TextField(
          key: const Key('reject-reason-input'),
          controller: reasonCtrl,
          decoration: const InputDecoration(labelText: 'Lý do từ chối'),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(false),
            child: const Text('Huỷ'),
          ),
          FilledButton(
            key: const Key('reject-dialog-confirm-btn'),
            onPressed: () => Navigator.of(ctx).pop(true),
            child: const Text('Từ chối'),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;

    setState(() => _busy = true);
    try {
      final reason = reasonCtrl.text.trim().isEmpty
          ? 'Từ chối khi kiểm duyệt'
          : reasonCtrl.text.trim();
      if (_isCash(item)) {
        await repo.rejectCashRequest(item.id, reason);
      } else {
        await repo.rejectStockRequest(item.id, reason);
      }
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Đã từ chối phiếu ${item.id}')),
      );
      _reload();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Không thể từ chối: $e')),
      );
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<List<ApprovalItem>>(
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
        final items = snapshot.data!;
        if (items.isEmpty) {
          return const Center(child: EmptyView(title: 'Hộp thư duyệt trống'));
        }
        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Text('Duyệt (${items.length})',
                style: Theme.of(context).textTheme.headlineSmall),
            const SizedBox(height: 16),
            for (final item in items)
              Card(
                key: Key('approval-card-${item.id}'),
                child: Padding(
                  padding: const EdgeInsets.symmetric(vertical: 4),
                  child: ListTile(
                    leading: Icon(
                      _isCash(item)
                          ? Icons.savings_outlined
                          : Icons.inventory_2_outlined,
                    ),
                    title: Text('${item.id} — ${_isCash(item) ? 'Quỹ' : 'Kho'}'),
                    subtitle: Text(
                      'Người tạo: ${item.maker} · Tác động: ${item.impact}'
                      '${item.detail.isNotEmpty ? ' · ${item.detail}' : ''}',
                    ),
                    trailing: Wrap(
                      spacing: 8,
                      children: [
                        OutlinedButton(
                          key: Key('btn-reject-${item.id}'),
                          onPressed: _busy ? null : () => _reject(item),
                          child: const Text('Từ chối'),
                        ),
                        FilledButton(
                          key: Key('btn-approve-${item.id}'),
                          onPressed: _busy ? null : () => _approve(item),
                          child: const Text('Duyệt'),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
          ],
        );
      },
    );
  }
}
