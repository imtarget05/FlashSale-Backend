import 'dart:convert';

import 'package:bankops/src/data/models/dtos.dart';
import 'package:bankops/src/data/repositories/http_bankops_repository.dart';
import 'package:bankops/src/features/auth/auth_service.dart';
import 'package:bankops/src/features/auth/login_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:provider/provider.dart';

void main() {
  test('AuthService.normalizeApiBaseUrl unifies /api/v1 suffix', () {
    expect(
      AuthService.normalizeApiBaseUrl('http://localhost:8000'),
      'http://localhost:8000/api/v1',
    );
    expect(
      AuthService.normalizeApiBaseUrl('http://localhost:8000/'),
      'http://localhost:8000/api/v1',
    );
    expect(
      AuthService.normalizeApiBaseUrl('http://localhost:8000/api/v1'),
      'http://localhost:8000/api/v1',
    );
    expect(
      AuthService.normalizeApiBaseUrl('http://localhost:8000/api/v1/'),
      'http://localhost:8000/api/v1',
    );
  });

  test(
    'HttpBankOpsRepository calls /api/v1 endpoints, maps submitted/issue, and sends Idempotency-Key',
    () async {
      final capturedPaths = <String>[];
      final capturedHeaders = <Map<String, String>>[];

      http.Response jsonResp(Map<String, dynamic> body, [int status = 200]) =>
          http.Response.bytes(
            utf8.encode(json.encode(body)),
            status,
            headers: {'content-type': 'application/json; charset=utf-8'},
          );

      final mockClient = MockClient((request) async {
        capturedPaths.add(request.url.path);
        capturedHeaders.add(request.headers);

        if (request.url.path == '/api/v1/inventory/requests' &&
            request.method == 'POST') {
          final body = json.decode(request.body) as Map<String, dynamic>;
          expect(body['kind'], 'issue');
          return jsonResp({
            'id': 'sr-101',
            'kind': 'issue',
            'status': 'draft',
            'version': 1,
            'maker_id': 'u_maker',
            'source_id': 'loc-1',
            'reason': 'Xuất kho test',
            'lines': [
              {'item_id': 'item-1', 'quantity': '5'}
            ],
          }, 201);
        }

        if (request.url.path == '/api/v1/inventory/requests/sr-101/submit' &&
            request.method == 'POST') {
          return jsonResp({
            'id': 'sr-101',
            'kind': 'issue',
            'status': 'submitted',
            'version': 2,
            'maker_id': 'u_maker',
          });
        }

        if (request.url.path == '/api/v1/inventory/requests/sr-101/approve' &&
            request.method == 'POST') {
          expect(request.headers['Idempotency-Key'], isNotEmpty);
          return jsonResp({
            'id': 'sr-101',
            'kind': 'issue',
            'status': 'posted',
            'version': 3,
            'maker_id': 'u_maker',
            'approved_by': 'u_checker',
          });
        }

        if (request.url.path == '/api/v1/cash-sim/requests' &&
            request.method == 'POST') {
          return jsonResp({
            'id': 'cr-202',
            'kind': 'receipt',
            'status': 'draft',
            'version': 1,
            'maker_id': 'u_maker',
            'cashbox_id': 'cb-1',
            'amount': '500000.00',
            'currency': 'VND',
            'reason': 'Thu quỹ test',
          }, 201);
        }

        if (request.url.path == '/api/v1/approvals/inbox') {
          return jsonResp({
            'approvals': [
              {
                'id': 'cr-202',
                'kind': 'cash',
                'maker': 'demo_maker',
                'impact': '+500000.00 VND',
                'detail': 'Thu quỹ test',
              }
            ],
            'simulation': true,
          });
        }

        if (request.url.path == '/api/v1/reconciliations') {
          return jsonResp({
            'runs': [
              {
                'id': 'rc-1',
                'status': 'matched',
                'exceptions': <String>[],
                'created_at': '2026-10-12T00:00:00Z',
              }
            ],
          });
        }

        return jsonResp({});
      });

      final repo = HttpBankOpsRepository(
        baseUrl: 'http://localhost:8000',
        client: mockClient,
        tokenProvider: () async => 'test-jwt-token',
      );

      final createdStock = await repo.createStockRequest(
        kind: RequestKind.payment,
        sourceLocationId: 'loc-1',
        lines: const [StockRequestLine(itemId: 'item-1', quantity: '5')],
        reason: 'Xuất kho test',
      );
      expect(createdStock.id, 'sr-101');
      expect(createdStock.kind, RequestKind.payment);
      expect(createdStock.status, RequestStatus.draft);

      final submittedStock = await repo.submitStockRequest('sr-101');
      expect(submittedStock.status, RequestStatus.pending);

      final approvedStock = await repo.approveStockRequest('sr-101');
      expect(approvedStock.status, RequestStatus.posted);

      final createdCash = await repo.createCashRequest(
        cashboxId: 'cb-1',
        kind: RequestKind.receipt,
        amount: '500000.00',
        reason: 'Thu quỹ test',
      );
      expect(createdCash.id, 'cr-202');

      final inbox = await repo.getApprovalInbox();
      expect(inbox, hasLength(1));
      expect(inbox.first.kind, 'cash');
      expect(inbox.first.impact, '+500000.00 VND');

      final reconciliations = await repo.getReconciliations();
      expect(reconciliations, hasLength(1));
      expect(reconciliations.first.status, 'matched');

      expect(
        capturedHeaders.first['Authorization'],
        'Bearer test-jwt-token',
      );
      expect(
        capturedPaths,
        containsAll([
          '/api/v1/inventory/requests',
          '/api/v1/inventory/requests/sr-101/submit',
          '/api/v1/inventory/requests/sr-101/approve',
          '/api/v1/cash-sim/requests',
          '/api/v1/approvals/inbox',
          '/api/v1/reconciliations',
        ]),
      );
    },
  );

  test(
    'AuthService.signInWithGoogle posts id_token to /api/v1/auth/google and sets user',
    () async {
      String? postedPath;
      Map<String, dynamic>? postedBody;

      final mockClient = MockClient((request) async {
        postedPath = request.url.path;
        postedBody = json.decode(request.body) as Map<String, dynamic>;
        return http.Response.bytes(
          utf8.encode(
            json.encode({
              'access_token': 'google-bankops-jwt',
              'refresh_token': 'google-bankops-refresh',
              'token_type': 'bearer',
              'expires_in': 1800,
              'is_new_user': true,
              'user': {
                'id': 'u-google-1',
                'username': 'staff@example.com',
                'display_name': 'Staff Google',
                'roles': ['staff'],
                'capabilities': ['inventory.view'],
              },
            }),
          ),
          200,
          headers: {'content-type': 'application/json; charset=utf-8'},
        );
      });

      final auth = AuthService(
        baseUrl: 'http://localhost:8000',
        httpClient: mockClient,
        googleIdTokenProvider: () async => 'mock-google-id-token-123456',
      );

      final ok = await auth.signInWithGoogle();
      expect(ok, isTrue);
      expect(postedPath, '/api/v1/auth/google');
      expect(postedBody?['id_token'], 'mock-google-id-token-123456');
      expect(auth.user?.username, 'staff@example.com');
      expect(auth.user?.roles, contains('staff'));
      expect(await auth.getAccessToken(), 'google-bankops-jwt');
    },
  );

  testWidgets(
    'LoginScreen Google Sign-In button invokes /api/v1/auth/google',
    (tester) async {
      String? hitEndpoint;
      final mockClient = MockClient((request) async {
        hitEndpoint = request.url.path;
        return http.Response.bytes(
          utf8.encode(
            json.encode({
              'access_token': 'jwt-google-ui',
              'refresh_token': 'ref-google-ui',
              'token_type': 'bearer',
              'expires_in': 1800,
              'is_new_user': false,
              'user': {
                'id': 'u-g-2',
                'username': 'google_user@bankops.demo',
                'display_name': 'Google User',
                'roles': ['staff'],
                'capabilities': [],
              },
            }),
          ),
          200,
          headers: {'content-type': 'application/json; charset=utf-8'},
        );
      });

      final auth = AuthService(
        baseUrl: 'http://localhost:8000',
        httpClient: mockClient,
        googleIdTokenProvider: () async => 'ui-google-token-999999',
      );

      await tester.pumpWidget(
        ChangeNotifierProvider<AuthService>.value(
          value: auth,
          child: const MaterialApp(home: LoginScreen()),
        ),
      );

      final googleBtn = find.byKey(const Key('login-google-btn'));
      expect(googleBtn, findsOneWidget);
      await tester.ensureVisible(googleBtn);
      await tester.tap(googleBtn);
      await tester.pumpAndSettle();

      expect(hitEndpoint, '/api/v1/auth/google');
      expect(auth.user?.username, 'google_user@bankops.demo');
    },
  );
}


