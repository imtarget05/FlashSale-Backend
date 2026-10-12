// DTOs cho báo cáo biểu đồ (Track A) và import staging preview (Track B).

class InventoryBucket {
  const InventoryBucket({
    required this.bucket,
    required this.inboundQty,
    required this.outboundQty,
    required this.netQty,
    required this.receiptCount,
    required this.issueCount,
  });

  final String bucket;
  final String inboundQty;
  final String outboundQty;
  final String netQty;
  final int receiptCount;
  final int issueCount;

  double get inboundValue => double.tryParse(inboundQty) ?? 0;
  double get outboundValue => double.tryParse(outboundQty) ?? 0;
  double get netValue => double.tryParse(netQty) ?? 0;
}

class CashflowBucket {
  const CashflowBucket({
    required this.bucket,
    required this.totalIn,
    required this.totalOut,
    required this.net,
    required this.journalCount,
  });

  final String bucket;
  final String totalIn;
  final String totalOut;
  final String net;
  final int journalCount;

  double get inValue => double.tryParse(totalIn) ?? 0;
  double get outValue => double.tryParse(totalOut) ?? 0;
  double get netValue => double.tryParse(net) ?? 0;
}

class StagnationItem {
  const StagnationItem({
    required this.itemId,
    required this.sku,
    required this.name,
    required this.quantity,
    required this.daysSinceLastIssue,
    required this.severity,
  });

  final String itemId;
  final String sku;
  final String name;
  final String quantity;
  final int? daysSinceLastIssue;
  final String severity;
}

class StagingRow {
  const StagingRow({
    required this.rowIndex,
    required this.sourceRowKey,
    required this.data,
    required this.valid,
    required this.errors,
  });

  final int rowIndex;
  final String sourceRowKey;
  final Map<String, dynamic> data;
  final bool valid;
  final List<String> errors;
}

class StagingPreview {
  const StagingPreview({
    required this.runId,
    required this.validCount,
    required this.invalidCount,
    required this.rows,
    this.deduplicated = false,
    this.mappingVersion = 1,
  });

  final String runId;
  final int validCount;
  final int invalidCount;
  final List<StagingRow> rows;
  final bool deduplicated;
  final int mappingVersion;

  bool get canConfirm => validCount > 0 && invalidCount == 0;
}

class ConfirmRunResult {
  const ConfirmRunResult({
    required this.runId,
    required this.status,
    required this.draftRequests,
  });

  final String runId;
  final String status;
  final List<String> draftRequests;
}
