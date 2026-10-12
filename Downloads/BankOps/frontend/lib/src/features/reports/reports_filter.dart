import 'package:flutter/material.dart';

enum ReportGranularity { daily, monthly }

class ReportsFilter extends StatelessWidget {
  const ReportsFilter({
    super.key,
    required this.granularity,
    required this.onGranularityChanged,
    this.dateRange,
    this.onDateRangeChanged,
  });

  final ReportGranularity granularity;
  final ValueChanged<ReportGranularity> onGranularityChanged;
  final DateTimeRange? dateRange;
  final ValueChanged<DateTimeRange?>? onDateRangeChanged;

  @override
  Widget build(BuildContext context) {
    return Wrap(
      spacing: 12,
      runSpacing: 8,
      crossAxisAlignment: WrapCrossAlignment.center,
      children: [
        SegmentedButton<ReportGranularity>(
          key: const Key('reports-granularity-toggle'),
          segments: const [
            ButtonSegment(
              value: ReportGranularity.daily,
              label: Text('Theo ngày'),
              icon: Icon(Icons.calendar_today_outlined, size: 16),
            ),
            ButtonSegment(
              value: ReportGranularity.monthly,
              label: Text('Theo tháng'),
              icon: Icon(Icons.date_range_outlined, size: 16),
            ),
          ],
          selected: {granularity},
          onSelectionChanged: (set) {
            if (set.isNotEmpty) onGranularityChanged(set.first);
          },
        ),
        if (onDateRangeChanged != null)
          OutlinedButton.icon(
            key: const Key('reports-date-range-btn'),
            icon: const Icon(Icons.filter_alt_outlined, size: 18),
            label: Text(
              dateRange == null
                  ? 'Tất cả thời gian'
                  : '${dateRange!.start.toIso8601String().substring(0, 10)} → ${dateRange!.end.toIso8601String().substring(0, 10)}',
            ),
            onPressed: () async {
              final picked = await showDateRangePicker(
                context: context,
                firstDate: DateTime(2025),
                lastDate: DateTime(2027),
                initialDateRange: dateRange,
              );
              if (picked != null) {
                onDateRangeChanged!(picked);
              }
            },
          ),
        if (dateRange != null && onDateRangeChanged != null)
          IconButton(
            tooltip: 'Xoá bộ lọc ngày',
            icon: const Icon(Icons.clear, size: 18),
            onPressed: () => onDateRangeChanged!(null),
          ),
      ],
    );
  }
}
