import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
import '../deliveries/delivery_providers.dart';
import 'auth_repository.dart';

sealed class SessionState {
  const SessionState();
}

/// Au lancement, le temps de relire le trousseau.
class SessionUnknown extends SessionState {
  const SessionUnknown();
}

class SessionSignedOut extends SessionState {
  const SessionSignedOut();
}

class SessionSignedIn extends SessionState {
  const SessionSignedIn({required this.displayName});

  final String displayName;
}

final authRepositoryProvider = Provider<AuthRepository>(
  (ref) => AuthRepository(
    ref.watch(apiClientProvider),
    ref.watch(tokenStoreProvider),
  ),
);

final sessionProvider =
    NotifierProvider<SessionController, SessionState>(SessionController.new);

class SessionController extends Notifier<SessionState> {
  @override
  SessionState build() => const SessionUnknown();

  /// Combien de comptes ont quitte ce telephone depuis le lancement.
  ///
  /// CE NOMBRE EST LU PAR « generationDuCompteProvider », qui porte la raison
  /// d'etre complete d'un compteur plutot que d'un booleen. En deux mots :
  /// Riverpod ne reconstruit un dependant que si la valeur recalculee a CHANGE,
  /// et un booleen qui repasse par sa valeur d'origine — A part, B arrive — ne
  /// change pas. Un compteur ne revient jamais sur ses pas.
  int _generation = 0;

  int get generation => _generation;

  Future<void> restore() async {
    final tokens = ref.read(tokenStoreProvider);
    await tokens.load();

    if (tokens.accessToken == null) {
      state = const SessionSignedOut();
      return;
    }

    try {
      final me = await ref.read(apiClientProvider).get('/me');
      state = SessionSignedIn(displayName: me['displayName'] as String? ?? '');
    } on Object {
      // Hors connexion au lancement : la session est gardee. Un jeton
      // reellement invalide sera rejete au premier appel, et ApiClient
      // declenchera signedOut.
      state = const SessionSignedIn(displayName: '');
    }
  }

  void signedIn(SignedInCustomer customer) =>
      state = SessionSignedIn(displayName: customer.displayName);

  /// DEUX COPIES DU MEME FAIT, ET C'EST DEJA CE QUI NOUS A COUTE UNE DEMI-
  /// JOURNEE AILLEURS.
  ///
  /// La session lit « /me » au demarrage et garde le nom ; l'ecran de profil
  /// lit « /me » de son cote. Quand le client change son nom, seule la copie du
  /// profil est rafraichie, et la session garde l'ancien — indefiniment, sans
  /// rien qui le signale.
  ///
  /// C'est EXACTEMENT le defaut corrige dans l'application livreur, ou l'etat
  /// du dossier existait en deux exemplaires : le profil disait « valide » et
  /// l'accueil « en cours », chacun ayant raison de son point de vue. Personne
  /// n'affiche aujourd'hui le nom porte par la session — mais le jour ou
  /// quelqu'un le fera, il sera faux, et la cause sera loin de l'ecran fautif.
  void renommer(String nom) {
    if (state is! SessionSignedIn) return;
    state = SessionSignedIn(displayName: nom);
  }

  /// LA SEULE PORTE DE SORTIE, et les deux chemins y passent : le bouton
  /// « Se deconnecter » et la perte de session decidee par ApiClient. C'est ce
  /// qui permet de n'ecrire l'oubli qu'une fois.
  ///
  /// CE QUI EST VIDE ICI EST CE QUE LE RESEAU NE RECONSTRUIT PAS. Tout ce qui
  /// vient du compte passe par un depot, donc par « apiClientProvider », qui
  /// surveille « generationDuCompteProvider » : le compteur avance ci-dessous et
  /// reconstruit a lui seul le profil, les adresses, les courses et le reste.
  /// « courseEnAttenteProvider », lui, ne vient d'aucune requete — il porte
  /// l'identifiant de la course que le client vient de payer, le temps que le
  /// webhook arrive. Laisse en place, il envoie l'accueil du compte SUIVANT
  /// chercher une course qui n'est pas la sienne : la fiche repond 403 ou 404,
  /// rien ne le rattrape, et l'accueil de B tourne indefiniment.
  ///
  /// L'ORDRE COMPTE. On vide AVANT de changer l'etat : la bascule declenche la
  /// redirection du routeur, et un ecran reconstruit entre les deux lirait
  /// encore l'identifiant de l'ancien compte.
  void signedOut() {
    ref.invalidate(courseEnAttenteProvider);

    // LE COMPTEUR AVANCE ICI, ET NULLE PART AILLEURS. C'est la seule porte de
    // sortie : on n'arrive pas sur un autre compte sans y etre passe. Le faire
    // avancer aussi a la connexion ou au renommage rendrait le nombre sensible
    // a des changements qui ne changent PAS de compte, et chaque correction de
    // prenom ferait repartir toutes les requetes de l'application.
    _generation++;

    state = const SessionSignedOut();
  }

  Future<void> signOut() async {
    await ref.read(authRepositoryProvider).signOut();
    signedOut();
  }
}
