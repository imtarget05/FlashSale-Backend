import 'package:flutter/widgets.dart';

/// Design tokens theo docs/07-flutter.md.
/// Màu nằm trong [AppStatusColors] (ThemeExtension); khoảng cách/bán kính/
/// thời lượng là hằng số dùng chung — widget không hardcode giá trị.
abstract final class AppSpacing {
  static const double xs = 4;
  static const double sm = 8;
  static const double md = 12;
  static const double lg = 16;
  static const double xl = 24;
  static const double xxl = 32;
}

abstract final class AppRadius {
  static const double sm = 8;
  static const double md = 12;
  static const double lg = 16;
}

/// Transition 150–250 ms theo đặc tả; trả về Duration(0) khi người dùng
/// bật "giảm chuyển động" trên hệ điều hành.
abstract final class AppDuration {
  static const Duration fast = Duration(milliseconds: 150);
  static const Duration normal = Duration(milliseconds: 200);
  static const Duration slow = Duration(milliseconds: 250);
}

/// Truy cập motion token có tính đến cài đặt giảm chuyển động của thiết bị.
abstract final class Motion {
  static bool reduced(BuildContext context) =>
      MediaQuery.disableAnimationsOf(context);

  static Duration fast(BuildContext context) =>
      reduced(context) ? Duration.zero : AppDuration.fast;

  static Duration normal(BuildContext context) =>
      reduced(context) ? Duration.zero : AppDuration.normal;

  static Duration slow(BuildContext context) =>
      reduced(context) ? Duration.zero : AppDuration.slow;
}

/// Breakpoints theo docs/07-flutter.md.
enum DeviceLayout { compact, medium, wide }

abstract final class Breakpoints {
  static const double medium = 600;
  static const double wide = 1200;

  static DeviceLayout of(BuildContext context) =>
      fromWidth(MediaQuery.sizeOf(context).width);

  static DeviceLayout fromWidth(double width) {
    if (width >= wide) return DeviceLayout.wide;
    if (width >= medium) return DeviceLayout.medium;
    return DeviceLayout.compact;
  }
}

extension DeviceLayoutX on DeviceLayout {
  bool get isCompact => this == DeviceLayout.compact;
  bool get isMedium => this == DeviceLayout.medium;
  bool get isWide => this == DeviceLayout.wide;
}
