import 'dart:convert';

import 'package:http/http.dart' as http;

import '../../features/auth/auth_service.dart';
import '../models/dtos.dart';
import 'bankops_repository.dart';

/// Repository HTTP thật, gọi backend FastAPI theo docs/06-api.md.
///
/// - `baseUrl` lấy từ `--dart-define=API_BASE_URL=...`, mặc định
///   `http://localhost:8000/api/v1` (chuẩn hoá qua [AuthService.normalizeApiBaseUrl]).
/// - Token Bearer lấy qua [tokenProvider] (do UI/AuthService cấp).
/// - Lỗi backend dạng envelope `{error:{code,message,request_id}}` được
///   chuyển thành [ApiException] giữ nguyên `requestId`.
class HttpBankOpsRepository implements BankOpsRepository {
  HttpBankOpsRepository({
    String? baseUrl,
    http.Client? client,
    Future<String?> Function()? tokenProvider,
  })  : _baseUrl = AuthService.normalizeApiBaseUrl(
          baseUrl ??
              const String.fromEnvironment('API_BASE_URL',
                  defaultValue: 'http://localhost:8000/api/v1'),
        ),
        _client = client ?? http.Client(),
        _tokenProvider = tokenProvider;

  final String _baseUrl;
  final http.Client _client;
  final Future<String?> Function()? _tokenProvider;

  Uri _uri(String path, [Map<String, String>? query]) => Uri.parse('$_baseUrl$path')
      .replace(queryParameters: query == null || query.isEmpty ? null : query);

  Future<Map<String, String>> _headers() async {
    final headers = <String, String>{'Content-Type': 'application/json'};
    final token = await _tokenProvider?.call();
    if (token != null && token.isNotEmpty) {
      headers['Authorization'] = 'Bearer $token';
    }
    return headers;
  }

  Never _throwEnvelope(http.Response resp) {
    String code = 'HTTP_${resp.statusCode}';
    String message = 'Máy chủ trả lỗi ${resp.statusCode}.';
    String? requestId = resp.headers['x-request-id'];
    try {
      final decoded = json.decode(resp.body);
      if (decoded is Map) {
        final err = decoded['error'];
        if (err is Map) {
          if (err['code'] is String) code = err['code'] as String;
          if (err['message'] is String) message = err['message'] as String;
          if (err['request_id'] is String) {
            requestId = err['request_id'] as String;
          }
        } else if (decoded['detail'] is String) {
          message = decoded['detail'] as String;
        }
      }
    } catch (_) {}
    throw ApiException(code, message, requestId: requestId);
  }

  Future<dynamic> _get(String path, [Map<String, String>? query]) async {
    final resp =
        await _client.get(_uri(path, query), headers: await _headers());
    if (resp.statusCode < 200 || resp.statusCode >= 300) {
      _throwEnvelope(resp);
    }
    return json.decode(resp.body);
  }

  Future<Map<String, dynamic>> _post(
      String path, Map<String, dynamic> body,
      {Map<String, String>? extraHeaders}) async {
    final headers = await _headers();
    if (extraHeaders != null) headers.addAll(extraHeaders);
    final resp =
        await _client.post(_uri(path), headers: headers, body: json.encode(body));
    if (resp.statusCode < 200 || resp.statusCode >= 300) {
      _throwEnvelope(resp);
    }
    final decoded = json.decode(resp.body);
    if (decoded is Map<String, dynamic>) return decoded;
    return {'data': decoded};
  }

  static List _asList(dynamic body, String key) {
    if (body is Map && body[key] is List) return body[key] as List;
    if (body is List) return body;
    return const [];
  }

  static String _str(Map m, String key, [String fallback = '']) {
    final v = m[key];
    if (v == null) return fallback;
    return v.toString();
  }

  static DateTime _date(Map m, String key) {
    final v = m[key];
    if (v is String) return DateTime.tryParse(v) ?? DateTime.now().toUtc();
    return DateTime.now().toUtc();
  }

  static RequestStatus _status(String raw) {
    if (raw == 'submitted') return RequestStatus.pending;
    for (final s in RequestStatus.values) {
      if (s.name == raw) return s;
    }
    return RequestStatus.draft;
  }

  /// Server kho dùng 'issue' cho xuất kho; quỹ dùng 'payment'.
  static RequestKind _kind(String raw) {
    if (raw == 'issue') return RequestKind.payment;
    for (final k in RequestKind.values) {
      if (k.name == raw) return k;
    }
    return RequestKind.receipt;
  }

