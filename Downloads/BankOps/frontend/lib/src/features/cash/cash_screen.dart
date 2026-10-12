import 'package:flutter/material.dart';

import '../../app/app_shell.dart';
import '../../core/widgets/demo_banner.dart';
import '../../core/widgets/state_views.dart';
import '../../data/models/dtos.dart';
import '../../data/repositories/bankops_repository.dart';

/// Màn quỹ — cashboxes, balances, journals.
class CashScreen extends StatefulWidget {
  const CashScreen({super.key});

  @override
  State<CashScreen> createState() => _CashScreenState();
}

class _CashScreenState extends State<CashScreen> {
  Future<_CashData>? _future;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final repo = RepositoryScope.of(context);
    _future ??= _load(repo);
  }

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<_CashData>(
      future: _future,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Center(child: LoadingView());
        }
        if (snapshot.hasError) {
          return Center(child: ErrorView(message: '${snapshot.error}'));
        }
        final data = snapshot.data!;
        return ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Text('Quỹ', style: Theme.of(context).textTheme.headlineSmall),
            const SizedBox(height: 16),
            const DemoBanner(),
            const SizedBox(height: 16),
            _section(context, 'Quỹ', data.cashboxes,
                (c) => '${c.code} — ${c.name} (số dư đầu: ${c.openingBalance})'),
            _section(context, 'Số dư', data.balances,
                (b) => '${b.cashboxId}: ${b.amount} ${b.currency}'),
            _section(context, 'Sổ quỹ', data.journals,
                (j) => '${j.kind} ${j.amount} — ${j.reason}'),
          ],
        );
      },
    );
  }

  Widget _section<T>(BuildContext context, String title, List<T> items,
      String Function(T) label) {
    final scheme = Theme.of(context).colorScheme;
    return Card(
      child: ExpansionTile(
        title: Text(title),
        subtitle: Text('${items.length} mục'),
        children: [
          for (final item in items)
            ListTile(
              title: Text(label(item)),
              trailing: Icon(Icons.chevron_right, color: scheme.outline),
            ),
        ],
      ),
    );
  }

  Future<_CashData> _load(BankOpsRepository repo) async {
    final results = await Future.wait([
      repo.getCashboxes(),
      repo.getCashBalances(),
      repo.getJournals(),
    ]);
    return _CashData(
      cashboxes: results[0] as List<Cashbox>,
      balances: results[1] as List<CashBalance>,
      journals: results[2] as List<JournalEntry>,
    );
  }
}

class _CashData {
  const _CashData({
    required this.cashboxes,
    required this.balances,
    required this.journals,
  });

  final List<Cashbox> cashboxes;
  final List<CashBalance> balances;
  final List<JournalEntry> journals;
}
