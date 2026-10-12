import 'package:flutter/material.dart';

import '../theme/app_tokens.dart';

/// Màu trạng thái + nhãn DEMO, expose qua ThemeExtension để không hardcode
/// trong widget (docs/07-flutter.md).
@immutable
class AppStatusColors extends ThemeExtension<AppStatusColors> {
  const AppStatusColors({
    required this.success,
    required this.warning,
    required this.danger,
    required this.info,
    required this.demoBadgeBg,
    required this.demoBadgeFg,
  });

  final Color success; // #207A50
  final Color warning; // #9A6200
  final Color danger; // #B42318
  final Color info;
  final Color demoBadgeBg;
  final Color demoBadgeFg;

  static const AppStatusColors light = AppStatusColors(
    success: Color(0xFF207A50),
    warning: Color(0xFF9A6200),
    danger: Color(0xFFB42318),
    info: Color(0xFF17365D),
    demoBadgeBg: Color(0xFFE6F4F5),
    demoBadgeFg: Color(0xFF00595F),
  );

  static const AppStatusColors dark = AppStatusColors(
    success: Color(0xFF6CC98F),
    warning: Color(0xFFF0B44A),
    danger: Color(0xFFF18B82),
    info: Color(0xFF9DB8E0),
    demoBadgeBg: Color(0xFF10343A),
    demoBadgeFg: Color(0xFF7FD8DE),
  );

  @override
  AppStatusColors copyWith({
    Color? success,
    Color? warning,
    Color? danger,
    Color? info,
    Color? demoBadgeBg,
    Color? demoBadgeFg,
  }) {
    return AppStatusColors(
      success: success ?? this.success,
      warning: warning ?? this.warning,
      danger: danger ?? this.danger,
      info: info ?? this.info,
      demoBadgeBg: demoBadgeBg ?? this.demoBadgeBg,
      demoBadgeFg: demoBadgeFg ?? this.demoBadgeFg,
    );
  }

  @override
  AppStatusColors lerp(ThemeExtension<AppStatusColors>? other, double t) {
    if (other is! AppStatusColors) return this;
    return AppStatusColors(
      success: Color.lerp(success, other.success, t)!,
      warning: Color.lerp(warning, other.warning, t)!,
      danger: Color.lerp(danger, other.danger, t)!,
      info: Color.lerp(info, other.info, t)!,
      demoBadgeBg: Color.lerp(demoBadgeBg, other.demoBadgeBg, t)!,
      demoBadgeFg: Color.lerp(demoBadgeFg, other.demoBadgeFg, t)!,
    );
  }
}

abstract final class AppTheme {
  static const Color navy = Color(0xFF17365D);
  static const Color teal = Color(0xFF007F86);
  static const Color surface = Color(0xFFF6F8FA);

  static const _seed = navy;

  static ThemeData light() => _build(Brightness.light);
  static ThemeData dark() => _build(Brightness.dark);

