import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_ui/hba_ui.dart';

import 'app/router.dart';
import 'features/auth/session_controller.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const ProviderScope(child: HbaClientApp()));
}

class HbaClientApp extends ConsumerStatefulWidget {
  const HbaClientApp({super.key});

  @override
  ConsumerState<HbaClientApp> createState() => _HbaClientAppState();
}

class _HbaClientAppState extends ConsumerState<HbaClientApp> {
  @override
  void initState() {
    super.initState();
    Future.microtask(() => ref.read(sessionProvider.notifier).restore());
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp.router(
      title: 'HBA Delivery',
      debugShowCheckedModeBanner: false,
      theme: HbaTheme.light(),
      routerConfig: ref.watch(routerProvider),
    );
  }
}
