import 'package:flutter/material.dart';

/// Chế độ giao diện toàn app: hệ thống / sáng / tối.
///
/// Theo Phase 3 plan: lưu trong bộ nhớ phiên (không persist), để bản đồ
/// sau này thêm `shared_preferences` nếu cần.
class ThemeModeController extends ChangeNotifier {
  ThemeMode _mode = ThemeMode.system;

  ThemeMode get mode => _mode;

  /// Đặt chế độ khi khác hiện tại; không làm gì khi trùng (tránh rebuild thừa).
  void setMode(ThemeMode mode) {
    if (mode == _mode) return;
    _mode = mode;
    notifyListeners();
  }
}
