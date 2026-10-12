import 'package:bankops/src/app/app_shell.dart';
import 'package:bankops/src/core/theme/app_theme.dart';
import 'package:bankops/src/data/repositories/fake_repository.dart';
import 'package:bankops/src/features/reports/reports_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets(
    'ReportsScreen renders inventory/cashflow charts, toggles daily/monthly, and shows stagnation',
    (WidgetTester tester) async {
      tester.view.physicalSize = const Size(1400, 900);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.resetPhysicalSize);

      final repo = FakeBankOpsRepository(latency: Duration.zero);
      await tester.pumpWidget(
        RepositoryScope(
          repository: repo,
          child: MaterialApp(
            theme: AppTheme.light(),
            home: const Scaffold(body: ReportsScreen()),
          ),
        ),
      );
      await tester.pumpAndSettle();

      // Verify tabs and daily buckets
      expect(find.text('Báo cáo & Biểu đồ'), findsOneWidget);
      expect(find.text('Nhập/Xuất kho'), findsOneWidget);
      expect(find.text('Dòng tiền mô phỏng'), findsOneWidget);
      expect(find.text('Tồn đọng'), findsOneWidget);
      expect(find.text('Đối soát'), findsOneWidget);
      expect(find.text('2026-10-01'), findsOneWidget);

      // Toggle to monthly granularity
      await tester.tap(find.text('Theo tháng'));
      await tester.pumpAndSettle();
      expect(find.text('2026-08'), findsOneWidget);
      expect(find.text('2026-10'), findsOneWidget);

      // Switch to Cashflow tab
      await tester.tap(find.byKey(const Key('tab-reports-cashflow')));
      await tester.pumpAndSettle();
      expect(find.textContaining('Thu quỹ mô phỏng (VND)'), findsOneWidget);

      // Switch to Stagnation tab
      await tester.tap(find.byKey(const Key('tab-reports-stagnation')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('stagnation-item-DEMO-LAPTOP')), findsOneWidget);
      expect(find.text('Nghiêm trọng (>=90 ngày)'), findsOneWidget);
    },
  );
}
