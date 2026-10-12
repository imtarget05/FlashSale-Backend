import 'package:flutter/material.dart';

import '../../../core/theme/app_theme.dart';
import '../../../core/widgets/state_views.dart';
import '../../../data/models/dtos.dart';

class StagnationList extends StatelessWidget {
  const StagnationList({super.key, required this.items});

  final List<StagnationItem> items;

  @override
  Widget build(BuildContext context) {
    if (items.isEmpty) {
      return const EmptyView(title: 'Không có hàng tồn đọng cần cảnh báo');
    }
    final statusColors = context.statusColors;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (final item in items)
          Card(
            key: Key('stagnation-item-${item.sku}'),
            child: ListTile(
              leading: Icon(
                _iconFor(item.severity),
                color: _colorFor(item.severity, statusColors),
              ),
              title: Text('${item.name} (${item.sku})'),
              subtitle: Text(
                item.daysSinceLastIssue == null
                    ? 'Chưa từng xuất kho · Tồn: ${item.quantity}'
                    : '${item.daysSinceLastIssue} ngày từ lần xuất gần nhất · Tồn: ${item.quantity}',
              ),
              trailing: Chip(
                label: Text(
                  _labelFor(item.severity),
                  style: TextStyle(
                    color: _colorFor(item.severity, statusColors),
                    fontWeight: FontWeight.w700,
                  ),
                ),
                side: BorderSide(color: _colorFor(item.severity, statusColors)),
              ),
            ),
          ),
      ],
    );
  }

  IconData _iconFor(String severity) {
    switch (severity) {
      case 'critical':
        return Icons.error_outline;
      case 'stagnant':
        return Icons.warning_amber_rounded;
      case 'watch':
        return Icons.visibility_outlined;
      default:
        return Icons.check_circle_outline;
    }
  }

  Color _colorFor(String severity, AppStatusColors colors) {
    switch (severity) {
      case 'critical':
        return colors.danger;
      case 'stagnant':
      case 'watch':
        return colors.warning;
      default:
        return colors.success;
    }
  }

  String _labelFor(String severity) {
    switch (severity) {
      case 'critical':
        return 'Nghiêm trọng (>=90 ngày)';
      case 'stagnant':
        return 'Tồn đọng (>=60 ngày)';
      case 'watch':
        return 'Theo dõi (>=30 ngày)';
      default:
        return 'Bình thường';
    }
  }
}
