import 'package:flutter/material.dart';

import '../../data/repositories/bankops_repository.dart';
import 'state_views.dart';

/// Khung hiển thị dữ liệu bất kỳ kèm Loading / Empty / Error + retry + refresh.
///
/// Dùng cho mọi màn đọc dữ liệu thay vì viết FutureBuilder + if-else ở
/// từng screen.
///
/// ```dart
/// AsyncView<_DashboardData>(
///   future: _future!,
///   isEmpty: (d) => d.locations.isEmpty && d.items.isEmpty,
///   builder: (context, data) => ...,
///   empty: const EmptyView(title: 'Chưa có dữ liệu nào'),
/// )
/// ```
class AsyncView<T> extends StatelessWidget {
  const AsyncView({
    super.key,
    required this.future,
    required this.builder,
    this.isEmpty,
    this.loading,
    this.empty,
    this.errorLabel,
    this.retryLabel = 'Thử lại',
    this.onRetry,
  });

  final Future<T> future;
  final Widget Function(BuildContext context, T data) builder;
  final bool Function(T data)? isEmpty;
  final Widget? loading;
  final Widget? empty;
  final String? errorLabel;
  final String retryLabel;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<T>(
      future: future,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return loading ?? const LoadingView();
        }
        if (snapshot.hasError) {
          return ErrorView(
            message: _formatError(snapshot.error),
            onRetry: onRetry ?? () => {},
          );
        }
        if (snapshot.hasData) {
          final Object? data = snapshot.data;
          if (data == null) return empty ?? const SizedBox.shrink();
          final value = data as T;
          final emptyCheck = isEmpty;
          if (emptyCheck != null && emptyCheck(value)) {
            return empty ?? const SizedBox.shrink();
          }
          return builder(context, value);
        }
        return empty ?? const SizedBox.shrink();
      },
    );
  }

  static String _formatError(Object? error) {
    if (error is ApiException) return error.message;
    if (error is FormatException) return error.message;
    if (error is Exception) return error.toString();
    return '$error';
  }
}
