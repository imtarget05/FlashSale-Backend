import 'package:flutter/material.dart';

import '../theme/app_tokens.dart';
import '../util/responsive.dart';

/// Bọc nội dung màn: padding theo breakpoint, nội dung giới hạn width,
/// refresh toàn cục.
///
/// Shell (`AppShell`) cung cấp AppBar + rail/bottom nav bên cạnh.
/// Màn chỉ cần truyền `body` (scrollable) và `header` tùy ý.
class AppScreen extends StatelessWidget {
  const AppScreen({
    super.key,
    required this.body,
    this.header,
    this.onRefresh,
  });

  final Widget body;
  final Widget? header;
  final RefreshCallback? onRefresh;

  @override
  Widget build(BuildContext context) {
    return Container(
      constraints: const BoxConstraints(maxWidth: 1120),
      padding: context.pagePadding,
      child: RefreshIndicator(
        onRefresh: onRefresh ?? () async {},
        child: Column(
          children: [
            if (header != null) ...[
              header!,
              const SizedBox(height: AppSpacing.xxl),
            ],
            Expanded(child: body),
          ],
        ),
      ),
    );
  }
}
