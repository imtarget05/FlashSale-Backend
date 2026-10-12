import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:google_sign_in/google_sign_in.dart';
import 'package:http/http.dart' as http;
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../../data/models/dtos.dart';

/// Trạng thái đăng nhập dùng chung cho app.
///
/// Luồng Google (không mật khẩu, không chạm bcrypt):
///   1. `GoogleSignIn.signIn()` trên thiết bị -> Google ID token.
///   2. `POST {baseUrl}/auth/google {"id_token": ...}`.
///   3. Backend verify + upsert user (lần đầu = signup, `is_new_user=true`)
///      rồi trả access/refresh JWT của BankOps.
///   4. Token lưu `flutter_secure_storage`; logout xóa local + sign-out Google.
///
/// CHƯA VERIFY: viết tay, chưa chạy `flutter analyze`/`flutter test`
/// (máy dev không có Flutter SDK). Cần chạy analyze + test thiết bị trước khi merge.
class AuthService extends ChangeNotifier {
  AuthService({
    String? baseUrl,
    String? googleClientId,
    GoogleSignIn? googleSignIn,
    Future<String?> Function()? googleIdTokenProvider,
    FlutterSecureStorage? storage,
    http.Client? httpClient,
  })  : _baseUrl = normalizeApiBaseUrl(baseUrl ?? _defaultBaseUrl),
        _googleClientId = (googleClientId ?? _defaultGoogleClientId).trim(),
        _hasCustomGoogleProvider =
            googleSignIn != null || googleIdTokenProvider != null,
        _googleIdTokenProvider = googleIdTokenProvider,
        _google = googleSignIn,
        _storage = storage ?? const FlutterSecureStorage(),
        _http = httpClient ?? http.Client();

  static const String _defaultGoogleClientId =
      String.fromEnvironment('GOOGLE_CLIENT_ID', defaultValue: '');

  /// Chuẩn hóa mọi URL gốc về dạng `.../api/v1` (không có dấu `/` cuối).
  static String normalizeApiBaseUrl(String raw) {
    var trimmed = raw.trim();
    while (trimmed.endsWith('/')) {
      trimmed = trimmed.substring(0, trimmed.length - 1);
    }
    if (!trimmed.endsWith('/api/v1')) {
      trimmed = '$trimmed/api/v1';
    }
    return trimmed;
  }

  /// Per-platform default: Android emulator cannot reach host `localhost`,
  /// so use `10.0.2.2`; iOS simulator/desktop/web keep `localhost`.
  /// Overridable via `--dart-define=API_BASE_URL=...`.
  static String get _defaultBaseUrl {
    const env = String.fromEnvironment('API_BASE_URL', defaultValue: '');
    if (env.isNotEmpty) return env;
    if (kIsWeb) return 'http://localhost:8000/api/v1';
    switch (defaultTargetPlatform) {
      case TargetPlatform.android:
        return 'http://10.0.2.2:8000/api/v1';
      case TargetPlatform.iOS:
      case TargetPlatform.macOS:
      case TargetPlatform.windows:
      case TargetPlatform.linux:
      case TargetPlatform.fuchsia:
        return 'http://localhost:8000/api/v1';
    }
  }

  final String _baseUrl;
  final String _googleClientId;
  final bool _hasCustomGoogleProvider;
  final Future<String?> Function()? _googleIdTokenProvider;
  GoogleSignIn? _google;
  final FlutterSecureStorage _storage;
  final http.Client _http;

  GoogleSignIn _resolveGoogleSignIn() {
    return _google ??= GoogleSignIn(
      clientId: _googleClientId.isEmpty ? null : _googleClientId,
      scopes: const ['email', 'profile', 'openid'],
    );
  }

  AppUser? _user;
  String? _memoryToken;
  bool _isLoading = true;
  String? _lastError;

  String get apiBaseUrl => _baseUrl;
  String get googleClientId => _googleClientId;
  bool get isGoogleSignInConfigured =>
      !kIsWeb || _googleClientId.isNotEmpty || _hasCustomGoogleProvider;
  AppUser? get user => _user;
  bool get isLoading => _isLoading;
  String? get lastError => _lastError;

  Future<String?> getAccessToken() async {
    if (_memoryToken != null && _memoryToken!.isNotEmpty) {
      return _memoryToken;
    }
    return (await _readStorage('access_token')) ?? _memoryToken;
  }

  Future<String?> _readStorage(String key) async {
    if (_googleIdTokenProvider != null && _storage == const FlutterSecureStorage()) {
      return null;
    }
    try {
      return await _storage
          .read(key: key)
          .timeout(const Duration(milliseconds: 250));
    } catch (_) {
      return null;
    }
  }

  Future<void> _writeStorage(String key, String? value) async {
    if (_googleIdTokenProvider != null && _storage == const FlutterSecureStorage()) {
      return;
    }
    try {
      await _storage
          .write(key: key, value: value)
          .timeout(const Duration(milliseconds: 250));
    } catch (_) {}
  }

  Future<Map<String, String>> _authHeaders() async {
    final token = await getAccessToken();
    return {
      'Content-Type': 'application/json',
      if (token != null && token.isNotEmpty) 'Authorization': 'Bearer $token',
    };
  }

