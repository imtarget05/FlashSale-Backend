import 'package:flutter/material.dart';

import '../theme/app_tokens.dart';

/// Chi tiết khoa học (vật tư, phiếu, tài sản) — bottom sheet kéo kiểu
/// `DraggableScrollableSheet`, có thanh tiêu đề + nút đóng.
class DetailSheet {
  static Future<void> show(BuildContext context,
      {required String title,
      required Widget child,
      double minExtent = 0.45,
      double maxExtent = 0.92}) {
    return showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (context) => GestureDetector(
        onTap: () => Navigator.of(context).pop(),
        child: DraggableScrollableSheet(
          initialChildSize: 0.82,
          minChildSize: minExtent,
          maxChildSize: maxExtent,
          expand: false,
          builder: (context, scrollController) => Scrollbar(
            thumbVisibility: true,
            child: SingleChildScrollView(
              controller: scrollController,
              padding: const EdgeInsets.only(
                top: AppSpacing.xxl,
                bottom: AppSpacing.xxl,
                left: AppSpacing.lg,
                right: AppSpacing.lg,
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  _SheetHandle(title),
                  const SizedBox(height: AppSpacing.lg),
                  child,
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _SheetHandle extends StatelessWidget {
  const _SheetHandle(this.title);

  final String title;

  @override
  Widget build(BuildContext context) {
    final sc = Theme.of(context);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: AppSpacing.md),
      decoration: BoxDecoration(
        color: sc.colorScheme.surfaceContainerLow,
        borderRadius: BorderRadius.circular(AppRadius.md),
      ),
      child: Row(
        children: [
          Text(
            title,
            style: sc.textTheme.titleMedium?.copyWith(
              fontWeight: FontWeight.w700,
              color: sc.colorScheme.onSurface,
            ),
          ),
          const Spacer(),
          InkWell(
            onTap: () => Navigator.of(context).pop(),
            borderRadius: BorderRadius.circular(AppRadius.md),
            child: Container(
              padding: const EdgeInsets.all(AppSpacing.sm),
              child: const Icon(Icons.close, size: 18),
            ),
          ),
        ],
      ),
    );
  }
}
