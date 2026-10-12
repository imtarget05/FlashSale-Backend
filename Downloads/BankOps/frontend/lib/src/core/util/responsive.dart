import 'package:flutter/material.dart';

import '../theme/app_tokens.dart';

/// Helpers responsive dùng chung: padding theo breakpoint, số cột grid,
/// chiều rộng nội dung tối đa — thay thế hardcode trong từng screen.
extension ResponsiveContext on BuildContext {
  DeviceLayout get layout => Breakpoints.of(this);
  bool get isCompact => layout.isCompact;
  bool get isMedium => layout.isMedium;
  bool get isWide => layout.isWide;

  /// Padding trang: 16 (compact) / 20 (medium) / 28 (wide).
  EdgeInsets get pagePadding => switch (layout) {
        DeviceLayout.compact => const EdgeInsets.all(AppSpacing.lg),
        DeviceLayout.medium => const EdgeInsets.symmetric(
            horizontal: AppSpacing.xl,
            vertical: AppSpacing.lg,
          ),
        DeviceLayout.wide => const EdgeInsets.symmetric(
            horizontal: AppSpacing.xxl,
            vertical: AppSpacing.xl,
          ),
      };

  /// Số cột grid theo bề rộng: 2 / 3 / 4.
  int gridColumns({int wide = 4, int medium = 3, int compact = 2}) =>
      switch (layout) {
        DeviceLayout.compact => compact,
        DeviceLayout.medium => medium,
        DeviceLayout.wide => wide,
      };

  /// Chiều rộng nội dung tối đa — giữa màn rộng nội dung không bị trải mỏng.
  double get contentMaxWidth => switch (layout) {
        DeviceLayout.compact => double.infinity,
        DeviceLayout.medium => 960,
        DeviceLayout.wide => 1120,
      };
}

/// Bọc nội dung: giới hạn maxWidth + canh giữa. Trả child nguyên trạng
/// nếu nội dung đã vừa khung (tránh padding thừa ở compact).
class ContentMaxWidth extends StatelessWidget {
  const ContentMaxWidth({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    final max = context.contentMaxWidth;
    if (max == double.infinity) return child;
    return Center(
      child: ConstrainedBox(
        constraints: BoxConstraints(maxWidth: max),
        child: child,
      ),
    );
  }
}
