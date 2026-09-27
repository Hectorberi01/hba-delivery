import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/auth/auth_repository.dart';
import '../features/auth/code_screen.dart';
import '../features/auth/phone_screen.dart';
import '../features/auth/session_controller.dart';
import '../features/dossier/dossier_screen.dart';
import '../features/gains/gains_screen.dart';
import '../features/home/home_screen.dart';
import '../features/missions/courses_screen.dart';
import '../features/profil/profil_screen.dart';
import 'coque.dart';

final routerProvider = Provider<GoRouter>((ref) {
  // GoRouter se reevalue quand la session change. Un ValueNotifier suffit :
  // seule la transition connecte / deconnecte l'interesse.
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

      if (!signedIn) {
        return onAuthRoute ? null : '/connexion';
      }

      if (onAuthRoute || location == '/demarrage') {
        return '/';
      }

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
      StatefulShellRoute.indexedStack(
        builder: (_, __, shell) => Coque(shell: shell),
        branches: [
          StatefulShellBranch(
            routes: [GoRoute(path: '/', builder: (_, __) => const HomeScreen())],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(path: '/courses', builder: (_, __) => const CoursesScreen()),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(path: '/gains', builder: (_, __) => const GainsScreen()),
            ],
          ),
          StatefulShellBranch(
            routes: [
              GoRoute(
                path: '/profil',
                builder: (_, __) => const ProfilScreen(),
                routes: [
                  // ENFANT DE PROFIL, PAS ONGLET A PART. Le dossier se
                  // constitue une fois ; lui donner un cinquieme onglet
                  // permanent encombrerait la barre pour toujours au
                  // benefice d'une semaine.
                  GoRoute(
                    path: 'dossier',
                    builder: (_, __) => const DossierScreen(),
                  ),
                ],
              ),
            ],
          ),
        ],
      ),
    ],
  );
});

/// L'ecran de demarrage, le temps de relire le trousseau.
///
/// IL PARLE AU BOUT DE HUIT SECONDES, ET C'EST UNE ASSURANCE, PAS UN
/// CORRECTIF. Le blocage connu — une exception du coffre qui laissait l'etat a
/// « SessionUnknown » — est corrige a la source, dans TokenStore et dans
/// SessionController.restore. Reste qu'un ecran sans texte et sans bouton est
/// un cul-de-sac par construction : le jour ou quelque chose d'autre retiendra
/// la restauration, le livreur aura une phrase et une porte au lieu d'un rond
/// qui tourne.
///
/// HUIT SECONDES, ET PAS DEUX. La lecture du coffre est quasi instantanee,
/// mais « restore » enchaine sur un appel a « /me » dont le delai d'attente
/// est de quinze secondes. Parler trop tot ferait clignoter un message
/// d'alerte a chaque demarrage un peu lent — sur un reseau de Cotonou,
/// c'est-a-dire souvent.
class _Splash extends ConsumerStatefulWidget {
  const _Splash();

  @override
  ConsumerState<_Splash> createState() => _SplashState();
}

class _SplashState extends ConsumerState<_Splash> {
  static const _patience = Duration(seconds: 8);

  Timer? _minuterie;
  bool _tropLong = false;

  @override
  void initState() {
    super.initState();
    _minuterie = Timer(_patience, () {
      if (mounted) setState(() => _tropLong = true);
    });
  }

  @override
  void dispose() {
    _minuterie?.cancel();
    super.dispose();
  }

  Future<void> _reessayer() async {
    setState(() => _tropLong = false);

    _minuterie?.cancel();
    _minuterie = Timer(_patience, () {
      if (mounted) setState(() => _tropLong = true);
    });

    await ref.read(sessionProvider.notifier).restore();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        body: Center(
          child: Padding(
            padding: const EdgeInsets.all(32),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const CircularProgressIndicator(),
                if (_tropLong) ...[
                  const SizedBox(height: 24),
                  Text(
                    "Le démarrage est plus long que d'habitude. "
                    'Vérifiez votre connexion.',
                    textAlign: TextAlign.center,
                    style: Theme.of(context).textTheme.bodyMedium,
                  ),
                  const SizedBox(height: 16),
                  TextButton(
                    onPressed: _reessayer,
                    child: const Text('Réessayer'),
                  ),
                ],
              ],
            ),
          ),
        ),
      );
}
