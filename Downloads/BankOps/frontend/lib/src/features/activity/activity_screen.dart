import 'package:flutter/material.dart';

import '../../app/app_shell.dart';
import '../../core/widgets/state_views.dart';
import '../../data/models/dtos.dart';
import 'activity_center.dart';

/// Màn hoạt động — activity feed.
class ActivityScreen extends StatefulWidget {
  const ActivityScreen({super.key});

  @override
  State<ActivityScreen> createState() => _ActivityScreenState();
}

class _ActivityScreenState extends State<ActivityScreen> {
  Future<List<ActivityEvent>>? _future;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final repo = RepositoryScope.of(context);
    _future ??= repo.getActivity();
  }

  void _reload() {
    final repo = RepositoryScope.of(context);
    setState(() {
      _future = repo.getActivity();
    });
  }

  @override
  Widget build(BuildContext context) {
    return FutureBuilder<List<ActivityEvent>>(
      future: _future,
      builder: (context, snapshot) {
        if (snapshot.connectionState != ConnectionState.done) {
          return const Center(child: LoadingView());
        }
        if (snapshot.hasError) {
          return Center(
            child: ErrorView(
              message: '${snapshot.error}',
              onRetry: _reload,
            ),
          );
        }
        return ActivityCenter(
          events: snapshot.data!,
          onRefresh: _reload,
        );
      },
    );
  }
}
