import 'package:flutter/material.dart';

import '../theme/app_tokens.dart';

/// Avatar người dùng: dấu tâm (initials) + tên gọn. Hiệu ứng hover/focus bằng
/// ink splash; không làm đổi hình nhỏ (vì đó là biểu tượng đại diện).
class UserAvatar extends StatelessWidget {
  const UserAvatar({super.key, this.name, this.size = 36, this.onTap});

  final String? name;
  final double size;
  final VoidCallback? onTap;

  static String initials(String? name) {
    if (name == null || name.trim().isEmpty) return '??';
    final parts = name.trim().split(RegExp(r'\s+'));
    if (parts.length == 1) {
      final only = parts.single;
      return '${only[0].toUpperCase()}${only.length > 1 ? only[1].toUpperCase() : ''}';
    }
    return '${parts[0][0].toUpperCase()}${parts[1][0].toUpperCase()}';
  }

  @override
  Widget build(BuildContext context) {
    final sc = Theme.of(context);
    final bg = _palette(sc);
    final textColor = sc.colorScheme.onSurface;
    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(AppRadius.md),
      child: Container(
        width: size,
        height: size,
        decoration: BoxDecoration(
          color: bg,
          shape: BoxShape.circle,
          border: Border.all(
            color: sc.colorScheme.outline,
            width: 2,
          ),
        ),
        child: Center(
          child: Text(
            name != null ? initials(name) : '??',
            style: TextStyle(color: textColor, fontWeight: FontWeight.w700, fontSize: size * 0.5),
          ),
        ),
      ),
    );
  }

  static Color _palette(ThemeData sc) => switch (sc.brightness) {
        Brightness.light => const Color(0xFF007F86),
        Brightness.dark => const Color(0xFF9DB8E0),
      };
}
