import 'package:flutter/material.dart';

import '../theme/app_theme.dart';
import '../theme/app_tokens.dart';

class LoadingView extends StatelessWidget {
  const LoadingView({super.key, this.itemCount = 3, this.width, this.label = 'Đang tải…'});

  final int itemCount;
  final double? width;
  final String label;

  @override
  Widget build(BuildContext context) {
    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        _shimmerRow(context, 0, width),
        for (var i = 0; i < itemCount; i++)
          Padding(
            padding: const EdgeInsets.symmetric(vertical: AppSpacing.sm),
            child: _shimmerRow(context, i, width),
          ),
      ],
    );
  }

  Widget _shimmerRow(BuildContext context, int index, double? width) {
    final shimmer = Theme.of(context).colorScheme.surfaceContainerHighest
        .withValues(alpha: 0.55);
    return Container(
      height: 72,
      width: width ?? double.infinity,
      margin: const EdgeInsets.symmetric(vertical: AppSpacing.xs),
      decoration: BoxDecoration(
        color: shimmer,
        borderRadius: BorderRadius.circular(AppRadius.md),
      ),
      child: Center(
        child: index == 0
            ? const LinearProgressIndicator(minHeight: 3, color: Colors.indigo)
            : null,
      ),
    );
  }
}

/// Màn trạng thái trống.
class EmptyView extends StatelessWidget {
  const EmptyView({
    super.key,
    required this.title,
    this.subtitle,
    this.icon = Icons.inbox_outlined,
    this.action,
  });

  final String title;
  final String? subtitle;
  final IconData icon;
  final Widget? action;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 48, color: scheme.outline),
          const SizedBox(height: AppSpacing.md),
          Text(title,
              style: TextStyle(color: scheme.onSurfaceVariant, fontSize: 16)),
          if (subtitle != null) ...[
            const SizedBox(height: AppSpacing.xs),
            Text(subtitle!,
                textAlign: TextAlign.center,
                style: TextStyle(color: scheme.onSurfaceVariant, fontSize: 13)),
          ],
          if (action != null) ...[
            const SizedBox(height: AppSpacing.lg),
            action!,
          ],
        ],
      ),
    );
  }
}

/// Màn hiển thị lỗi kèm nút thử lại.
class ErrorView extends StatelessWidget {
  const ErrorView({
    super.key,
    required this.message,
    this.onRetry,
    this.retryLabel = 'Thử lại',
  });

  final String message;
  final VoidCallback? onRetry;
  final String retryLabel;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.error_outline, size: 48, color: context.statusColors.danger),
          const SizedBox(height: AppSpacing.md),
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: AppSpacing.xl),
            child: Text(message,
                textAlign: TextAlign.center,
                style: TextStyle(color: scheme.onSurface, fontSize: 16)),
          ),
          if (onRetry != null) ...[
            const SizedBox(height: AppSpacing.lg),
            FilledButton.icon(
              onPressed: onRetry,
              icon: const Icon(Icons.refresh),
              label: Text(retryLabel),
            ),
          ],
        ],
      ),
    );
  }
}
