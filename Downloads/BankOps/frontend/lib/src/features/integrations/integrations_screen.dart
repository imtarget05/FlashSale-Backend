import 'package:flutter/material.dart';

import '../../app/app_shell.dart';
import '../../core/widgets/state_views.dart';
import '../../data/models/dtos.dart';
import '../../data/repositories/bankops_repository.dart';
import 'import_screen.dart';

/// Màn tích hợp — nhập dữ liệu CSV/JSON/Google Sheets (Staging -> Preview -> Confirm) + danh sách kết nối & lịch sử.
class IntegrationsScreen extends StatefulWidget {
  const IntegrationsScreen({super.key});

  @override
  State<IntegrationsScreen> createState() => _IntegrationsScreenState();
}

class _IntegrationsScreenState extends State<IntegrationsScreen> {
  Future<_IntegrationsData>? _future;

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

  @override
  Widget build(BuildContext context) {
    return DefaultTabController(
      length: 2,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
            child: Text(
              'Tích hợp & Nhập dữ liệu',
              style: Theme.of(context).textTheme.headlineSmall,
            ),
          ),
          const TabBar(
            tabs: [
              Tab(
                key: Key('tab-integrations-import'),
                text: 'Nhập dữ liệu (Staging)',
              ),
              Tab(
                key: Key('tab-integrations-history'),
                text: 'Kết nối & Lịch sử',
              ),
            ],
          ),
          Expanded(
            child: TabBarView(
              children: [
                ImportScreen(
                  onConfirmed: (_) => _reload(),
                ),
                FutureBuilder<_IntegrationsData>(
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
                        Text(
                          'Kết nối ngoại vi',
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                        const SizedBox(height: 8),
                        for (final c in data.connections)
                          Card(
                            child: ListTile(
                              leading: const Icon(Icons.hub_outlined),
                              title: Text(c.name),
                              subtitle: Text('${c.endpoint} [${c.status}]'),
                            ),
                          ),
                        const SizedBox(height: 16),
                        Text(
                          'Lịch sử chạy tích hợp (Integration Runs)',
                          style: Theme.of(context).textTheme.titleMedium,
                        ),
                        const SizedBox(height: 8),
                        for (final r in data.runs)
                          Card(
                            child: ListTile(
                              leading: const Icon(Icons.history_outlined),
                              title: Text('${r.id} [${r.status}]'),
                              subtitle: Text(
                                'Kết nối: ${r.connectionId}'
                                '${r.processed != null ? ' · Đã xử lý: ${r.processed}/${r.total ?? r.processed}' : ''}',
                              ),
                            ),
                          ),
                      ],
                    );
                  },
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Future<_IntegrationsData> _load(BankOpsRepository repo) async {
    final results = await Future.wait([
      repo.getConnections(),
      repo.getIntegrationRuns(),
    ]);
    return _IntegrationsData(
      connections: results[0] as List<IntegrationConnection>,
      runs: results[1] as List<IntegrationRun>,
    );
  }
}

class _IntegrationsData {
  const _IntegrationsData({required this.connections, required this.runs});

  final List<IntegrationConnection> connections;
  final List<IntegrationRun> runs;
}
