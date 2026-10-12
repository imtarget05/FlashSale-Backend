import 'package:flutter/material.dart';

import '../../core/theme/app_theme.dart';
import '../../core/widgets/state_views.dart';
import '../../data/models/dtos.dart';

class ActivityCenter extends StatelessWidget {
  const ActivityCenter({
    super.key,
    required this.events,
    this.onRefresh,
  });

  final List<ActivityEvent> events;
  final VoidCallback? onRefresh;

  @override
  Widget build(BuildContext context) {
    if (events.isEmpty) {
      return const Center(child: EmptyView(title: 'Chưa có hoạt động nào'));
    }
    final statusColors = context.statusColors;

    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Row(
          children: [
            Expanded(
              child: Text(
                'Hoạt động & Tiến trình nền',
                style: Theme.of(context).textTheme.headlineSmall,
              ),
            ),
            if (onRefresh != null)
              OutlinedButton.icon(
                key: const Key('activity-resync-btn'),
                onPressed: onRefresh,
                icon: const Icon(Icons.sync, size: 18),
                label: const Text('Đồng bộ lại'),
              ),
          ],
        ),
        const SizedBox(height: 16),
        for (final e in events)
          Card(
            key: Key('activity-item-${e.id}'),
            margin: const EdgeInsets.only(bottom: 10),
            child: Padding(
              padding: const EdgeInsets.symmetric(vertical: 4),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  ListTile(
                    leading: Icon(
                      _iconFor(e.status),
                      color: _colorFor(e.status, statusColors),
                    ),
                    title: Text('${e.id} [${e.status}]'),
                    subtitle: Text(
                      '${e.subjectId} — ${e.messageKey} · ${e.occurredAt.toIso8601String().substring(0, 16).replaceFirst('T', ' ')}',
                    ),
                    trailing: e.processed != null && e.total != null
                        ? Text(
                            '${e.processed}/${e.total}',
                            style: Theme.of(context)
                                .textTheme
                                .labelLarge
                                ?.copyWith(fontWeight: FontWeight.w700),
                          )
                        : null,
                  ),
                  if (e.processed != null &&
                      e.total != null &&
                      e.total! > 0)
                    Padding(
                      padding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
                      child: LinearProgressIndicator(
                        value: (e.processed! / e.total!).clamp(0.0, 1.0),
                      ),
                    ),
                ],
              ),
            ),
          ),
      ],
    );
  }

  IconData _iconFor(String status) {
    switch (status) {
      case 'posted':
      case 'succeeded':
      case 'confirmed':
      case 'created':
        return Icons.check_circle_outline;
      case 'pending':
      case 'running':
      case 'queued':
        return Icons.schedule_outlined;
      case 'failed':
      case 'rejected':
        return Icons.error_outline;
      default:
        return Icons.notifications_none_outlined;
    }
  }

  Color _colorFor(String status, AppStatusColors colors) {
    switch (status) {
      case 'posted':
      case 'succeeded':
      case 'confirmed':
      case 'created':
        return colors.success;
      case 'pending':
      case 'running':
      case 'queued':
        return colors.warning;
      case 'failed':
      case 'rejected':
        return colors.danger;
      default:
        return colors.info;
    }
  }
}
