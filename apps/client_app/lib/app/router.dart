import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../features/auth/auth_repository.dart';
import '../features/auth/bienvenue_screen.dart';
import '../features/auth/code_screen.dart';
import '../features/auth/phone_screen.dart';
import '../features/auth/session_controller.dart';
import '../features/commande/feuille_modale.dart';
import '../features/aide/aide_screen.dart';
import '../features/avis/avis_models.dart';
import '../features/avis/notation_screen.dart';
import '../features/deliveries/courses_screen.dart';
import '../features/deliveries/detail_livraison.dart';
import '../features/deliveries/home_screen.dart';
import '../features/deliveries/new_delivery_screen.dart';
import '../features/deliveries/point_field.dart';
import '../features/deliveries/tracking_screen.dart';
import '../features/notifications/notifications_screen.dart';
import '../features/profil/profil_screen.dart';
import '../features/wallet/wallet_screen.dart';
import '../features/rides/nouvelle_course_screen.dart';
import '../features/rides/suivi_course_screen.dart';

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

      // TROIS ADRESSES AVANT LA CONNEXION, PAS DEUX. « /bienvenue » a rejoint
      // la liste le 4 octobre 2026 : c'est la premiere chose que voit un
      // inconnu, et l'oublier ici le renverrait en boucle sur le champ de
      // telephone qu'il n'a pas encore demande a remplir.
      final onAuthRoute = location == '/bienvenue' ||
          location == '/connexion' ||
          location == '/code';

      // L'ACCUEIL DE L'INCONNU EST « /bienvenue », PAS « /connexion ». Les deux
      // boutons de cet ecran menent au second ; c'est lui qui decide d'y aller.
      if (!signedIn) return onAuthRoute ? null : '/bienvenue';
      if (onAuthRoute || location == '/demarrage') return '/';

      return null;
    },
    routes: [
      GoRoute(path: '/demarrage', builder: (_, __) => const _Splash()),
      GoRoute(path: '/bienvenue', builder: (_, __) => const BienvenueScreen()),
      GoRoute(path: '/connexion', builder: (_, __) => const PhoneScreen()),
      GoRoute(
        path: '/code',
        builder: (context, state) {
          final args = state.extra! as (OtpChallenge, String);
          return CodeScreen(challenge: args.$1, phone: args.$2);
        },
      ),
      // UN SEUL ARBRE D'ECRANS, EMPILES LES UNS SUR LES AUTRES.
      //
      // PAS DE BARRE D'ONGLETS, ET CELLE DU 4 OCTOBRE 2026 A VECU UNE HEURE.
      // Elle avait ete posee d'apres les maquettes ; essayee sur un telephone,
      // elle prend une bande permanente au bas d'un ecran dont la carte EST le
      // contenu, pour trois destinations qu'on ouvre quelques fois par semaine.
      // La decision du 2 octobre tenait, et elle est reprise : l'accueil est une
      // carte plein ecran, et tout le reste s'empile dessus.
      //
      // LE MENU DU HAUT OUVRE LE PROFIL, ET LE PROFIL EST LE HUB. Pas de tiroir
      // lateral : un tiroir est une seconde liste de destinations qui double
      // celle du profil, et deux listes finissent toujours par differer. Le
      // client y trouve ses informations, ses commandes et son wallet au meme
      // endroit.
      //
      // CE QUE LA PILE CHANGE, ET CE QU'ELLE NE CHANGE PAS. Chaque ecran recoit
      // une fleche de retour qui ramene vraiment la ou l'on etait. L'accueil,
      // lui, reste VIVANT dessous : il ne redemande ni sa carte ni la position
      // du telephone au retour.
      GoRoute(path: '/', builder: (_, __) => const HomeScreen()),
      GoRoute(path: '/courses', builder: (_, __) => const CoursesScreen()),
      GoRoute(path: '/profil', builder: (_, __) => const ProfilScreen()),
      GoRoute(path: '/wallet', builder: (_, __) => const WalletScreen()),

      // LES DEUX ECRANS DE COMMANDE SE POSENT EN FEUILLE SUR LA CARTE, et non
      // en page pleine. « pageBuilder » et non « builder » : c'est la seule
      // facon de choisir la route qu'une entree du routeur fabrique. Voir
      // [PageEnFeuille] pour ce que la transparence change.
      // LE SUIVI EN FEUILLE, OUVERT PAR LE BANDEAU DE L'ACCUEIL.
      //
      // Depuis le 4 octobre 2026 : appuyer sur « votre livreur arrive » ne doit
      // pas remplacer la carte par une page. La feuille monte dessus, la carte
      // continue de vivre derriere — meme controleur, memes livreurs — et la
      // refermer ramene exactement ou l'on etait, sans reconstruire l'accueil.
      //
      // « /suivi/:id » EXISTE TOUJOURS, et ce n'est pas un doublon : il porte
      // l'attente plein cadre, qui a sa propre carte, et il reste la cible des
      // notifications et de la liste des courses. Les deux montrent le meme
      // bloc — voir DetailDeLaLivraison.
      GoRoute(
        path: '/livraison/:id',
        pageBuilder: (_, state) => PageEnFeuille<void>(
          key: state.pageKey,
          enfant: FeuilleDuSuivi(deliveryId: state.pathParameters['id']!),
        ),
      ),
      GoRoute(
        path: '/nouvelle',
        pageBuilder: (_, state) => PageEnFeuille<void>(
          key: state.pageKey,
          enfant: const NewDeliveryScreen(),
        ),
      ),
      GoRoute(
        path: '/nouvelle-course',

        // LE RACCOURCI D'ACCUEIL PASSE LA DESTINATION PAR « extra ». Elle
        // n'entre pas dans le chemin : deux coordonnees dans une URL seraient
        // copiees, partagees et rejouees, alors qu'un lieu enregistre
        // n'appartient qu'a ce passager. Le transtypage prudent rend simplement
        // null quand on ouvre l'ecran sans rien passer.
        pageBuilder: (context, state) => PageEnFeuille<void>(
          key: state.pageKey,
          enfant: NouvelleCourseScreen(
            destination:
                state.extra is PickedPoint ? state.extra as PickedPoint : null,
          ),
        ),
      ),
      GoRoute(
        path: '/suivi/:id',
        builder: (context, state) =>
            TrackingScreen(deliveryId: state.pathParameters['id']!),
      ),
      GoRoute(
        path: '/course/:id',
        builder: (context, state) =>
            SuiviCourseScreen(courseId: state.pathParameters['id']!),
      ),

      // LA NOTATION PASSE PAR « extra », ET NON PAR LE CHEMIN. Mettre
      // l'identifiant d'une mission dans une adresse en ferait un lien copiable
      // et rejouable ; la mission notee n'appartient qu'a ce passager, et Rating
      // le verifie — mais une adresse qui circule invite a essayer.
      //
      // SANS RIEN A NOTER, ON REND L'ACCUEIL. C'est le cas d'une adresse ouverte
      // a la main ou rejouee par le systeme au redemarrage.
      GoRoute(
        path: '/avis',
        redirect: (_, state) => state.extra is AvisEnAttente ? null : '/',
        builder: (context, state) =>
            NotationScreen(avis: state.extra! as AvisEnAttente),
      ),
      GoRoute(path: '/aide', builder: (_, __) => const AideScreen()),

      // LA CLOCHE DE L'ACCUEIL MENE ICI. Elle existe parce qu'un bouton qui
      // n'ouvre rien se presse deux fois puis ne se presse plus jamais.
      GoRoute(
        path: '/notifications',
        builder: (_, __) => const NotificationsScreen(),
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
