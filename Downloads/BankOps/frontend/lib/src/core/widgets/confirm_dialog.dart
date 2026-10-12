import 'package:flutter/material.dart';

import '../theme/app_tokens.dart';

/// Dialog xác nhận hành động (xóa, từ chối, chốt phiếu).
/// Trả `true` nếu người dùng bấm nút xác nhận.
Future<bool> showConfirmDialog(
  BuildContext context, {
  required String title,
  required String message,
  String confirmLabel = 'Xác nhận',
  String cancelLabel = 'Hủy',
  bool danger = false,
}) {
  final sc = Theme.of(context);
  return showDialog<bool>(
    context: context,
    builder: (dialogContext) => AlertDialog(
      shape:
          RoundedRectangleBorder(borderRadius: BorderRadius.circular(AppRadius.lg)),
      title: Text(title, style: sc.textTheme.titleMedium),
      content: Text(message, style: sc.textTheme.bodyMedium),
      actions: [
        TextButton(
          key: const Key('confirm_dialog_cancel'),
          onPressed: () => Navigator.of(dialogContext).pop(false),
          child: Text(cancelLabel),
        ),
        FilledButton(
          key: const Key('confirm_dialog_confirm'),
          onPressed: () => Navigator.of(dialogContext).pop(true),
          style: FilledButton.styleFrom(
            backgroundColor:
                danger ? sc.colorScheme.error : null,
            foregroundColor:
                danger ? null : sc.colorScheme.onPrimaryContainer,
          ),
          child: Text(confirmLabel),
        ),
      ],
    ),
  ).then((confirmed) => confirmed ?? false);
}

/// Dialog nhập lý do từ chối (cho phiếu tài liệu).
Future<String?> showRejectDialog(
  BuildContext context, {
  required String title,
  String? initial,
}) {
  final controller = TextEditingController(text: initial);
  try {
    return showDialog<String>(
      context: context,
      builder: (context) => AlertDialog(
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(AppRadius.lg)),
        title: Text(title, style: Theme.of(context).textTheme.titleMedium),
        content: TextField(
          controller: controller,
          maxLines: 3,
          decoration: const InputDecoration(labelText: 'Lý do từ chối', hintText: 'Nêu rõ lý do…'),
        ),
        actions: [
          TextButton(
            key: const Key('reject_dialog_cancel'),
            onPressed: () => Navigator.of(context).pop(null),
            child: const Text('Hủy'),
          ),
          FilledButton(
            key: const Key('reject_dialog_confirm'),
            onPressed: () => Navigator.of(context).pop(controller.text.trim()),
            child: const Text('Đồng ý'),
          ),
        ],
      ),
    );
  } finally {
    controller.dispose();
  }
}