  static String _serverStockKind(RequestKind kind) {
    if (kind == RequestKind.payment) return 'issue';
    return kind.name;
  }

  static AssetStatus _assetStatus(String raw) {
    for (final s in AssetStatus.values) {
      if (s.name == raw) return s;
    }
    return AssetStatus.available;
  }

  // ---- auth/users ----

  @override
  Future<List<AppUser>> loadUsers() async {
    try {
      final me = await _get('/me');
      if (me is Map) {
        return [
          AppUser(
            id: _str(me, 'id'),
            username: _str(me, 'username'),
            displayName: _str(me, 'display_name', _str(me, 'username')),
            roles: (me['roles'] is List ? me['roles'] as List : const [])
                .map((r) => r.toString())
                .toList(),
          ),
        ];
      }
    } catch (_) {}
    return const [
      AppUser(
        id: 'u_maker',
        username: 'demo_maker',
        displayName: 'Nguyễn Văn A (Tạo phiếu)',
        roles: ['maker', 'inventory_custodian', 'cash_simulated_custodian'],
      ),
      AppUser(
        id: 'u_checker',
        username: 'demo_checker',
        displayName: 'Trần Thị B (Duyệt phiếu)',
        roles: ['checker', 'inventory_custodian', 'cash_simulated_custodian'],
      ),
      AppUser(
        id: 'u_auditor',
        username: 'demo_auditor',
        displayName: 'Lê Văn C (Kiểm toán)',
        roles: ['auditor'],
      ),
    ];
  }

  @override
  Future<AppUser> login(String username) async {
    final data = await _post('/auth/login', {
      'username': username,
      'password': 'DemoPass123!',
    });
    final u = (data['user'] is Map) ? data['user'] as Map : data;
    return AppUser(
      id: _str(u, 'id'),
      username: _str(u, 'username', username),
      displayName: _str(u, 'display_name', username),
      roles: (u['roles'] is List ? u['roles'] as List : const [])
          .map((r) => r.toString())
          .toList(),
    );
  }

  // ---- inventory ----

  @override
  Future<List<InventoryLocation>> getLocations() async {
    final body = await _get('/inventory/locations');
    return _asList(body, 'locations')
        .whereType<Map>()
        .map((m) => InventoryLocation(
              id: _str(m, 'id'),
              code: _str(m, 'code'),
              name: _str(m, 'name'),
            ))
        .toList();
  }

  @override
  Future<List<InventoryItem>> getItems() async {
    final body = await _get('/inventory/items');
    return _asList(body, 'items')
        .whereType<Map>()
        .map((m) => InventoryItem(
              id: _str(m, 'id'),
              sku: _str(m, 'sku'),
              name: _str(m, 'name'),
              unit: _str(m, 'unit'),
              trackedSerial: m['tracked_serial'] == true,
            ))
        .toList();
  }

  @override
  Future<List<StockBalance>> getBalances({String? locationId}) async {
    final body = await _get('/inventory/balances',
        {if (locationId != null) 'location_id': locationId});
    return _asList(body, 'balances')
        .whereType<Map>()
        .map((m) => StockBalance(
              locationId: _str(m, 'location_id'),
              itemId: _str(m, 'item_id'),
              quantity: _str(m, 'quantity', '0'),
            ))
        .toList();
  }

  @override
  Future<List<AssetRecord>> getAssets({String? locationId}) async {
    final body = await _get('/inventory/assets');
    final all = _asList(body, 'assets').whereType<Map>().map((m) => AssetRecord(
          serial: _str(m, 'serial'),
          itemId: _str(m, 'item_id'),
          locationId: _str(m, 'location_id'),
          status: _assetStatus(_str(m, 'status')),
        ));
    if (locationId == null) return all.toList();
    return all.where((a) => a.locationId == locationId).toList();
  }

  @override
  Future<List<StockMovement>> getMovements({String? itemId}) async {
    final body = await _get('/inventory/movements');
    final all = _asList(body, 'movements')
        .whereType<Map>()
        .map((m) => StockMovement(
              id: _str(m, 'id'),
              itemId: _str(m, 'item_id'),
              locationId: _str(m, 'location_id'),
              quantity: _str(m, 'quantity_signed', '0'),
              kind: _str(m, 'kind', 'receipt'),
              createdAt: _date(m, 'created_at'),
            ));
    if (itemId == null) return all.toList();
    return all.where((x) => x.itemId == itemId).toList();
  }

