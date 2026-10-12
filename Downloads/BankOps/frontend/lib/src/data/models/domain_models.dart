/// Domain enums dùng chung cho repository và UI.
library;

/// Trạng thái phiếu — dùng cho cả stock và cash request.
enum RequestStatus {
  draft,
  pending,
  approved,
  posted,
  rejected,
  cancelled;

  /// Phiếu đã chốt (không transition được nữa).
  bool get isFinal =>
      this == posted || this == rejected || this == cancelled;
}

/// Loại phiếu — dùng cho cả stock và cash request.
enum RequestKind {
  receipt,
  payment,
  transfer,
  adjustment;
}

/// Trạng thái tài sản.
enum AssetStatus {
  available,
  inUse,
  maintenance,
  disposed;
}
