import 'package:intl/intl.dart';

import '../../data/models/domain_models.dart';

/// Định dạng số tiền/ngày/nhãn trạng thái dùng chung toàn app.
///
/// Nguyên tắc (docs/07): dữ liệu từ repo là chuỗi (`'750000.00'`), UI phải
/// hiển thị đúng chuẩn vi-VN — phân tách nghìn bằng dấu `.`, thập phân bằng
/// `,`, ngày `dd/MM/yyyy HH:mm`. Không format lại số dư cuối ở client, chỉ
/// render chuỗi server đã tính (xem docs/07 "Client không tự tính balance").
abstract final class AppFormat {
  /// Số nguyên có phân tách nghìn: `'100000'` → `100.000`.
  static String number(String raw, {String? fallback}) {
    final v = _parse(raw);
    if (v == null) return fallback ?? raw;
    final isInt = v == v.roundToDouble();
    return NumberFormat.decimalPattern('vi_VN').format(
      isInt ? v.roundToDouble() : v,
    );
  }

  /// Lấy số làm tròn (không dùng ký tự `int` làm tên hàm, gây xung đột với
  /// `int` kiểu trong Dart).
  static String intFormat(int v) => NumberFormat.decimalPattern('vi_VN').format(v);

  /// Tiền: `'1000000.00', 'VND'` → `1.000.000 VND`.
  static String money(String raw, {String currency = 'VND', String? fallback}) {
    final v = _parse(raw);
    if (v == null) return fallback ?? raw;
    return '${NumberFormat.decimalPattern('vi_VN').format(v)} $currency';
  }

  /// Số lượng tồn: `'100.000'` (thập phân) → `100` — bỏ `.000` thừa.
  static String quantity(String raw, {String? fallback}) {
    final v = _parse(raw);
    if (v == null) return fallback ?? raw;
    if (v == v.roundToDouble()) return number(raw, fallback: fallback);
    return NumberFormat('#,##0.###', 'vi_VN').format(v);
  }

  static double? _parse(String raw) {
    if (raw.trim().isEmpty) return null;
    return double.tryParse(raw.replaceAll(',', '').trim());
  }

  // ---- ngày giờ ----

  /// `05/10/2026 14:30`.
  static String dateTime(DateTime value) =>
      DateFormat('dd/MM/yyyy HH:mm').format(value.toLocal());

  /// `05/10/2026`.
  static String date(DateTime value) =>
      DateFormat('dd/MM/yyyy').format(value.toLocal());

  /// `14:30`.
  static String time(DateTime value) =>
      DateFormat('HH:mm').format(value.toLocal());

  /// Hôm nay → `14:30`, hôm qua → `Hôm qua 14:30`, trong 7 ngày → `2 ngày trước`,
  /// còn lại → `05/10/2026`.
  static String relativeTime(DateTime value, {DateTime? now}) {
    final local = value.toLocal();
    final ref = (now ?? DateTime.now());
    final today = DateTime(ref.year, ref.month, ref.day);
    final day = DateTime(local.year, local.month, local.day);
    final diffDays = today.difference(day).inDays;
    if (diffDays == 0) return 'Hôm nay ${time(local)}';
    if (diffDays == 1) return 'Hôm qua ${time(local)}';
    if (diffDays > 1 && diffDays < 7) return '$diffDays ngày trước';
    return date(local);
  }

  /// Chia `09:00`/`Hôm qua`/`05/10` cho nhóm Activity (docs/07 Today/Earlier).
  static String dayGroup(DateTime value, {DateTime? now}) {
    final local = value.toLocal();
    final ref = (now ?? DateTime.now());
    final today = DateTime(ref.year, ref.month, ref.day);
    final day = DateTime(local.year, local.month, local.day);
    final diff = today.difference(day).inDays;
    if (diff == 0) return 'Hôm nay';
    if (diff == 1) return 'Hôm qua';
    if (diff < 7) return '7 ngày qua';
    return 'Trước đó';
  }
}

/// Nhãn tiếng Việt cho enum trạng thái — luôn kèm [label] thay vì in `.name`.
extension RequestStatusX on RequestStatus {
  String get label => switch (this) {
        RequestStatus.draft => 'Bản nháp',
        RequestStatus.pending => 'Chờ duyệt',
        RequestStatus.approved => 'Đã duyệt',
        RequestStatus.posted => 'Đã ghi sổ',
        RequestStatus.rejected => 'Bị từ chối',
        RequestStatus.cancelled => 'Đã hủy',
      };

  /// Gợi ý tone màu cho chip (không phải tín hiệu duy nhất, luôn kèm icon).
  bool get isFailed => this == RequestStatus.rejected;
  bool get isSuccess => this == RequestStatus.posted || this == RequestStatus.approved;
}

extension RequestKindX on RequestKind {
  String get label => switch (this) {
        RequestKind.receipt => 'Nhập kho',
        RequestKind.payment => 'Xuất kho',
        RequestKind.transfer => 'Chuyển kho',
        RequestKind.adjustment => 'Điều chỉnh',
      };
}

extension AssetStatusX on AssetStatus {
  String get label => switch (this) {
        AssetStatus.available => 'Sẵn có',
        AssetStatus.inUse => 'Đang dùng',
        AssetStatus.maintenance => 'Bảo trì',
        AssetStatus.disposed => 'Đã thanh lý',
      };
}

/// Trạng thái dạng chuỗi từ server (`'connected'`, `'succeeded'`…).
/// Trả `null` nếu không nhận diện được để UI hiển thị chuỗi gốc.
String? rawStatusLabel(String raw) {
  if (raw == 'connected') return 'Đã kết nối';
  if (raw == 'disconnected') return 'Ngắt kết nối';
  if (raw == 'error') return 'Lỗi kết nối';
  if (raw == 'running') return 'Đang chạy';
  if (raw == 'queued') return 'Trong hàng đợi';
  if (raw == 'succeeded' || raw == 'success' || raw == 'completed') {
    return 'Hoàn tất';
  }
  if (raw == 'failed' || raw == 'failure') return 'Thất bại';
  if (raw == 'cancelled') return 'Bị hủy';
  if (raw == 'pending') return 'Chờ xử lý';
  if (raw == 'created') return 'Vừa tạo';
  if (raw == 'processing') return 'Đang xử lý';
  return null;
}

/// Tone màu cho chuỗi trạng thái server, dùng cho chip/icon.
enum RawStatusTone { success, warning, danger, info, neutral }

RawStatusTone rawStatusTone(String raw) {
  if (raw == 'connected' ||
      raw == 'succeeded' ||
      raw == 'success' ||
      raw == 'completed') {
    return RawStatusTone.success;
  }
  if (raw == 'pending' ||
      raw == 'queued' ||
      raw == 'processing' ||
      raw == 'running' ||
      raw == 'created') {
    return RawStatusTone.info;
  }
  if (raw == 'error' || raw == 'failed' || raw == 'failure') {
    return RawStatusTone.danger;
  }
  if (raw == 'cancelled' || raw == 'disconnected') {
    return RawStatusTone.warning;
  }
  return RawStatusTone.neutral;
}
