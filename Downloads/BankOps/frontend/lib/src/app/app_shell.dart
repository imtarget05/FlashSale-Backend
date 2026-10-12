import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../data/models/dtos.dart';
import '../data/repositories/bankops_repository.dart';
import '../core/theme/app_tokens.dart';
import '../core/widgets/app_screen.dart';
import '../core/widgets/demo_banner.dart';
import '../core/widgets/user_avatar.dart';
import '../data/repositories/http_bankops_repository.dart';
import '../features/activity/activity_screen.dart';
import '../features/approvals/approvals_screen.dart';
import '../features/auth/auth_service.dart';
import '../features/cash/cash_screen.dart';
import '../features/dashboard/dashboard_screen.dart';
import '../features/inventory/inventory_screen.dart';
import '../features/integrations/integrations_screen.dart';
import '../features/reports/reports_screen.dart';
import '../features/requests/requests_screen.dart';
import '../features/settings/settings_screen.dart';

class SessionScope extends InheritedWidget {
  const SessionScope({super.key, required super.child, this.user, this.onSignOut});
  final AppUser? user;
  final Future<void> Function()? onSignOut;

  static SessionScope? maybeOf(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<SessionScope>();

  @override
  bool updateShouldNotify(SessionScope old) =>
      old.user != user || old.onSignOut != onSignOut;
}

class RepositoryScope extends InheritedWidget {
  const RepositoryScope({super.key, required this.repository, required super.child});

  final BankOpsRepository repository;

  static BankOpsRepository of(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<RepositoryScope>()!.repository;

  @override
  bool updateShouldNotify(RepositoryScope old) => repository != old.repository;
}

/// Shell 3-tier theo docs/07-flutter.md:
/// <600 bottom nav 4 mục + More | 600–1199 rail | >=1200 sidebar + topbar.
class AppShell extends StatefulWidget {
  const AppShell({super.key, this.repository});

  final BankOpsRepository? repository;

  @override
  State<AppShell> createState() => _AppShellState();
}

class _AppShellState extends State<AppShell> {
  BankOpsRepository? _repository;
  int _index = 0;
  int _refreshTick = 0;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (widget.repository != null) {
      _repository = widget.repository;
    } else if (_repository == null) {
      final auth = Provider.of<AuthService?>(context, listen: false);
      _repository = HttpBankOpsRepository(
        baseUrl: auth?.apiBaseUrl,
        tokenProvider: auth?.getAccessToken,
      );
    }
  }

  static const _items = <_Item>[
    _Item(0, 'Tổng quan', Icons.dashboard_outlined, Icons.dashboard),
    _Item(1, 'Kho', Icons.inventory_2_outlined, Icons.inventory_2),
    _Item(2, 'Quỹ', Icons.savings_outlined, Icons.savings),
    _Item(3, 'Yêu cầu', Icons.receipt_long_outlined, Icons.receipt_long),
    _Item(4, 'Duyệt', Icons.fact_check_outlined, Icons.fact_check),
    _Item(5, 'Tích hợp', Icons.hub_outlined, Icons.hub),
    _Item(6, 'Hoạt động', Icons.timeline_outlined, Icons.timeline),
    _Item(7, 'Báo cáo', Icons.insert_chart_outlined, Icons.insert_chart),
    _Item(8, 'Cài đặt', Icons.settings_outlined, Icons.settings),
  ];

  static const _slugs = <String>[
    'dashboard',
    'inventory',
    'cash',
    'requests',
    'approvals',
    'integrations',
    'activity',
    'reports',
    'settings',
  ];

  static const _titles = <String>[
    'Tổng quan',
    'Kho',
    'Quỹ',
    'Yêu cầu',
    'Duyệt',
    'Tích hợp',
    'Hoạt động',
    'Báo cáo',
    'Cài đặt',
  ];

  static const _icons = <IconData>[
    Icons.dashboard_outlined,
    Icons.inventory_2_outlined,
    Icons.savings_outlined,
    Icons.receipt_long_outlined,
    Icons.fact_check_outlined,
    Icons.hub_outlined,
    Icons.timeline_outlined,
    Icons.insert_chart_outlined,
    Icons.settings_outlined,
  ];

  static const _filled = <IconData>[
    Icons.dashboard,
    Icons.inventory_2,
    Icons.savings,
    Icons.receipt_long,
    Icons.fact_check,
    Icons.hub,
    Icons.timeline,
    Icons.insert_chart,
    Icons.settings,
  ];

  Widget _screenFor(int i) {
    switch (i) {
      case 0:
        return const DashboardScreen();
      case 1:
        return const InventoryScreen();
      case 2:
        return const CashScreen();
      case 3:
        return const RequestsScreen();
      case 4:
        return const ApprovalsScreen();
      case 5:
        return const IntegrationsScreen();
      case 6:
        return const ActivityScreen();
      case 7:
        return const ReportsScreen();
      case 8:
        return const SettingsScreen();
      default:
        return const SizedBox.shrink();
    }
  }

