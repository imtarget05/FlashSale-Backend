import 'package:flutter/material.dart';

import '../../app/app_shell.dart';
import '../../core/widgets/demo_banner.dart';
import '../../core/widgets/state_views.dart';
import '../../data/models/dtos.dart';
import '../../data/repositories/bankops_repository.dart';

class DashboardScreen extends StatefulWidget {
  const DashboardScreen({super.key});

  @override
  State<DashboardScreen> createState() => _DashboardScreenState();
}

class _DashboardScreenState extends State<DashboardScreen> {
  Future<DashboardData>? _future;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final repo = RepositoryScope.of(context);
    _future ??= _load(repo);
  }

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<DashboardData>(
      future: _future,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Center(child: LoadingView());
        }
        if (snapshot.hasError) {
          return Center(child: ErrorView(message: '${snapshot.error}'));
        }
        final data = snapshot.data!;
        return CustomScrollView(
          slivers: [
            const SliverToBoxAdapter(child: _DashboardHeader()),
            SliverPadding(
              padding: const EdgeInsets.all(16),
              sliver: SliverList(
                delegate: SliverChildListDelegate([
                  const DemoBanner(),
                  const SizedBox(height: 16),
                  SummaryCard(data: data),
                  const SizedBox(height: 16),
                ]),
              ),
            ),
            SliverPadding(
              padding: const EdgeInsets.all(16),
              sliver: DashboardKpiGrid(data: data),
            ),
          ],
        );
      },
    );
  }

  Future<DashboardData> _load(BankOpsRepository repo) async {
    final results = await Future.wait([
      repo.getLocations(),
      repo.getItems(),
      repo.getBalances(),
      repo.getAssets(),
      repo.getStockRequests(),
      repo.getApprovalInbox(),
    ]);
    return DashboardData(
      locations: results[0] as List<InventoryLocation>,
      items: results[1] as List<InventoryItem>,
      balances: results[2] as List<StockBalance>,
      assets: results[3] as List<AssetRecord>,
      stockRequests: results[4] as List<StockRequest>,
      inbox: results[5] as List<ApprovalItem>,
    );
  }
}

class _DashboardHeader extends StatelessWidget {
  const _DashboardHeader();

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final textTheme = Theme.of(context).textTheme;
    return Container(
      width: double.infinity,
      color: scheme.surfaceContainerLowest,
      padding: const EdgeInsets.all(24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Tổng quan vận hành',
            style: textTheme.headlineLarge?.copyWith(
              fontWeight: FontWeight.w800,
              fontSize: 32,
            ),
          ),
          const SizedBox(height: 4),
          Text(
            'Kho đang hoạt động',
            style: textTheme.bodyMedium,
          ),
        ],
      ),
    );
  }
}

class DashboardData {
  const DashboardData({
    required this.locations,
    required this.items,
    required this.balances,
    required this.assets,
    required this.stockRequests,
    required this.inbox,
  });

  final List<InventoryLocation> locations;
  final List<InventoryItem> items;
  final List<StockBalance> balances;
  final List<AssetRecord> assets;
  final List<StockRequest> stockRequests;
  final List<ApprovalItem> inbox;
}

class SummaryCard extends StatelessWidget {
  const SummaryCard({super.key, required this.data});

  final DashboardData data;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final textTheme = Theme.of(context).textTheme;
    final value = data.balances.fold<double>(
      0,
      (sum, b) => sum + (double.tryParse(b.quantity) ?? 0),
    );
    final text = '${value.toStringAsFixed(0)} đơn vị tồn kho';
    return Card(
      child: Container(
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          gradient: LinearGradient(
            colors: [
              scheme.surfaceContainerLow,
              scheme.surfaceContainerLowest,
            ],
            begin: Alignment.topLeft,
            end: Alignment.bottomRight,
          ),
        ),
        child: Row(
          children: [
            Icon(Icons.inventory_2_outlined, size: 32, color: scheme.primary),
            const SizedBox(width: 12),
            Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Tồn kho tổng quan',
                  style: textTheme.bodySmall,
                ),
                Text(
                  text,
                  style: textTheme.titleLarge?.copyWith(
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ],
            ),
            const Spacer(),
            Icon(Icons.chevron_right, color: scheme.outline),
          ],
        ),
      ),
    );
  }
}

class DashboardKpiGrid extends StatelessWidget {
  const DashboardKpiGrid({super.key, required this.data});

  final DashboardData data;

  static const _tiles = [
    _KpiTile('Kho', Icons.warehouse_outlined),
    _KpiTile('Vật tư', Icons.category_outlined),
    _KpiTile('Tồn kho', Icons.checklist_outlined),
    _KpiTile('Tài sản', Icons.qr_code_outlined),
    _KpiTile('Yêu cầu', Icons.receipt_long_outlined),
    _KpiTile('Duyệt', Icons.fact_check_outlined),
  ];

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final textTheme = Theme.of(context).textTheme;
    final counts = [
      data.locations.length,
      data.items.length,
      data.balances.length,
      data.assets.length,
      data.stockRequests.length,
      data.inbox.length,
    ];
    return SliverGrid(
      delegate: SliverChildBuilderDelegate(
        (context, index) {
          final tile = _tiles[index];
          final count = counts[index];
          return Card(
            child: Center(
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Icon(tile.icon, size: 32, color: scheme.primary),
                  const SizedBox(height: 4),
                  Text(
                    '$count',
                    style: textTheme.headlineMedium?.copyWith(
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  Text(tile.label, style: textTheme.bodySmall),
                ],
              ),
            ),
          );
        },
        childCount: 6,
      ),
      gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
        crossAxisCount: 2,
        crossAxisSpacing: 12,
        mainAxisSpacing: 12,
        childAspectRatio: 1.1,
      ),
    );
  }
}

class _KpiTile {
  final String label;
  final IconData icon;

  const _KpiTile(this.label, this.icon);
}