  /// Tự đăng nhập khi mở app nếu còn token hợp lệ (qua `GET /api/v1/auth/me`).
  Future<void> tryAutoLogin() async {
    _isLoading = true;
    notifyListeners();
    try {
      final token = await getAccessToken();
      if (token == null || token.isEmpty) {
        _user = null;
        return;
      }
      final resp = await _http.get(
        Uri.parse('$_baseUrl/auth/me'),
        headers: {'Authorization': 'Bearer $token'},
      );
      if (resp.statusCode == 200) {
        _user = _userFromJson(json.decode(resp.body) as Map<String, dynamic>);
      } else {
        await _clearLocal();
        _user = null;
      }
    } catch (_) {
      _user = null;
    } finally {
      _isLoading = false;
      notifyListeners();
    }
  }

  /// Đăng nhập bằng username + password (dùng cho tài khoản demo hoặc nhân viên).
  Future<bool> signInWithPassword({
    required String username,
    required String password,
  }) async {
    _lastError = null;
    notifyListeners();
    try {
      final resp = await _http.post(
        Uri.parse('$_baseUrl/auth/login'),
        headers: const {'Content-Type': 'application/x-www-form-urlencoded'},
        body: {
          'username': username.trim(),
          'password': password,
        },
      );
      if (resp.statusCode != 200) {
        _lastError = _serverMessage(resp.body);
        notifyListeners();
        return false;
      }
      final body = json.decode(resp.body) as Map<String, dynamic>;
      final accessToken = body['access_token'] as String;
      final refreshToken = body['refresh_token'] as String?;
      _memoryToken = accessToken;
      await _writeStorage('access_token', accessToken);
      await _writeStorage('refresh_token', refreshToken);
      final u = body['user'] as Map<String, dynamic>;
      _user = _userFromJson(u);
      notifyListeners();
      return true;
    } catch (e) {
      _lastError = 'Không kết nối được máy chủ ($_baseUrl): $e';
      notifyListeners();
      return false;
    }
  }

  /// Đăng nhập/đăng ký bằng Google. Lần đầu backend tự tạo user (`is_new_user`).
  /// Trả `true` khi có token BankOps.
  Future<bool> signInWithGoogle() async {
    _lastError = null;
    notifyListeners();
    if (!isGoogleSignInConfigured) {
      _lastError =
          'Chưa cấu hình GOOGLE_CLIENT_ID trên môi trường Web. Vui lòng dùng tài khoản Demo (Maker / Checker / Auditor) ở trên.';
      notifyListeners();
      return false;
    }
    try {
      String? idToken;
      if (_googleIdTokenProvider != null) {
        idToken = await _googleIdTokenProvider();
        if (idToken == null) {
          _lastError = 'Đã hủy đăng nhập Google.';
          notifyListeners();
          return false;
        }
      } else {
        final account = await _resolveGoogleSignIn().signIn();
        if (account == null) {
          _lastError = 'Đã hủy đăng nhập Google.';
          notifyListeners();
          return false; // user bấm cancel, không phải lỗi
        }
        final auth = await account.authentication;
        idToken = auth.idToken;
      }
      if (idToken == null || idToken.isEmpty) {
        _lastError = 'Google không trả ID token (kiểm tra cấu hình OAuth client).';
        notifyListeners();
        return false;
      }
      final resp = await _http.post(
        Uri.parse('$_baseUrl/auth/google'),
        headers: const {'Content-Type': 'application/json'},
        body: json.encode({'id_token': idToken}),
      );
      if (resp.statusCode != 200) {
        _lastError = _serverMessage(resp.body);
        notifyListeners();
        return false;
      }
      final body = json.decode(resp.body) as Map<String, dynamic>;
      final accessToken = body['access_token'] as String;
      final refreshToken = body['refresh_token'] as String?;
      _memoryToken = accessToken;
      await _writeStorage('access_token', accessToken);
      await _writeStorage('refresh_token', refreshToken);
      final u = body['user'] as Map<String, dynamic>;
      _user = _userFromJson(u);
      if (body['is_new_user'] == true) {
        _lastError = null;
      }
      notifyListeners();
      return true;
    } catch (e) {
      _lastError = 'Đăng nhập thất bại: $e';
      notifyListeners();
      return false;
    }
  }

  /// Logout: gọi backend (marker) + xóa token local + sign-out Google trên máy.
  Future<void> signOut() async {
    try {
      final headers = await _authHeaders();
      await _http.post(Uri.parse('$_baseUrl/auth/logout'), headers: headers);
    } catch (_) {
      // backend không với tới vẫn phải xóa local để user thoát được
    }
    try {
      await _google?.signOut();
    } catch (_) {}
    await _clearLocal();
    _user = null;
    notifyListeners();
  }

  Future<void> _clearLocal() async {
    _memoryToken = null;
    if (_googleIdTokenProvider != null && _storage == const FlutterSecureStorage()) {
      return;
    }
    try {
      await Future.wait([
        _storage.delete(key: 'access_token'),
        _storage.delete(key: 'refresh_token'),
      ]).timeout(const Duration(milliseconds: 250));
    } catch (_) {}
  }

  String _serverMessage(String body) {
    try {
      final decoded = json.decode(body);
      if (decoded is Map) {
        final err = decoded['error'];
        if (err is Map && err['message'] is String) return err['message'] as String;
        if (decoded['detail'] is String) return decoded['detail'] as String;
      }
    } catch (_) {}
    return 'Máy chủ từ chối đăng nhập.';
  }

  AppUser _userFromJson(Map<String, dynamic> json) => AppUser(
        id: json['id'] as String,
        username: json['username'] as String,
        displayName: (json['display_name'] ?? json['username']) as String,
        roles: List<String>.from(json['roles'] as List? ?? const []),
      );
}