  Future<void> _reload() async {
    setState(() => _refreshTick++);
  }

  void _openSettings() {
    Navigator.of(context).push(
      PageRouteBuilder(
        pageBuilder: (context, _, __) => const SettingsScreen(),
        transitionsBuilder: (context, animation, _, child) {
          const begin = Offset(1.0, 0.0);
          const end = Offset.zero;
          final tween = Tween(begin: begin, end: end)
              .chain(CurveTween(curve: Curves.easeInOut));
          return SlideTransition(position: animation.drive(tween), child: child);
        },
      ),
    );
  }

  Future<void> _signOut() async {
    await SessionScope.maybeOf(context)?.onSignOut?.call();
    if (mounted) setState(() => _index = 0);
  }

  /// Bar chỉ có 5 slot: 0-2 map thẳng _index, slot 3 = Duyệt (_index 4),
  /// slot 4 = Thêm (mở sheet, không phải destination điều hướng).
  int _bottomSelected() {
    if (_index >= 0 && _index <= 2) return _index;
    if (_index == 4) return 3;
    return 4;
  }

  Widget _destination(String label, int index,
      {IconData? icon, IconData? selectedIcon, Key? key}) {
    return NavigationDestination(
      key: key,
      icon: Icon(icon ?? _icons[index], size: 22),
      selectedIcon: Icon(selectedIcon ?? _filled[index], size: 22),
      label: label,
    );
  }

  Widget _bottomBar() {
    return NavigationBar(
      selectedIndex: _bottomSelected(),
      onDestinationSelected: (i) {
        if (i == 4) {
          _openMoreSheet();
          return;
        }
        setState(() => _index = i == 3 ? 4 : i);
      },
      height: 64,
      labelBehavior: NavigationDestinationLabelBehavior.alwaysShow,
      destinations: [
        _destination('Tổng quan', 0, key: const Key('nav-bottom-dashboard')),
        _destination('Kho', 1, key: const Key('nav-bottom-inventory')),
        _destination('Quỹ', 2, key: const Key('nav-bottom-cash')),
        _destination('Duyệt', 4, key: const Key('nav-bottom-approvals')),
        _destination('Thêm', 4,
            icon: Icons.more_horiz_outlined,
            selectedIcon: Icons.expand_more,
            key: const Key('nav-bottom-more')),
      ],
    );
  }

