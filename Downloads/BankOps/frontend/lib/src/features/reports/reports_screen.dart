import 'package:flutter/material.dart';

import '../../app/app_shell.dart';
import '../../core/theme/app_theme.dart';
import '../../core/widgets/demo_banner.dart';
import '../../core/widgets/state_views.dart';
import '../../data/models/dtos.dart';
import '../../data/repositories/bankops_repository.dart';
import 'reports_filter.dart';
import 'widgets/cashflow_chart.dart';
import 'widgets/inventory_chart.dart';
import 'widgets/stagnation_list.dart';

/// Màn báo cáo — biểu đồ nhập/xuất kho, dòng tiền mô phỏng, cảnh báo tồn đọng, và đối soát.
class ReportsScreen extends StatefulWidget {
  const ReportsScreen({super.key});

  @override
  State<ReportsScreen> createState() => _ReportsScreenState();
}

class _ReportsScreenState extends State<ReportsScreen> {
  ReportGranularity _granularity = ReportGranularity.daily;
  DateTimeRange? _dateRange;
  Future<_ReportsData>? _future;

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

  Future<_ReportsData> _load(BankOpsRepository repo) async {
    final dateFrom = _dateRange?.start.toIso8601String();
    final dateTo = _dateRange?.end.toIso8601String();
    final invFuture = _granularity == ReportGranularity.daily
        ? repo.getInventoryDaily(dateFrom: dateFrom, dateTo: dateTo)
        : repo.getInventoryMonthly(dateFrom: dateFrom, dateTo: dateTo);
    final cashFuture = _granularity == ReportGranularity.daily
        ? repo.getCashflowDaily(dateFrom: dateFrom, dateTo: dateTo)
        : repo.getCashflowMonthly(dateFrom: dateFrom, dateTo: dateTo);

    final results = await Future.wait([
      invFuture,
      cashFuture,
      repo.getStagnation(),
      repo.getReconciliations(),
    ]);
    return _ReportsData(
      inventoryBuckets: results[0] as List<InventoryBucket>,
      cashflowBuckets: results[1] as List<CashflowBucket>,
      stagnationItems: results[2] as List<StagnationItem>,
      reconciliations: results[3] as List<ReconciliationRun>,
    );
  }

  @override
  Widget build(BuildContext context) {
    return DefaultTabController(
      length: 4,
      child: FutureBuilder<_ReportsData>(
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
          return Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'Báo cáo & Biểu đồ',
                      style: Theme.of(context).textTheme.headlineSmall,
                    ),
                    const SizedBox(height: 12),
                    const DemoBanner(),
                    const SizedBox(height: 12),
                    ReportsFilter(
                      granularity: _granularity,
                      onGranularityChanged: (g) {
                        _granularity = g;
                        _reload();
                      },
                      dateRange: _dateRange,
                      onDateRangeChanged: (range) {
                        _dateRange = range;
                        _reload();
                      },
                    ),
                  ],
                ),
              ),
              const TabBar(
                isScrollable: true,
                tabs: [
                  Tab(key: Key('tab-reports-inventory'), text: 'Nhập/Xuất kho'),
                  Tab(key: Key('tab-reports-cashflow'), text: 'Dòng tiền mô phỏng'),
                  Tab(key: Key('tab-reports-stagnation'), text: 'Tồn đọng'),
                  Tab(key: Key('tab-reports-reconciliation'), text: 'Đối soát'),
                ],
              ),
              Expanded(
                child: TabBarView(
                  children: [
                    SingleChildScrollView(
                      padding: const EdgeInsets.all(16),
                      child: InventoryChart(buckets: data.inventoryBuckets),
                    ),
                    SingleChildScrollView(
                      padding: const EdgeInsets.all(16),
                      child: CashflowChart(buckets: data.cashflowBuckets),
                    ),
                    SingleChildScrollView(
                      padding: const EdgeInsets.all(16),
                      child: StagnationList(items: data.stagnationItems),
                    ),
                    _ReconciliationTab(runs: data.reconciliations),
                  ],
                ),
              ),
            ],
          );
        },
      ),
    );
  }
}

class _ReconciliationTab extends StatelessWidget {
  const _ReconciliationTab({required this.runs});

  final List<ReconciliationRun> runs;

  @override
  Widget build(BuildContext context) {
    if (runs.isEmpty) {
      return const Center(child: EmptyView(title: 'Chưa có báo cáo đối soát'));
    }
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        for (final r in runs)
          Card(
            child: ListTile(
              title: Text('${r.id} [${r.status}]'),
              subtitle: Text('Ngoại lệ: ${r.exceptions.join(', ')}'),
              trailing: Icon(
                r.exceptions.isEmpty
                    ? Icons.check_circle_outline
                    : Icons.error_outline,
                color: r.exceptions.isEmpty
                    ? context.statusColors.success
                    : context.statusColors.danger,
              ),
            ),
          ),
      ],
    );
  }
}

class _ReportsData {
  const _ReportsData({
    required this.inventoryBuckets,
    required this.cashflowBuckets,
    required this.stagnationItems,
    required this.reconciliations,
  });

  final List<InventoryBucket> inventoryBuckets;
  final List<CashflowBucket> cashflowBuckets;
  final List<StagnationItem> stagnationItems;
  final List<ReconciliationRun> reconciliations;
}
