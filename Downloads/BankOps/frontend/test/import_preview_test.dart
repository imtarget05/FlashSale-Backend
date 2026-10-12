import 'package:bankops/src/app/app_shell.dart';
import 'package:bankops/src/core/theme/app_theme.dart';
import 'package:bankops/src/data/repositories/fake_repository.dart';
import 'package:bankops/src/features/integrations/import_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  testWidgets(
    'ImportScreen blocks confirm when staging has invalid rows and creates draft when valid',
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
            home: const Scaffold(body: ImportScreen()),
          ),
        ),
      );
      await tester.pumpAndSettle();

      // 1. Load CSV with invalid row
      await tester.tap(find.byKey(const Key('import-sample-invalid-btn')));
      await tester.pumpAndSettle();

      expect(find.text('Hợp lệ: 1'), findsOneWidget);
      expect(find.text('Lỗi: 1'), findsOneWidget);
      expect(
        find.textContaining('Vui lòng sửa dữ liệu nguồn và tải lại'),
        findsOneWidget,
      );

      final disabledBtn = tester.widget<ButtonStyleButton>(
        find.byKey(const Key('import-confirm-btn')),
      );
      expect(disabledBtn.onPressed, isNull);

      // 2. Load valid CSV
      await tester.tap(find.byKey(const Key('import-sample-valid-btn')));
      await tester.pumpAndSettle();

      expect(find.text('Hợp lệ: 2'), findsOneWidget);
      expect(find.text('Lỗi: 0'), findsOneWidget);

      final enabledBtn = tester.widget<ButtonStyleButton>(
        find.byKey(const Key('import-confirm-btn')),
      );
      expect(enabledBtn.onPressed, isNotNull);

      // 3. Confirm -> creates draft request
      await tester.tap(find.byKey(const Key('import-confirm-btn')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('import-confirmed-banner')), findsOneWidget);
      expect(find.textContaining('Đã tạo 1 phiếu nháp'), findsOneWidget);
    },
  );
}