  @override
  Future<List<StockRequest>> getStockRequests() async {
    final body = await _get('/inventory/requests');
    return _asList(body, 'requests')
        .whereType<Map>()
        .map(_parseStockRequest)
        .toList();
  }

  @override
  Future<StockRequest> createStockRequest({
    required RequestKind kind,
    String? sourceLocationId,
    String? destLocationId,
    required List<StockRequestLine> lines,
    String? reason,
  }) async {
    final serverKind = _serverStockKind(kind);
    final payload = <String, dynamic>{
      'kind': serverKind,
      'reason': reason ?? '',
      'lines': [
        for (final line in lines)
          {
            'item_id': line.itemId,
            'quantity': line.quantity,
            'serials': const <String>[],
          }
      ],
    };
    if (serverKind == 'receipt') {
      payload['dest_id'] = destLocationId ?? sourceLocationId;
    } else if (serverKind == 'issue') {
      payload['source_id'] = sourceLocationId ?? destLocationId;
    } else if (serverKind == 'transfer') {
      payload['source_id'] = sourceLocationId;
      payload['dest_id'] = destLocationId ?? sourceLocationId;
    } else {
      payload['source_id'] = sourceLocationId;
      payload['dest_id'] = destLocationId ?? sourceLocationId;
    }
    final res = await _post(
      '/inventory/requests',
      payload,
      extraHeaders: {'Idempotency-Key': _idemKey()},
    );
    return _parseStockRequest(res);
  }

  StockRequest _parseStockRequest(Map m) {
    final lines = (m['lines'] is List ? m['lines'] as List : const [])
        .whereType<Map>()
        .map((l) => StockRequestLine(
              itemId: _str(l, 'item_id'),
              quantity: _str(l, 'quantity', '0'),
            ))
        .toList();
    final maker = _str(m, 'maker_id');
    final sourceId = m['source_id']?.toString();
    final destId = m['dest_id']?.toString();
    return StockRequest(
      id: _str(m, 'id'),
      kind: _kind(_str(m, 'kind')),
      status: _status(_str(m, 'status', 'draft')),
      version: (m['version'] is int)
          ? m['version'] as int
          : int.tryParse(_str(m, 'version', '1')) ?? 1,
      createdBy: maker,
      createdByName: _str(m, 'maker_name', maker),
      locationId: sourceId ?? destId ?? '',
      items: lines,
      reason: _str(m, 'reason'),
      createdAt: _date(m, 'created_at'),
      approvedBy: m['approved_by']?.toString(),
      approvedByName: m['approved_by_name']?.toString(),
      rejectReason: m['reject_reason']?.toString(),
      destinationLocationId: destId,
    );
  }

  @override
  Future<StockRequest> submitStockRequest(String id) async =>
      _parseStockRequest(await _post(
        '/inventory/requests/$id/submit',
        {},
        extraHeaders: {'Idempotency-Key': _idemKey()},
      ));

  @override
  Future<StockRequest> approveStockRequest(String id) async =>
      _parseStockRequest(await _post(
        '/inventory/requests/$id/approve',
        {},
        extraHeaders: {'Idempotency-Key': _idemKey()},
      ));

  @override
  Future<StockRequest> rejectStockRequest(String id, String reason) async =>
      _parseStockRequest(await _post(
        '/inventory/requests/$id/reject',
        {'reason': reason},
        extraHeaders: {'Idempotency-Key': _idemKey()},
      ));

  // ---- cash-sim ----

  @override
  Future<List<Cashbox>> getCashboxes() async {
    final body = await _get('/cash-sim/boxes');
    return _asList(body, 'boxes')
        .whereType<Map>()
        .map((m) => Cashbox(
              id: _str(m, 'id'),
              code: _str(m, 'code'),
              name: _str(m, 'name'),
              openingBalance: _str(m, 'opening_balance', '0'),
            ))
        .toList();
  }

  @override
  Future<List<CashBalance>> getCashBalances() async {
    final body = await _get('/cash-sim/balances');
    return _asList(body, 'balances')
        .whereType<Map>()
        .map((m) => CashBalance(
              cashboxId: _str(m, 'cashbox_id'),
              amount: _str(m, 'amount', '0'),
              currency: _str(m, 'currency', 'VND'),
            ))
        .toList();
  }

