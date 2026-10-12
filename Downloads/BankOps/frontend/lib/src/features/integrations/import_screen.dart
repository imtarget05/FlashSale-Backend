import 'dart:convert';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import '../../app/app_shell.dart';
import '../../core/theme/app_theme.dart';
import '../../core/widgets/demo_banner.dart';
import '../../data/models/dtos.dart';

/// Màn nhập dữ liệu ngoại vi (CSV / JSON / Google Sheets) qua staging -> preview -> confirm -> draft.
class ImportScreen extends StatefulWidget {
  const ImportScreen({
    super.key,
    this.initialPreview,
    this.onConfirmed,
  });

  final StagingPreview? initialPreview;
  final ValueChanged<ConfirmRunResult>? onConfirmed;

  @override
  State<ImportScreen> createState() => _ImportScreenState();
}

class _ImportScreenState extends State<ImportScreen> {
  final _sheetIdCtrl = TextEditingController(
    text: '1BxiMVs0XRA5nFMdKvBdBZjgmUUqptlbs74OgvE2upms',
  );
  final _sheetTabCtrl = TextEditingController(text: 'Sheet1');

  StagingPreview? _preview;
  ConfirmRunResult? _confirmed;
  String? _error;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _preview = widget.initialPreview;
  }

  @override
  void dispose() {
    _sheetIdCtrl.dispose();
    _sheetTabCtrl.dispose();
    super.dispose();
  }

  Future<void> _pickAndUpload() async {
    final repo = RepositoryScope.of(context);
    final res = await FilePicker.platform.pickFiles(
      type: FileType.custom,
      allowedExtensions: ['csv', 'json'],
      withData: true,
    );
    if (res == null || res.files.isEmpty) return;
    final file = res.files.first;
    final bytes = file.bytes;
    if (bytes == null) return;
    final ext = (file.extension ?? 'csv').toLowerCase();
    await _uploadBytes(
      repoName: file.name,
      bytes: bytes,
      type: ext == 'json' ? 'json' : 'csv',
      repo: repo,
    );
  }

  Future<void> _uploadBytes({
    required String repoName,
    required List<int> bytes,
    required String type,
    required dynamic repo,
  }) async {
    setState(() {
      _busy = true;
      _error = null;
      _confirmed = null;
    });
    try {
      final preview = await repo.uploadImportFile(
        filename: repoName,
        bytes: bytes,
        type: type,
      ) as StagingPreview;
      if (!mounted) return;
      setState(() {
        _preview = preview;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = '$e';
      });
    } finally {
      if (mounted) {
        setState(() {
          _busy = false;
        });
      }
    }
  }

  Future<void> _loadSampleCsv({required bool includeInvalidRow}) async {
    final repo = RepositoryScope.of(context);
    final csv = includeInvalidRow
        ? 'sku,location_code,quantity,source_row_key\n'
            'DEMO-PAPER-A4,DEMO-HN,10,sample-ok-1\n'
            'INVALID-SKU,DEMO-HN,-5,sample-bad-2\n'
        : 'sku,location_code,quantity,source_row_key\n'
            'DEMO-PAPER-A4,DEMO-HN,10,sample-ok-1\n'
            'DEMO-LAPTOP,DEMO-HCM,2,sample-ok-2\n';
    await _uploadBytes(
      repoName: includeInvalidRow ? 'sample_mixed.csv' : 'sample_valid.csv',
      bytes: utf8.encode(csv),
      type: 'csv',
      repo: repo,
    );
  }

  Future<void> _fetchSheet() async {
    final repo = RepositoryScope.of(context);
    setState(() {
      _busy = true;
      _error = null;
      _confirmed = null;
    });
    try {
      final preview = await repo.fetchGoogleSheet(
        sheetId: _sheetIdCtrl.text.trim(),
        tab: _sheetTabCtrl.text.trim().isEmpty
            ? 'Sheet1'
            : _sheetTabCtrl.text.trim(),
      );
      if (!mounted) return;
      setState(() {
        _preview = preview;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = '$e';
      });
    } finally {
      if (mounted) {
        setState(() {
          _busy = false;
        });
      }
    }
  }

  Future<void> _confirmStaging() async {
    final preview = _preview;
    if (preview == null || !preview.canConfirm) return;
    final repo = RepositoryScope.of(context);
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final result = await repo.confirmRun(preview.runId);
      if (!mounted) return;
      setState(() {
        _confirmed = result;
      });
      widget.onConfirmed?.call(result);
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = '$e';
      });
    } finally {
      if (mounted) {
        setState(() {
          _busy = false;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final statusColors = context.statusColors;
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        const DemoBanner(),
        const SizedBox(height: 16),
        Card(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  '1. Tải file CSV / JSON hoặc kéo từ Google Sheets',
                  style: Theme.of(context).textTheme.titleMedium?.copyWith(
                        fontWeight: FontWeight.w700,
                      ),
                ),
                const SizedBox(height: 8),
                Text(
                  'Dữ liệu luôn vào vùng tạm (staging) để kiểm tra từng dòng trước khi xác nhận tạo phiếu nháp (Draft). Không bao giờ ghi thẳng vào sổ cái.',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
                const SizedBox(height: 12),
                Wrap(
                  spacing: 12,
                  runSpacing: 8,
                  children: [
                    FilledButton.icon(
                      key: const Key('import-pick-file-btn'),
                      onPressed: _busy ? null : _pickAndUpload,
                      icon: const Icon(Icons.upload_file_outlined),
                      label: const Text('Chọn file CSV / JSON'),
                    ),
                    OutlinedButton.icon(
                      key: const Key('import-sample-valid-btn'),
                      onPressed: _busy
                          ? null
                          : () => _loadSampleCsv(includeInvalidRow: false),
                      icon: const Icon(Icons.check_circle_outline),
                      label: const Text('Mẫu CSV hợp lệ'),
                    ),
                    OutlinedButton.icon(
                      key: const Key('import-sample-invalid-btn'),
                      onPressed: _busy
                          ? null
                          : () => _loadSampleCsv(includeInvalidRow: true),
                      icon: const Icon(Icons.bug_report_outlined),
                      label: const Text('Mẫu CSV có dòng lỗi'),
                    ),
                  ],
                ),
                const Divider(height: 28),
                Row(
                  children: [
                    Expanded(
                      flex: 3,
                      child: TextField(
                        key: const Key('import-sheet-id-field'),
                        controller: _sheetIdCtrl,
                        decoration: const InputDecoration(
                          labelText: 'Google Sheet ID (Sandbox)',
                          border: OutlineInputBorder(),
                          isDense: true,
                        ),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: TextField(
                        key: const Key('import-sheet-tab-field'),
                        controller: _sheetTabCtrl,
                        decoration: const InputDecoration(
                          labelText: 'Tab',
                          border: OutlineInputBorder(),
                          isDense: true,
                        ),
                      ),
                    ),
                    const SizedBox(width: 8),
                    FilledButton.tonalIcon(
                      key: const Key('import-fetch-sheet-btn'),
                      onPressed: _busy ? null : _fetchSheet,
                      icon: const Icon(Icons.cloud_download_outlined),
                      label: const Text('Kéo Sheet'),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
        if (_busy) ...[
          const SizedBox(height: 16),
          const LinearProgressIndicator(),
        ],
        if (_error != null) ...[
          const SizedBox(height: 16),
          Card(
            color: Theme.of(context).colorScheme.errorContainer,
            child: ListTile(
              leading: Icon(Icons.error_outline, color: statusColors.danger),
              title: Text(_error!),
            ),
          ),
        ],
        if (_confirmed != null) ...[
          const SizedBox(height: 16),
          Card(
            key: const Key('import-confirmed-banner'),
            color: statusColors.demoBadgeBg,
            child: ListTile(
              leading:
                  Icon(Icons.check_circle, color: statusColors.success),
              title: Text(
                'Đã tạo ${_confirmed!.draftRequests.length} phiếu nháp: ${_confirmed!.draftRequests.join(', ')}',
              ),
              subtitle: const Text(
                'Phiếu đang ở trạng thái Draft và sẽ đi qua quy trình Maker-Checker chuẩn.',
              ),
            ),
          ),
        ],
        if (_preview != null) ...[
          const SizedBox(height: 16),
          StagingPreviewView(
            preview: _preview!,
            busy: _busy,
            confirmed: _confirmed != null,
            onConfirm: _confirmStaging,
          ),
        ],
      ],
    );
  }
}

class StagingPreviewView extends StatelessWidget {
  const StagingPreviewView({
    super.key,
    required this.preview,
    required this.onConfirm,
    this.busy = false,
    this.confirmed = false,
  });

  final StagingPreview preview;
  final VoidCallback onConfirm;
  final bool busy;
  final bool confirmed;

  @override
  Widget build(BuildContext context) {
    final statusColors = context.statusColors;
    final canConfirm = preview.canConfirm && !busy && !confirmed;

    return Card(
      key: const Key('staging-preview-card'),
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    '2. Xem trước Staging (${preview.runId})',
                    style: Theme.of(context).textTheme.titleMedium?.copyWith(
                          fontWeight: FontWeight.w700,
                        ),
                  ),
                ),
                FilledButton.icon(
                  key: const Key('import-confirm-btn'),
                  onPressed: canConfirm ? onConfirm : null,
                  icon: const Icon(Icons.playlist_add_check),
                  label: Text(
                    confirmed
                        ? 'Đã xác nhận'
                        : 'Xác nhận tạo phiếu nháp (Draft)',
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            Wrap(
              spacing: 12,
              runSpacing: 8,
              children: [
                Chip(
                  key: const Key('staging-valid-chip'),
                  avatar: Icon(
                    Icons.check_circle_outline,
                    size: 18,
                    color: statusColors.success,
                  ),
                  label: Text('Hợp lệ: ${preview.validCount}'),
                ),
                Chip(
                  key: const Key('staging-invalid-chip'),
                  avatar: Icon(
                    Icons.error_outline,
                    size: 18,
                    color: preview.invalidCount > 0
                        ? statusColors.danger
                        : statusColors.info,
                  ),
                  label: Text('Lỗi: ${preview.invalidCount}'),
                ),
                if (preview.deduplicated)
                  const Chip(
                    label: Text('Đã chống trùng (Deduplicated)'),
                  ),
              ],
            ),
            if (preview.invalidCount > 0) ...[
              const SizedBox(height: 8),
              Text(
                'Có ${preview.invalidCount} dòng lỗi. Vui lòng sửa dữ liệu nguồn và tải lại trước khi xác nhận.',
                style: TextStyle(
                  color: statusColors.danger,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ],
            const SizedBox(height: 12),
            for (final row in preview.rows)
              ListTile(
                key: Key('staging-row-${row.rowIndex}'),
                dense: true,
                leading: Icon(
                  row.valid ? Icons.check_circle : Icons.cancel,
                  color: row.valid ? statusColors.success : statusColors.danger,
                ),
                title: Text(
                  'Dòng #${row.rowIndex + 1} (${row.sourceRowKey}) — SKU: ${row.data['sku'] ?? row.data['item_sku'] ?? '-'} · Kho: ${row.data['location_code'] ?? row.data['location'] ?? '-'} · SL: ${row.data['quantity'] ?? '-'}',
                ),
                subtitle: Text(
                  row.valid ? 'Hợp lệ' : 'Lỗi: ${row.errors.join('; ')}',
                  style: TextStyle(
                    color:
                        row.valid ? statusColors.success : statusColors.danger,
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}
