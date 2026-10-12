import 'package:flutter/material.dart';

import '../theme/app_tokens.dart';

/// Dòng nhãn – giá trị: dùng cho mọi danh sách, card chi tiết, sổ quỹ.
class InfoRow extends StatelessWidget {
  const InfoRow({
    super.key,
    required this.label,
    required this.value,
    this.accent = false,
    this.icon,
  });

  final String label;
  final String value;
  final bool accent;
  final IconData? icon;

  @override
  Widget build(BuildContext context) {
    final sc = Theme.of(context);
    final valueStyle = accent
        ? sc.textTheme.titleMedium?.copyWith(
            fontWeight: FontWeight.w700,
            color: sc.colorScheme.primary,
          )
        : sc.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.w600);
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        if (icon != null) ...[
          Icon(icon, size: 16, color: sc.colorScheme.onSurfaceVariant),
          const SizedBox(width: AppSpacing.xs),
        ],
        Text(label, style: sc.textTheme.bodySmall, maxLines: 1, overflow: TextOverflow.ellipsis),
        const SizedBox(width: AppSpacing.md),
        if (icon != null) const SizedBox(width: AppSpacing.xs),
        Flexible(
          child: Text(
            value,
            style: valueStyle,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
          ),
        ),
      ],
    );
  }
}