  static ThemeData _build(Brightness brightness) {
    final scheme = ColorScheme.fromSeed(
      seedColor: _seed,
      brightness: brightness,
    ).copyWith(
      primary: brightness == Brightness.light ? navy : const Color(0xFF9DB8E0),
      secondary: teal,
      surface: brightness == Brightness.light
          ? surface
          : const Color(0xFF101418),
    );
    final base = ThemeData(
      useMaterial3: true,
      colorScheme: scheme,
      visualDensity: VisualDensity.standard,
    );
    final status =
        brightness == Brightness.light ? AppStatusColors.light : AppStatusColors.dark;

    return base.copyWith(
      extensions: [status],
      // Typography scale 12/14/16/20/20/28 theo docs/07:9.
      textTheme: textTheme(brightness),
      appBarTheme: AppBarThemeData(
        backgroundColor: scheme.surface,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        scrolledUnderElevation: 1,
        centerTitle: false,
        titleTextStyle: textTheme(brightness).titleLarge?.copyWith(
              color: scheme.onSurface,
              fontWeight: FontWeight.w700,
            ),
        iconTheme: IconThemeData(color: scheme.onSurfaceVariant),
      ),
      inputDecorationTheme: InputDecorationTheme(
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(AppRadius.md),
          borderSide: BorderSide(color: scheme.outlineVariant),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(AppRadius.md),
          borderSide: BorderSide(color: scheme.outlineVariant),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(AppRadius.md),
          borderSide: BorderSide(color: scheme.primary, width: 1.5),
        ),
        filled: true,
        fillColor: scheme.surfaceContainerLow,
        isDense: true,
        hintStyle: TextStyle(color: scheme.onSurfaceVariant),
        contentPadding: const EdgeInsets.symmetric(
          horizontal: AppSpacing.lg,
          vertical: AppSpacing.md,
        ),
      ),
      cardTheme: CardThemeData(
        elevation: 0,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(AppRadius.lg),
          side: BorderSide(color: scheme.outlineVariant),
        ),
        color: scheme.surfaceContainerLow,
        surfaceTintColor: Colors.transparent,
        margin: EdgeInsets.zero,
        clipBehavior: Clip.antiAlias,
      ),
      dividerTheme: DividerThemeData(
        color: scheme.outlineVariant,
        thickness: 1,
        space: 1,
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          minimumSize: const Size(44, 44),
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg),
          textStyle: const TextStyle(fontWeight: FontWeight.w600),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(AppRadius.md),
          ),
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          minimumSize: const Size(44, 44),
          padding: const EdgeInsets.symmetric(horizontal: AppSpacing.lg),
          textStyle: const TextStyle(fontWeight: FontWeight.w600),
          side: BorderSide(color: scheme.outlineVariant),
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(AppRadius.md),
          ),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(
          minimumSize: const Size(44, 40),
          textStyle: const TextStyle(fontWeight: FontWeight.w600),
        ),
      ),
      listTileTheme: ListTileThemeData(
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(AppRadius.md),
        ),
        contentPadding: const EdgeInsets.symmetric(
          horizontal: AppSpacing.lg,
          vertical: AppSpacing.xs,
        ),
        titleTextStyle: textTheme(brightness).bodyLarge?.copyWith(
              color: scheme.onSurface,
              fontWeight: FontWeight.w600,
            ),
        subtitleTextStyle: textTheme(brightness).bodySmall?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
        iconColor: scheme.onSurfaceVariant,
      ),
      dialogTheme: DialogThemeData(
        backgroundColor: scheme.surfaceContainerHigh,
        surfaceTintColor: Colors.transparent,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(AppRadius.lg + 4),
        ),
        titleTextStyle: textTheme(brightness).titleLarge?.copyWith(
              color: scheme.onSurface,
              fontWeight: FontWeight.w700,
            ),
        contentTextStyle: textTheme(brightness).bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
      ),
      bottomSheetTheme: BottomSheetThemeData(
        backgroundColor: scheme.surfaceContainerLow,
        surfaceTintColor: Colors.transparent,
        showDragHandle: true,
        shape: const RoundedRectangleBorder(
          borderRadius: BorderRadius.vertical(
            top: Radius.circular(AppRadius.lg + 4),
          ),
        ),
      ),
      progressIndicatorTheme: ProgressIndicatorThemeData(
        color: scheme.primary,
        linearMinHeight: 4,
      ),
    );
  }

  /// Typography 12/14/16/20/28 — docs/07:9, weight phân cấp rõ ràng.
  static TextTheme textTheme(Brightness brightness) {
    final onSurface = brightness == Brightness.light
        ? const Color(0xFF101828)
        : const Color(0xFFF2F4F7);
    final onVariant = brightness == Brightness.light
        ? const Color(0xFF475467)
        : const Color(0xFF98A2B3);
    return TextTheme(
      headlineMedium: TextStyle(
        fontSize: 28,
        height: 1.25,
        fontWeight: FontWeight.w800,
        letterSpacing: -0.4,
        color: onSurface,
      ),
      headlineSmall: TextStyle(
        fontSize: 20,
        height: 1.3,
        fontWeight: FontWeight.w700,
        letterSpacing: -0.2,
        color: onSurface,
      ),
      titleLarge: TextStyle(
        fontSize: 20,
        height: 1.3,
        fontWeight: FontWeight.w700,
        color: onSurface,
      ),
      titleMedium: TextStyle(
        fontSize: 16,
        height: 1.4,
        fontWeight: FontWeight.w600,
        color: onSurface,
      ),
      titleSmall: TextStyle(
        fontSize: 14,
        height: 1.4,
        fontWeight: FontWeight.w600,
        color: onSurface,
      ),
      bodyLarge: TextStyle(
        fontSize: 16,
        height: 1.5,
        fontWeight: FontWeight.w400,
        color: onSurface,
      ),
      bodyMedium: TextStyle(
        fontSize: 14,
        height: 1.5,
        fontWeight: FontWeight.w400,
        color: onSurface,
      ),
      bodySmall: TextStyle(
        fontSize: 12,
        height: 1.4,
        fontWeight: FontWeight.w400,
        color: onVariant,
      ),
      labelLarge: TextStyle(
        fontSize: 14,
        height: 1.3,
        fontWeight: FontWeight.w600,
        color: onSurface,
      ),
      labelSmall: TextStyle(
        fontSize: 12,
        height: 1.3,
        fontWeight: FontWeight.w600,
        letterSpacing: 0.2,
        color: onVariant,
      ),
    );
  }
}

extension AppStatusColorsX on BuildContext {
  AppStatusColors get statusColors =>
      Theme.of(this).extension<AppStatusColors>()!;
}