  @override
  Future<List<JournalEntry>> getJournals() async {
    final body = await _get('/cash-sim/journals');
    return _asList(body, 'journals')
        .whereType<Map>()
        .map((m) => JournalEntry(
              id: _str(m, 'id'),
              cashboxId: _str(m, 'cashbox_id'),
              kind: _str(m, 'kind', 'receipt'),
              amount: _str(m, 'amount', '0'),
              currency: _str(m, 'currency', 'VND'),
              reason: _str(m, 'reason', _str(m, 'correlation_id')),
              createdAt: _date(m, 'posted_at'),
            ))
        .toList();
  }

  CashRequest _parseCashRequest(Map m) {
    final maker = _str(m, 'maker_id');
    return CashRequest(
      id: _str(m, 'id'),
      kind: _kind(_str(m, 'kind')),
      status: _status(_str(m, 'status', 'draft')),
      version: (m['version'] is int)
          ? m['version'] as int
          : int.tryParse(_str(m, 'version', '1')) ?? 1,
      createdBy: maker,
      createdByName: _str(m, 'maker_name', maker),
      cashboxId: _str(m, 'cashbox_id'),
      amount: _str(m, 'amount', '0'),
      currency: _str(m, 'currency', 'VND'),
      reason: _str(m, 'reason'),
      createdAt: _date(m, 'created_at'),
      approvedBy: m['approved_by']?.toString(),
      approvedByName: m['approved_by_name']?.toString(),
      rejectReason: m['reject_reason']?.toString(),
    );
  }

  @override
  Future<List<CashRequest>> getCashRequests() async {
    final body = await _get('/cash-sim/requests');
    return _asList(body, 'requests')
        .whereType<Map>()
        .map(_parseCashRequest)
        .toList();
  }

  @override
  Future<CashRequest> createCashRequest({
    required String cashboxId,
    required RequestKind kind,
    required String amount,
    String? reason,
  }) async {
    final res = await _post(
      '/cash-sim/requests',
      {
        'kind': kind == RequestKind.payment ? 'payment' : 'receipt',
        'cashbox_id': cashboxId,
        'amount': amount,
        'currency': 'VND',
        'reason': reason ?? '',
      },
      extraHeaders: {'Idempotency-Key': _idemKey()},
    );
    return _parseCashRequest(res);
  }

  @override
  Future<CashRequest> submitCashRequest(String id) async =>
      _parseCashRequest(await _post(
        '/cash-sim/requests/$id/submit',
        {},
        extraHeaders: {'Idempotency-Key': _idemKey()},
      ));

  @override
  Future<CashRequest> approveCashRequest(String id) async =>
      _parseCashRequest(await _post(
        '/cash-sim/requests/$id/approve',
        {},
        extraHeaders: {'Idempotency-Key': _idemKey()},
      ));

  @override
  Future<CashRequest> rejectCashRequest(String id, String reason) async =>
      _parseCashRequest(await _post(
        '/cash-sim/requests/$id/reject',
        {'reason': reason},
        extraHeaders: {'Idempotency-Key': _idemKey()},
      ));

  // ---- approvals ----

  @override
  Future<List<ApprovalItem>> getApprovalInbox() async {
    final body = await _get('/approvals/inbox');
    return _asList(body, 'approvals').whereType<Map>().map((m) {
      final id = _str(m, 'id');
      return ApprovalItem(
        id: id,
        kind: _str(m, 'kind'),
        maker: _str(m, 'maker'),
        impact: _str(m, 'impact', _str(m, 'status')),
        detail: _str(m, 'detail', id),
      );
    }).toList();
  }

  // ---- activity ----

  @override
  Future<List<ActivityEvent>> getActivity({String? cursor}) async {
    final body = await _get(
        '/activity/', {if (cursor != null) 'cursor': cursor});
    return _asList(body, 'events')
        .whereType<Map>()
        .map((m) => ActivityEvent(
              id: _str(m, 'id'),
              occurredAt: _date(m, 'occurred_at'),
              subjectId: _str(m, 'subject_id'),
              status: _str(m, 'status'),
              messageKey: _str(m, 'message_key', _str(m, 'status')),
              processed: m['processed'] is int ? m['processed'] as int : null,
              total: m['total'] is int ? m['total'] as int : null,
            ))
        .toList();
  }

  // ---- integrations / reconciliations / reports ----

