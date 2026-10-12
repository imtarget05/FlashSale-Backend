import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'auth_service.dart';

/// Màn hình đăng nhập: hỗ trợ đăng nhập tài khoản (Maker/Checker/Auditor) + Google Sign-In.
class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  final _usernameCtrl = TextEditingController(text: 'demo_maker');
  final _passwordCtrl = TextEditingController(text: 'DemoPass123!');
  bool _busy = false;

  @override
  void dispose() {
    _usernameCtrl.dispose();
    _passwordCtrl.dispose();
    super.dispose();
  }

  Future<void> _loginWithPassword([String? presetUser]) async {
    if (_busy) return;
    if (presetUser != null) {
      _usernameCtrl.text = presetUser;
      _passwordCtrl.text = 'DemoPass123!';
    }
    setState(() => _busy = true);
    try {
      final ok = await context.read<AuthService>().signInWithPassword(
            username: _usernameCtrl.text,
            password: _passwordCtrl.text,
          );
      if (!mounted) return;
      if (!ok) {
        final msg =
            context.read<AuthService>().lastError ?? 'Đăng nhập thất bại.';
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(msg)));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _loginWithGoogle() async {
    if (_busy) return;
    setState(() => _busy = true);
    try {
      final ok = await context.read<AuthService>().signInWithGoogle();
      if (!mounted) return;
      if (!ok) {
        final msg =
            context.read<AuthService>().lastError ?? 'Đăng nhập thất bại.';
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(msg)));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      body: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 420),
            child: Card(
              child: Padding(
                padding: const EdgeInsets.all(24),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Text(
                      'BankOps',
                      style: theme.textTheme.headlineMedium?.copyWith(
                        fontWeight: FontWeight.w800,
                      ),
                      textAlign: TextAlign.center,
                    ),
                    const SizedBox(height: 8),
                    Text(
                      'Dữ liệu giả lập — không phải ngân hàng thật.',
                      style: theme.textTheme.bodySmall,
                      textAlign: TextAlign.center,
                    ),
                    const SizedBox(height: 20),
                    TextField(
                      key: const Key('login-username-field'),
                      controller: _usernameCtrl,
                      enabled: !_busy,
                      decoration: const InputDecoration(
                        labelText: 'Tên đăng nhập',
                        prefixIcon: Icon(Icons.person_outline),
                        border: OutlineInputBorder(),
                        isDense: true,
                      ),
                    ),
                    const SizedBox(height: 12),
                    TextField(
                      key: const Key('login-password-field'),
                      controller: _passwordCtrl,
                      enabled: !_busy,
                      obscureText: true,
                      onSubmitted: (_) => _loginWithPassword(),
                      decoration: const InputDecoration(
                        labelText: 'Mật khẩu',
                        prefixIcon: Icon(Icons.lock_outline),
                        border: OutlineInputBorder(),
                        isDense: true,
                      ),
                    ),
                    const SizedBox(height: 16),
                    FilledButton.icon(
                      key: const Key('login-submit-btn'),
                      onPressed: _busy ? null : () => _loginWithPassword(),
                      icon: _busy
                          ? const SizedBox.square(
                              dimension: 18,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Icon(Icons.login),
                      label: Text(_busy ? 'Đang đăng nhập…' : 'Đăng nhập'),
                    ),
                    const SizedBox(height: 16),
                    Text(
                      'Chọn nhanh vai trò Demo (Maker / Checker / Auditor):',
                      style: theme.textTheme.labelMedium,
                    ),
                    const SizedBox(height: 8),
                    Wrap(
                      spacing: 8,
                      runSpacing: 8,
                      children: [
                        ActionChip(
                          key: const Key('login-demo-maker-chip'),
                          avatar: const Icon(Icons.edit_note, size: 18),
                          label: const Text('Maker (demo_maker)'),
                          onPressed: _busy
                              ? null
                              : () => _loginWithPassword('demo_maker'),
                        ),
                        ActionChip(
                          key: const Key('login-demo-checker-chip'),
                          avatar: const Icon(Icons.fact_check_outlined, size: 18),
                          label: const Text('Checker (demo_checker)'),
                          onPressed: _busy
                              ? null
                              : () => _loginWithPassword('demo_checker'),
                        ),
                        ActionChip(
                          key: const Key('login-demo-auditor-chip'),
                          avatar: const Icon(Icons.visibility_outlined, size: 18),
                          label: const Text('Auditor (demo_auditor)'),
                          onPressed: _busy
                              ? null
                              : () => _loginWithPassword('demo_auditor'),
                        ),
                      ],
                    ),
                    const Padding(
                      padding: EdgeInsets.symmetric(vertical: 16),
                      child: Divider(),
                    ),
                    OutlinedButton.icon(
                      key: const Key('login-google-btn'),
                      onPressed: _busy ? null : _loginWithGoogle,
                      icon: const Icon(Icons.account_circle_outlined),
                      label: const Text('Tiếp tục với Google'),
                    ),
                    const SizedBox(height: 8),
                    Text(
                      context.watch<AuthService>().isGoogleSignInConfigured
                          ? 'Lần đầu đăng nhập Google sẽ tự tạo tài khoản demo.'
                          : 'Bản Web Demo chưa cấu hình GOOGLE_CLIENT_ID — vui lòng chọn nhanh tài khoản Demo ở trên.',
                      style: theme.textTheme.bodySmall,
                      textAlign: TextAlign.center,
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}
