import 'package:flutter/material.dart';

import '../../app/app_shell.dart';
import '../../core/widgets/demo_banner.dart';
import '../../core/widgets/state_views.dart';
import '../../data/models/dtos.dart';
import '../../data/repositories/bankops_repository.dart';

/// Màn quản lý kho — locations, items, balances, assets.
class InventoryScreen extends StatefulWidget {
  const InventoryScreen({super.key});

  @override
  State<InventoryScreen> createState() => _InventoryScreenState();
}

class _InventoryScreenState extends State<InventoryScreen> {
  Future<_InventoryData>? _future;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final repo = RepositoryScope.of(context);
    _future ??= _load(repo);
  }

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<_InventoryData>(
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
            Text('Kho', style: Theme.of(context).textTheme.headlineSmall),
            const SizedBox(height: 16),
            const DemoBanner(),
            const SizedBox(height: 16),
            _section(context, 'Địa điểm', data.locations,
                (l) => '${l.code} — ${l.name}'),
            _section(context, 'Vật tư', data.items,
                (i) => '${i.sku} — ${i.name} (${i.unit})'),
            _section(context, 'Tồn kho', data.balances,
                (b) => '${b.locationId} / ${b.itemId}: ${b.quantity}'),
            _section(context, 'Tài sản', data.assets,
                (a) => '${a.serial} [${a.status.name}]'),
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

  Future<_InventoryData> _load(BankOpsRepository repo) async {
    final results = await Future.wait([
      repo.getLocations(),
      repo.getItems(),
      repo.getBalances(),
      repo.getAssets(),
    ]);
    return _InventoryData(
      locations: results[0] as List<InventoryLocation>,
      items: results[1] as List<InventoryItem>,
      balances: results[2] as List<StockBalance>,
      assets: results[3] as List<AssetRecord>,
    );
  }
}

class _InventoryData {
  const _InventoryData({
    required this.locations,
    required this.items,
    required this.balances,
    required this.assets,
  });

  final List<InventoryLocation> locations;
  final List<InventoryItem> items;
  final List<StockBalance> balances;
  final List<AssetRecord> assets;
}