  String _idemKey() =>
      'idem-${DateTime.now().microsecondsSinceEpoch}-${identityHashCode(this)}';

  StagingPreview _parseStagingPreview(Map<String, dynamic> body) {
    final rows = _asList(body, 'rows').whereType<Map>().map((r) {
      final rawData = r['data'] is Map
          ? (r['data'] as Map).map((k, v) => MapEntry(k.toString(), v))
          : <String, dynamic>{};
      final rawErrors = (r['errors'] is List ? r['errors'] as List : const [])
          .map((e) => e.toString())
          .toList();
      return StagingRow(
        rowIndex: r['row_index'] is int ? r['row_index'] as int : 0,
        sourceRowKey: _str(r, 'source_row_key'),
        data: rawData,
        valid: r['valid'] == true,
        errors: rawErrors,
      );
    }).toList();
    return StagingPreview(
      runId: _str(body, 'run_id'),
      validCount: body['valid_count'] is int ? body['valid_count'] as int : 0,
      invalidCount:
          body['invalid_count'] is int ? body['invalid_count'] as int : 0,
      deduplicated: body['deduplicated'] == true,
      mappingVersion:
          body['mapping_version'] is int ? body['mapping_version'] as int : 1,
      rows: rows,
    );
  }

  @override
  Future<List<IntegrationConnection>> getConnections() async {
    final body = await _get('/integrations/connections');
    return _asList(body, 'connections')
        .whereType<Map>()
        .map((m) => IntegrationConnection(
              id: _str(m, 'id'),
              name: _str(m, 'name'),
              endpoint: _str(m, 'type'),
              status: _str(m, 'status', 'connected'),
            ))
        .toList();
  }

  @override
  Future<List<IntegrationRun>> getIntegrationRuns() async {
    final body = await _get('/integrations/runs');
    return _asList(body, 'runs')
        .whereType<Map>()
        .map((m) => IntegrationRun(
              id: _str(m, 'id'),
              connectionId: _str(m, 'connection_id'),
              status: _str(m, 'status'),
              startedAt: _date(m, 'created_at'),
              processed:
                  m['valid_count'] is int ? m['valid_count'] as int : null,
              total: (m['valid_count'] is int && m['invalid_count'] is int)
                  ? (m['valid_count'] as int) + (m['invalid_count'] as int)
                  : null,
            ))
        .toList();
  }

  @override
  Future<StagingPreview> uploadImportFile({
    required String filename,
    required List<int> bytes,
    required String type,
  }) async {
    final req = http.MultipartRequest('POST', _uri('/integrations/uploads'));
    final headers = await _headers();
    headers.remove('Content-Type');
    headers['Idempotency-Key'] = _idemKey();
    req.headers.addAll(headers);
    req.fields['type'] = type;
    req.files.add(
      http.MultipartFile.fromBytes('file', bytes, filename: filename),
    );
    final streamed = await _client.send(req);
    final resp = await http.Response.fromStream(streamed);
    if (resp.statusCode < 200 || resp.statusCode >= 300) {
      _throwEnvelope(resp);
    }
    final decoded = json.decode(resp.body) as Map<String, dynamic>;
    return _parseStagingPreview(decoded);
  }

  @override
  Future<StagingPreview> fetchGoogleSheet({
    required String sheetId,
    String tab = 'Sheet1',
  }) async {
    final body = await _post(
      '/integrations/google-sheets/fetch',
      {'sheet_id': sheetId, 'tab': tab},
      extraHeaders: {'Idempotency-Key': _idemKey()},
    );
    return _parseStagingPreview(body);
  }

  @override
  Future<StagingPreview> getRunPreview(String runId) async {
    final body = await _get('/integrations/runs/$runId/preview');
    return _parseStagingPreview(body as Map<String, dynamic>);
  }

  @override
  Future<ConfirmRunResult> confirmRun(String runId) async {
    final body = await _post(
      '/integrations/runs/$runId/confirm',
      {},
      extraHeaders: {'Idempotency-Key': _idemKey()},
    );
    final drafts = _asList(body, 'draft_requests')
        .map((e) => e.toString())
        .toList();
    return ConfirmRunResult(
      runId: _str(body, 'run_id', runId),
      status: _str(body, 'status', 'confirmed'),
      draftRequests: drafts,
    );
  }

