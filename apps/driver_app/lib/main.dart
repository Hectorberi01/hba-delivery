import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_ui/hba_ui.dart';

import 'app/router.dart';
import 'features/auth/session_controller.dart';

void main() {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const ProviderScope(child: HbaDriverApp()));
}

class HbaDriverApp extends ConsumerStatefulWidget {
  const HbaDriverApp({super.key});

  @override
  ConsumerState<HbaDriverApp> createState() => _HbaDriverAppState();
}

class _HbaDriverAppState extends ConsumerState<HbaDriverApp> {
  @override
  void initState() {
    super.initState();
    // Relit le trousseau avant d'afficher quoi que ce soit.
    Future.microtask(() => ref.read(sessionProvider.notifier).restore());
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp.router(
      title: 'HBA Livreur',
      debugShowCheckedModeBanner: false,
      theme: HbaTheme.light(),
      routerConfig: ref.watch(routerProvider),
    );
  }
}