  void _openMoreSheet() {
    showModalBottomSheet<void>(
      context: context,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(16)),
      ),
      builder: (context) => Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const ListTile(title: Text('Chọn màn'), leading: Icon(Icons.list_alt_outlined)),
          const Divider(),
          ..._items.asMap().entries.map((e) => ListTile(
                leading: const Icon(Icons.arrow_forward_ios, size: 16),
                title: Text(_titles[e.key]),
                dense: true,
                onTap: () {
                  Navigator.of(context).pop();
                  setState(() => _index = e.key);
                },
              )),
        ],
      ),
    );
  }

  Widget _navRail({required double width}) {
    return Container(
      width: width,
      color: Theme.of(context).colorScheme.surface,
      child: CustomScrollView(
        slivers: [
          SliverToBoxAdapter(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(12, 10, 12, 8),
              child: Row(
                children: [
                  Icon(Icons.account_balance_wallet_outlined, size: 20, color: Theme.of(context).colorScheme.primary),
                  const SizedBox(width: 6),
                  Text('BankOps',
                      style: Theme.of(context).textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w800)),
                  const Spacer(),
                  Container(width: 8, height: 8, decoration: BoxDecoration(color: Theme.of(context).colorScheme.primaryContainer, shape: BoxShape.circle)),
                  const SizedBox(width: 6),
                  Text('DEMO', style: Theme.of(context).textTheme.labelSmall?.copyWith(color: Theme.of(context).colorScheme.primaryContainer, fontWeight: FontWeight.w700)),
                ],
              ),
            ),
          ),
          SliverList(
            delegate: SliverChildBuilderDelegate(
              (context, i) {
                final item = _items[i];
                final selected = _index == item.index;
                final sc = Theme.of(context);
                return Container(
                  height: 52,
                  margin: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                  decoration: BoxDecoration(color: selected ? sc.colorScheme.primaryContainer : Colors.transparent, borderRadius: BorderRadius.circular(10)),
                  child: InkWell(
                    key: Key('nav-${_slugs[item.index]}'),
                    onTap: () => setState(() => _index = item.index),
                    borderRadius: BorderRadius.circular(10),
                    child: Padding(
                      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
                      child: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Icon(item.icon, size: 22, color: selected ? sc.colorScheme.primary : sc.colorScheme.onSurfaceVariant),
                          const SizedBox(width: 10),
                          Text(item.label, style: TextStyle(color: selected ? sc.colorScheme.onPrimaryContainer : sc.colorScheme.onSurfaceVariant, fontWeight: FontWeight.w600)),
                        ],
                      ),
                    ),
                  ),
                );
              },
              childCount: _items.length,
            ),
          ),
        ],
      ),
    );
  }

  PreferredSizeWidget _topBar({SessionScope? session}) {
    return AppBar(
      title: Text(_titles[_index]),
      centerTitle: true,
      backgroundColor: Theme.of(context).colorScheme.surface,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      scrolledUnderElevation: 1,
      actions: [
        const DemoBanner(),
        const SizedBox(width: 8),
        IconButton(
          key: const Key('topbar-refresh-btn'),
          icon: const Icon(Icons.refresh_outlined),
          onPressed: _reload,
          tooltip: 'Làm mới',
        ),
        IconButton(icon: const Icon(Icons.settings_outlined), onPressed: _openSettings, tooltip: 'Cài đặt'),
        const SizedBox(width: 6),
        PopupMenuButton<String>(
          key: const Key('topbar-user-menu'),
          icon: session?.user != null
              ? UserAvatar(name: session!.user!.displayName, size: 34)
              : const UserAvatar(size: 34),
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
          tooltip: session?.user?.displayName ?? 'Người dùng',
          itemBuilder: (context) => [
            if (session?.user != null) ...[
              const PopupMenuItem(
                value: 'settings',
                child: ListTile(leading: Icon(Icons.settings, size: 18), title: Text('Cài đặt'), contentPadding: EdgeInsets.zero),
              ),
              const PopupMenuDivider(),
            ],
            const PopupMenuItem(
              key: Key('menu-signout'),
              value: 'signout',
              child: ListTile(leading: Icon(Icons.exit_to_app, size: 18), title: Text('Đăng xuất'), contentPadding: EdgeInsets.zero),
            ),
          ],
          onSelected: (v) {
            if (v == 'settings') _openSettings();
            if (v == 'signout') _signOut();
          },
        ),
      ],
    );
  }

  @override
  Widget build(BuildContext context) {
    final session = SessionScope.maybeOf(context);
    final layout = Breakpoints.fromWidth(MediaQuery.sizeOf(context).width);

    final body = AppScreen(
      body: KeyedSubtree(
        key: ValueKey('screen-$_index-$_refreshTick-${session?.user?.id ?? ''}'),
        child: _screenFor(_index),
      ),
      header: _index == 0 ? const _DashboardHeader() : null,
      onRefresh: _reload,
    );

    if (layout.isWide) return _wide(session, body);
    if (layout.isMedium) return _medium(session, body);
    return _compact(session, body);
  }

  Widget _wide(SessionScope? session, Widget body) {
    return RepositoryScope(repository: _repository!, child: Scaffold(
      body: Row(
        children: [
          _navRail(width: 256),
          const VerticalDivider(width: 8, thickness: 1),
          Expanded(
            child: Column(
              children: [
                _topBar(session: session),
                Expanded(child: body),
              ],
            ),
          ),
        ],
      ),
    ));
  }

  Widget _medium(SessionScope? session, Widget body) {
    return RepositoryScope(repository: _repository!, child: Scaffold(
      appBar: _topBar(session: session),
      body: Row(
        children: [
          _navRail(width: 88),
          const VerticalDivider(width: 8, thickness: 1),
          Expanded(child: body),
        ],
      ),
    ));
  }

  Widget _compact(SessionScope? session, Widget body) {
    return RepositoryScope(repository: _repository!, child: Scaffold(
      appBar: _topBar(session: session),
      body: body,
      bottomNavigationBar: _bottomBar(),
    ));
  }
}

class _DashboardHeader extends StatelessWidget {
  const _DashboardHeader();

  @override
  Widget build(BuildContext context) {
    final sc = Theme.of(context);
    final auth = context.watch<AuthService>();
    return Container(
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
      decoration: BoxDecoration(color: sc.colorScheme.surfaceContainerLow, borderRadius: BorderRadius.circular(AppRadius.md)),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          UserAvatar(name: auth.user?.displayName, size: 36),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text('Xin chào, ${auth.user?.displayName ?? 'người dùng'}',
                    style: sc.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w700)),
                Text('Chức năng của bạn', style: sc.textTheme.bodySmall),
              ],
            ),
          ),
          const Icon(Icons.keyboard_arrow_down_outlined, size: 20, color: Colors.grey),
        ],
      ),
    );
  }
}

class _Item {
  const _Item(this.index, this.label, this.icon, this.filled);
  final int index;
  final String label;
  final IconData icon;
  final IconData filled;
}

