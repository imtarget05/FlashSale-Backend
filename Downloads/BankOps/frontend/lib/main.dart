import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import 'src/app/app_shell.dart';
import 'src/core/theme/app_theme.dart';
import 'src/app/theme_mode_controller.dart';
import 'src/features/auth/auth_service.dart';
import 'src/features/auth/login_screen.dart';

void main() {
  runApp(
    MultiProvider(
      providers: [
        ChangeNotifierProvider(
          create: (_) => AuthService()..tryAutoLogin(),
        ),
        ChangeNotifierProvider(
          create: (_) => ThemeModeController(),
        ),
      ],
      child: const BankOpsApp(),
    ),
  );
}

/// Cầu nối session: đọc AuthService, expose user cho AppShell qua SessionScope.
class SessionBridge extends StatelessWidget {
  const SessionBridge({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Consumer<AuthService>(
      builder: (context, auth, _) => SessionScope(
        user: auth.user,
        onSignOut: auth.signOut,
        child: child,
      ),
    );
  }
}

/// Chưa đăng nhập -> LoginScreen (Google, lần đầu tự signup);
/// đã có token hợp lệ -> AppShell.
class _AuthGate extends StatelessWidget {
  const _AuthGate();

  @override
  Widget build(BuildContext context) {
    final auth = context.watch<AuthService>();
    if (auth.isLoading) {
      return const Scaffold(body: Center(child: CircularProgressIndicator()));
    }
    if (auth.user == null) return const LoginScreen();
    return const AppShell();
  }
}

class BankOpsApp extends StatelessWidget {
  const BankOpsApp({super.key});

  @override
  Widget build(BuildContext context) {
    final themeMode = context.watch<ThemeModeController>().mode;
    return MaterialApp(
      title: 'BankOps',
      debugShowCheckedModeBanner: false,
      theme: AppTheme.light(),
      darkTheme: AppTheme.dark(),
      themeMode: themeMode,
      home: const SessionBridge(child: _AuthGate()),
    );
  }
}
