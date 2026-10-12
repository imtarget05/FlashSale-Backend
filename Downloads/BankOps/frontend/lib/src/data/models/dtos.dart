export 'domain_models.dart';
export 'report_dtos.dart';

import 'domain_models.dart';

/// User model (demo).
class AppUser {
  const AppUser({
    required this.id,
    required this.username,
    required this.displayName,
    required this.roles,
  });

  final String id;
  final String username;
  final String displayName;
  final List<String> roles;
}

/// Kho/địa điểm.
class InventoryLocation {
  const InventoryLocation({
    required this.id,
    required this.code,
    required this.name,
  });

  final String id;
  final String code;
  final String name;
}

/// Vật tư/mặt hàng.
class InventoryItem {
  const InventoryItem({
    required this.id,
    required this.sku,
    required this.name,
    required this.unit,
    required this.trackedSerial,
  });

  final String id;
  final String sku;
  final String name;
  final String unit;
  final bool trackedSerial;
}

/// Số tồn kho theo location + item.
class StockBalance {
  const StockBalance({
    required this.locationId,
    required this.itemId,
    required this.quantity,
  });

  final String locationId;
  final String itemId;
  final String quantity;
}

/// Tài sản theo serial.
class AssetRecord {
  const AssetRecord({
    required this.serial,
    required this.itemId,
    required this.locationId,
    required this.status,
  });

  final String serial;
  final String itemId;
  final String locationId;
  final AssetStatus status;
}

/// Movement ledger.
class StockMovement {
  const StockMovement({
    required this.id,
    required this.itemId,
    required this.locationId,
    required this.quantity,
    required this.kind,
    required this.createdAt,
  });

  final String id;
  final String itemId;
  final String locationId;
  final String quantity;
  final String kind;
  final DateTime createdAt;
}

/// Line item trong stock request.
class StockRequestLine {
  const StockRequestLine({required this.itemId, required this.quantity});

  final String itemId;
  final String quantity;
}

/// Stock request.
class StockRequest {
  const StockRequest({
    required this.id,
    required this.kind,
    required this.status,
    required this.version,
    required this.createdBy,
    required this.createdByName,
    required this.locationId,
    required this.items,
    required this.reason,
    required this.createdAt,
    this.approvedBy,
    this.approvedByName,
    this.rejectReason,
    this.destinationLocationId,
  });

  final String id;
  final RequestKind kind;
  final RequestStatus status;
  final int version;
  final String createdBy;
  final String createdByName;
  final String locationId;
  final List<StockRequestLine> items;
  final String reason;
  final DateTime createdAt;
  final String? approvedBy;
  final String? approvedByName;
  final String? rejectReason;
  final String? destinationLocationId;
}

/// Cash box.
class Cashbox {
  const Cashbox({
    required this.id,
    required this.code,
    required this.name,
    required this.openingBalance,
  });

  final String id;
  final String code;
  final String name;
  final String openingBalance;
}

/// Cash balance.
class CashBalance {
  const CashBalance({
    required this.cashboxId,
    required this.amount,
    required this.currency,
  });

  final String cashboxId;
  final String amount;
  final String currency;
}

/// Journal entry.
class JournalEntry {
  const JournalEntry({
    required this.id,
    required this.cashboxId,
    required this.kind,
    required this.amount,
    required this.currency,
    required this.reason,
    required this.createdAt,
  });

  final String id;
  final String cashboxId;
  final String kind;
  final String amount;
  final String currency;
  final String reason;
  final DateTime createdAt;
}

/// Cash request.
class CashRequest {
  const CashRequest({
    required this.id,
    required this.kind,
    required this.status,
    required this.version,
    required this.cashboxId,
    required this.amount,
    required this.currency,
    required this.reason,
    required this.createdBy,
    required this.createdByName,
    required this.createdAt,
    this.approvedBy,
    this.approvedByName,
    this.rejectReason,
  });

  final String id;
  final RequestKind kind;
  final RequestStatus status;
  final int version;
  final String cashboxId;
  final String amount;
  final String currency;
  final String reason;
  final String createdBy;
  final String createdByName;
  final DateTime createdAt;
  final String? approvedBy;
  final String? approvedByName;
  final String? rejectReason;
}

/// Approval inbox item.
class ApprovalItem {
  const ApprovalItem({
    required this.id,
    required this.kind,
    required this.maker,
    required this.impact,
    required this.detail,
  });

  final String id;
  final String kind;
  final String maker;
  final String impact;
  final String detail;
}

/// Activity event.
class ActivityEvent {
  const ActivityEvent({
    required this.id,
    required this.occurredAt,
    required this.subjectId,
    required this.status,
    required this.messageKey,
    this.processed,
    this.total,
  });

  final String id;
  final DateTime occurredAt;
  final String subjectId;
  final String status;
  final String messageKey;
  final int? processed;
  final int? total;
}

/// Integration connection.
class IntegrationConnection {
  const IntegrationConnection({
    required this.id,
    required this.name,
    required this.endpoint,
    required this.status,
  });

  final String id;
  final String name;
  final String endpoint;
  final String status;
}

/// Integration run.
class IntegrationRun {
  const IntegrationRun({
    required this.id,
    required this.connectionId,
    required this.status,
    required this.startedAt,
    this.processed,
    this.total,
    this.error,
  });

  final String id;
  final String connectionId;
  final String status;
  final DateTime startedAt;
  final int? processed;
  final int? total;
  final String? error;
}

/// Reconciliation run.
class ReconciliationRun {
  const ReconciliationRun({
    required this.id,
    required this.status,
    required this.startedAt,
    this.exceptions = const [],
  });

  final String id;
  final String status;
  final DateTime startedAt;
  final List<String> exceptions;
}
