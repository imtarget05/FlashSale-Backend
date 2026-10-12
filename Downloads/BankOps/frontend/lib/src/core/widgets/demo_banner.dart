import 'package:flutter/material.dart';

import '../theme/app_theme.dart';

/// Nhãn DEMO — QUỸ MÔ PHỎNG cho mọi màn tiền.
class DemoBanner extends StatelessWidget {
  const DemoBanner({super.key, this.text = 'DEMO — QUỸ MÔ PHỎNG'});

  final String text;

  @override
  Widget build(BuildContext context) {
    final status = context.statusColors;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
      decoration: BoxDecoration(
        color: status.demoBadgeBg,
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: status.demoBadgeFg.withValues(alpha: 0.35)),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.savings_outlined, size: 18, color: status.demoBadgeFg),
          const SizedBox(width: 8),
          Flexible(
            child: Text(
              text,
              style: TextStyle(
                color: status.demoBadgeFg,
                fontWeight: FontWeight.w700,
                fontSize: 12,
                letterSpacing: 0.4,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
