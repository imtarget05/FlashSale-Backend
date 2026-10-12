import 'package:flutter/material.dart';

import '../theme/app_tokens.dart';

/// Thẻ KPI trong dashboard: icon + số + nhãn, có nút click để đi sâu.
class MetricTile extends StatelessWidget {
  const MetricTile({
    super.key,
    required this.icon,
    required this.label,
    required this.value,
    this.sublabel,
    this.accent = false,
    this.onTap,
    this.valueTextStyle,
  });

  final IconData icon;
  final String label;
  final String value;
  final String? sublabel;
  final bool accent;
  final VoidCallback? onTap;
  final TextStyle? valueTextStyle;

  @override
  Widget build(BuildContext context) {
    final sc = Theme.of(context);
    final bg = accent ? sc.colorScheme.primaryContainer : sc.colorScheme.surfaceContainerLow;
    final fg = accent ? sc.colorScheme.primary : sc.colorScheme.onSurface;
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(AppRadius.md),
      child: Container(
        decoration: BoxDecoration(
          color: bg,
          borderRadius: BorderRadius.circular(AppRadius.md),
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 26, color: fg),
            const SizedBox(height: AppSpacing.xs),
            Flexible(
              child: Text(
                value,
                style: valueTextStyle ??
                    sc.textTheme.headlineMedium?.copyWith(
                      fontWeight: FontWeight.w800,
                      color: fg,
                    ),
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ),
            Text(
              label,
              style: sc.textTheme.bodySmall?.copyWith(color: fg),
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
            ),
            if (sublabel != null) ...[
              const SizedBox(height: AppSpacing.xs),
              Text(
                sublabel!,
                style: sc.textTheme.bodySmall?.copyWith(
                  color: sc.colorScheme.onSurfaceVariant,
                ),
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ],
          ],
        ),
      ),
    );
  }
}
