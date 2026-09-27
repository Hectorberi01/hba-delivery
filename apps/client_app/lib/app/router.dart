import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/auth/auth_repository.dart';
import '../features/auth/code_screen.dart';
import '../features/auth/phone_screen.dart';
import '../features/auth/session_controller.dart';
import '../features/deliveries/home_screen.dart';
import '../features/deliveries/new_delivery_screen.dart';
import '../features/deliveries/tracking_screen.dart';

final routerProvider = Provider<GoRouter>((ref) {
  final refresh = ValueNotifier<int>(0);
  ref.onDispose(refresh.dispose);
  ref.listen(sessionProvider, (_, __) => refresh.value++);

  return GoRouter(
    initialLocation: '/',
    refreshListenable: refresh,
    redirect: (context, state) {
      final session = ref.read(sessionProvider);
      final location = state.matchedLocation;

      if (session is SessionUnknown) {
        return location == '/demarrage' ? null : '/demarrage';
      }

      final signedIn = session is SessionSignedIn;
      final onAuthRoute = location == '/connexion' || location == '/code';

      if (!signedIn) return onAuthRoute ? null : '/connexion';
      if (onAuthRoute || location == '/demarrage') return '/';

      return null;
    },
    routes: [
      GoRoute(path: '/demarrage', builder: (_, __) => const _Splash()),
      GoRoute(path: '/connexion', builder: (_, __) => const PhoneScreen()),
      GoRoute(
        path: '/code',
        builder: (context, state) {
          final args = state.extra! as (OtpChallenge, String);
          return CodeScreen(challenge: args.$1, phone: args.$2);
        },
      ),
      GoRoute(path: '/', builder: (_, __) => const HomeScreen()),
      GoRoute(path: '/nouvelle', builder: (_, __) => const NewDeliveryScreen()),
      GoRoute(
        path: '/suivi/:id',
        builder: (context, state) =>
            TrackingScreen(deliveryId: state.pathParameters['id']!),
      ),
    ],
  );
});

class _Splash extends StatelessWidget {
  const _Splash();

  @override
  Widget build(BuildContext context) =>
      const Scaffold(body: Center(child: CircularProgressIndicator()));
}
