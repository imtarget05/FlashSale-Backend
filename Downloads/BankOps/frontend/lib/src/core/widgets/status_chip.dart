import 'package:flutter/material.dart';

import '../theme/app_theme.dart';
import '../theme/app_tokens.dart';
import '../util/formatting.dart';
import '../../data/models/domain_models.dart';

/// Cường độ của một tone.
enum StatusTone {
  success,
  warning,
  danger,
  info,
  neutral,
}

/// Thanh trạng thái (chip): **luôn kèm icon** (docs/07: "màu không là
/// tín hiệu duy nhất"), thân xám nhạt, text tối đủ tương phản.
///
/// ```dart
/// StatusChip.request(RequestStatus.pending)
/// StatusChip.raw('connected', tone: RawStatusTone.success)
/// ```
class StatusChip extends StatelessWidget {
  const StatusChip._({
    required this.label,
    this.icon,
    this.tone = StatusTone.neutral,
  });

  final String label;
  final IconData? icon;
  final StatusTone tone;

  factory StatusChip.request(RequestStatus status) {
    final tone = status.isSuccess
        ? StatusTone.success
        : (status.isFailed ? StatusTone.danger : StatusTone.info);
    return StatusChip._(
      label: status.label,
      icon: _iconFor(status),
      tone: tone,
    );
  }

  factory StatusChip.raw(
    String value, {
    String? fallback,
    RawStatusTone? tone,
  }) {
    final label = rawStatusLabel(value) ?? fallback ?? value;
    return StatusChip._(
      label: label,
      icon: rawStatusIcon(value),
      tone: tone != null ? _mapRawToStatus(tone) : _mapRawToStatus(rawStatusTone(value)),
    );
  }

  static StatusTone _mapRawToStatus(RawStatusTone raw) => switch (raw) {
        RawStatusTone.success => StatusTone.success,
        RawStatusTone.warning => StatusTone.warning,
        RawStatusTone.danger => StatusTone.danger,
        RawStatusTone.info => StatusTone.info,
        RawStatusTone.neutral => StatusTone.neutral,
      };

  @override
  Widget build(BuildContext context) {
    final sc = Theme.of(context).extension<AppStatusColors>()!;
    final toneColor = switch (tone) {
      StatusTone.success => sc.success,
      StatusTone.warning => sc.warning,
      StatusTone.danger => sc.danger,
      StatusTone.info => sc.info,
      StatusTone.neutral => sc.info,
    };

    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: AppSpacing.sm,
        vertical: AppSpacing.xs,
      ),
      decoration: BoxDecoration(
        color: toneColor.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(AppRadius.sm),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icon != null) ...[
            Icon(icon, size: 13, color: toneColor),
            const SizedBox(width: AppSpacing.xs),
          ],
          Text(
            label,
            style: TextStyle(color: toneColor),
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
          ),
        ],
      ),
    );
  }

  static IconData _iconFor(RequestStatus status) {
    switch (status) {
      case RequestStatus.draft:
        return Icons.edit_note_outlined;
      case RequestStatus.pending:
        return Icons.schedule_outlined;
      case RequestStatus.approved:
        return Icons.fact_check_outlined;
      case RequestStatus.posted:
        return Icons.check_circle_outlined;
      case RequestStatus.rejected:
        return Icons.cancel_outlined;
      case RequestStatus.cancelled:
        return Icons.cancel_outlined;
    }
  }

  static IconData rawStatusIcon(String raw) {
    if (raw == 'connected') return Icons.check_circle_outline;
    if (raw == 'disconnected') return Icons.offline_bolt_outlined;
    if (raw == 'error') return Icons.error_outline;
    if (raw == 'running') return Icons.play_arrow_outlined;
    if (raw == 'queued') return Icons.queue_outlined;
    if (raw == 'succeeded' || raw == 'success' || raw == 'completed') {
      return Icons.check_circle_outline;
    }
    if (raw == 'failed' || raw == 'failure') return Icons.error_outline;
    if (raw == 'cancelled') return Icons.cancel_outlined;
    if (raw == 'pending') return Icons.schedule_outlined;
    if (raw == 'created') return Icons.add_circle_outline;
    if (raw == 'processing') return Icons.autorenew_outlined;
    return Icons.info_outline;
  }
}