  ReconciliationRun _parseReconciliation(Map m) {
    final rawEx = m['exceptions'] is List
        ? m['exceptions'] as List
        : _asList(m, 'exceptions_json');
    return ReconciliationRun(
      id: _str(m, 'id'),
      status: _str(m, 'status'),
      startedAt: _date(m, 'created_at'),
      exceptions: rawEx.map((e) => e.toString()).toList(),
    );
  }

  @override
  Future<List<ReconciliationRun>> getReconciliations() async {
    final body = await _get('/reconciliations');
    final list = _asList(body, 'runs').isNotEmpty
        ? _asList(body, 'runs')
        : _asList(body, 'reconciliations');
    return list.whereType<Map>().map(_parseReconciliation).toList();
  }

  @override
  Future<ReconciliationRun> runReconciliation({String scope = 'cash'}) async {
    final body = await _post(
      '/reconciliations',
      {'scope': scope},
      extraHeaders: {'Idempotency-Key': _idemKey()},
    );
    return _parseReconciliation(body);
  }

  List<InventoryBucket> _parseInventoryBuckets(dynamic body) {
    return _asList(body, 'buckets')
        .whereType<Map>()
        .map((m) => InventoryBucket(
              bucket: _str(m, 'bucket'),
              inboundQty: _str(m, 'inbound_qty', '0'),
              outboundQty: _str(m, 'outbound_qty', '0'),
              netQty: _str(m, 'net_qty', '0'),
              receiptCount:
                  m['receipt_count'] is int ? m['receipt_count'] as int : 0,
              issueCount: m['issue_count'] is int ? m['issue_count'] as int : 0,
            ))
        .toList();
  }

  List<CashflowBucket> _parseCashflowBuckets(dynamic body) {
    return _asList(body, 'buckets')
        .whereType<Map>()
        .map((m) => CashflowBucket(
              bucket: _str(m, 'bucket'),
              totalIn: _str(m, 'total_in', '0'),
              totalOut: _str(m, 'total_out', '0'),
              net: _str(m, 'net', '0'),
              journalCount:
                  m['journal_count'] is int ? m['journal_count'] as int : 0,
            ))
        .toList();
  }

  @override
  Future<List<InventoryBucket>> getInventoryDaily({
    String? dateFrom,
    String? dateTo,
  }) async {
    final body = await _get('/reports/inventory-daily', {
      if (dateFrom != null) 'date_from': dateFrom,
      if (dateTo != null) 'date_to': dateTo,
    });
    return _parseInventoryBuckets(body);
  }

  @override
  Future<List<InventoryBucket>> getInventoryMonthly({
    String? dateFrom,
    String? dateTo,
  }) async {
    final body = await _get('/reports/inventory-monthly', {
      if (dateFrom != null) 'date_from': dateFrom,
      if (dateTo != null) 'date_to': dateTo,
    });
    return _parseInventoryBuckets(body);
  }

  @override
  Future<List<CashflowBucket>> getCashflowDaily({
    String? dateFrom,
    String? dateTo,
  }) async {
    final body = await _get('/reports/cashflow-daily', {
      if (dateFrom != null) 'date_from': dateFrom,
      if (dateTo != null) 'date_to': dateTo,
    });
    return _parseCashflowBuckets(body);
  }

  @override
  Future<List<CashflowBucket>> getCashflowMonthly({
    String? dateFrom,
    String? dateTo,
  }) async {
    final body = await _get('/reports/cashflow-monthly', {
      if (dateFrom != null) 'date_from': dateFrom,
      if (dateTo != null) 'date_to': dateTo,
    });
    return _parseCashflowBuckets(body);
  }

  @override
  Future<List<StagnationItem>> getStagnation({
    int watchDays = 30,
    int stagnantDays = 60,
    int criticalDays = 90,
  }) async {
    final body = await _get('/reports/stagnation', {
      'watch_days': '$watchDays',
      'stagnant_days': '$stagnantDays',
      'critical_days': '$criticalDays',
    });
    return _asList(body, 'items')
        .whereType<Map>()
        .map((m) => StagnationItem(
              itemId: _str(m, 'item_id'),
              sku: _str(m, 'sku'),
              name: _str(m, 'name'),
              quantity: _str(m, 'quantity', '0'),
              daysSinceLastIssue: m['days_since_last_issue'] is int
                  ? m['days_since_last_issue'] as int
                  : null,
              severity: _str(m, 'severity', 'normal'),
            ))
        .toList();
  }
}
