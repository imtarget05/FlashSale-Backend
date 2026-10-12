import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../app/theme_mode_controller.dart';
import '../../core/theme/app_tokens.dart';
import '../../core/widgets/section_header.dart';
import '../auth/auth_service.dart';

/// Màn Cài đặt (standalone, không phụ thuộc shell nav):
/// chọn theme + thông tin bản demo + đăng xuất.
class SettingsScreen extends StatelessWidget {
  const SettingsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final sc = Theme.of(context);
    final auth = context.watch<AuthService>();
    final themeController = context.watch<ThemeModeController>();
    return Scaffold(
      appBar: AppBar(
        title: const Text('Cài đặt'),
        centerTitle: true,
      ),
      body: ListView(
        padding: const EdgeInsets.all(AppSpacing.lg),
        children: [
          const SectionHeader(title: 'Giao diện', showCount: false),
          const SizedBox(height: AppSpacing.md),
          Card(
            child: Padding(
              padding: const EdgeInsets.all(AppSpacing.lg),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Chế độ hiển thị', style: sc.textTheme.titleSmall),
                  const SizedBox(height: AppSpacing.sm),
                  SegmentedButton<ThemeMode>(
                    segments: const [
                      ButtonSegment(
                        value: ThemeMode.system,
                        icon: Icon(Icons.settings_suggest_outlined, size: 18),
                        label: Text('Hệ thống'),
                      ),
                      ButtonSegment(
                        value: ThemeMode.light,
                        icon: Icon(Icons.light_mode_outlined, size: 18),
                        label: Text('Sáng'),
                      ),
                      ButtonSegment(
                        value: ThemeMode.dark,
                        icon: Icon(Icons.dark_mode_outlined, size: 18),
                        label: Text('Tối'),
                      ),
                    ],
                    selected: {themeController.mode},
                    onSelectionChanged: (modes) =>
                        themeController.setMode(modes.first),
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: AppSpacing.lg),
          const SectionHeader(title: 'Tài khoản', showCount: false),
          const SizedBox(height: AppSpacing.md),
          Card(
            child: ListTile(
              leading: const Icon(Icons.person_outline),
              title: Text(auth.user?.displayName ?? 'Khách'),
              subtitle: Text((auth.user?.roles ?? const []).join(', ')),
            ),
          ),
          const SizedBox(height: AppSpacing.lg),
          const SectionHeader(title: 'Ứng dụng', showCount: false),
          const SizedBox(height: AppSpacing.md),
          Card(
            child: Column(
              children: [
                const ListTile(
                  leading: Icon(Icons.info_outline),
                  title: Text('BankOps 0.1.0+1'),
                  subtitle: Text('Bản demo — mọi dữ liệu là giả lập.'),
                ),
                const Divider(height: 1),
                ListTile(
                  leading: const Icon(Icons.exit_to_app),
                  title: const Text('Đăng xuất'),
                  onTap: () async {
                    await context.read<AuthService>().signOut();
                    if (context.mounted) {
                      Navigator.of(context).popUntil((route) => route.isFirst);
                    }
                  },
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
