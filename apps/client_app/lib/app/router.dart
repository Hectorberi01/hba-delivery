import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/auth/auth_repository.dart';
import '../features/auth/code_screen.dart';
import '../features/auth/phone_screen.dart';
import '../features/auth/session_controller.dart';
import '../features/aide/aide_screen.dart';
import '../features/deliveries/courses_screen.dart';
import '../features/deliveries/home_screen.dart';
import '../features/deliveries/new_delivery_screen.dart';
import '../features/deliveries/tracking_screen.dart';
import '../features/profil/profil_screen.dart';
import 'coque.dart';

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
      // LES QUATRE ONGLETS, CHACUN AVEC SON PROPRE NAVIGATOR.
      //
      // « /nouvelle » ET « /suivi » VIVENT SOUS L'ACCUEIL, pas a cote. Poses a
      // la racine, ils masqueraient la navigation : le client qui suit une
      // course perdrait l'arc et ne pourrait plus aller nulle part sans
      // revenir en arriere. Sous la branche, l'arc reste visible et l'onglet
      // « Accueil » reste allume — ce qui est vrai, puisque c'est de la qu'on
      // vient.
      StatefulShellRoute.indexedStack(
        builder: (_, __, shell) => Coque(shell: shell),
        branches: [
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/',
                builder: (_, __) => const HomeScreen(),
                routes: [
                  GoRoute(
                    path: 'nouvelle',
                    builder: (_, __) => const NewDeliveryScreen(),
                  ),
                  GoRoute(
                    path: 'suivi/:id',
                    builder: (context, state) =>
                        TrackingScreen(deliveryId: state.pathParameters['id']!),
                  ),
                ],
              ),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(path: '/courses', builder: (_, __) => const CoursesScreen()),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(path: '/profil', builder: (_, __) => const ProfilScreen()),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(path: '/aide', builder: (_, __) => const AideScreen()),
            ],
          ),
        ],
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
