import 'package:hba_core/hba_core.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/plantages.dart';
import '../../core/providers.dart';
import 'auth_repository.dart';

/// Etat de session du livreur.
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
  const SessionSignedIn({required this.displayName, required this.kycApproved});

  final String displayName;

  /// Faux : le livreur est inscrit mais ne peut pas encore travailler.
  final bool kycApproved;
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
  /// Combien de comptes ont quitte ce telephone depuis le lancement.
  ///
  /// CE NOMBRE EST LU PAR « generationDuCompteProvider », qui porte la raison
  /// d'etre complete d'un compteur plutot que d'un booleen : Riverpod ne
  /// reconstruit un dependant que si la valeur recalculee a CHANGE, et un
  /// booleen qui repasse par sa valeur d'origine — A part, B arrive — ne change
  /// pas.
  int _generation = 0;

  int get generation => _generation;

  @override
  SessionState build() => const SessionUnknown();

  TokenStore get _tokens => ref.read(tokenStoreProvider);

  /// Appele une fois au demarrage.
  ///
  /// CETTE METHODE NE DOIT JAMAIS LEVER, ET C'EST LA SEULE CHOSE QUI COMPTE
  /// ICI. Elle est lancee depuis un « Future.microtask » de main.dart :
  /// personne n'attrape ce qui s'en echappe. Tant qu'elle n'aboutit pas,
  /// l'etat reste « SessionUnknown » — et le routeur renvoie alors TOUT vers
  /// l'ecran de demarrage, qui n'a ni texte ni bouton. Une exception ici
  /// laissait donc l'application sur un rond qui tourne, definitivement.
  ///
  /// TokenStore n'en leve plus depuis sa reecriture ; ce « try » est la pour
  /// tout le reste — un provider qui ne se construit pas, une dependance
  /// future. L'ecran de connexion est un mauvais endroit ou se retrouver par
  /// surprise ; l'ecran de demarrage est un cul-de-sac.
  Future<void> restore() async {
    try {
      await _tokens.load();

      if (_tokens.accessToken == null) {
        state = const SessionSignedOut();
        return;
      }

      await refresh(fallbackName: '');
    } on Object catch (erreur, pile) {
      Plantages.noter(erreur, pile, contexte: 'restauration de la session');
      state = const SessionSignedOut();
    }
  }

  /// Relit le profil aupres du service Driver.
  ///
  /// SEULE SOURCE DU STATUT KYC. La reponse de verification OTP vient
  /// d'Identity, qui ne connait pas les pieces du livreur : elle ne peut pas
  /// dire si le dossier est valide. Le statut se lit ici, et nulle part
  /// ailleurs.
  ///
  /// Appelee aussi au tirer-pour-rafraichir : un dossier valide pendant que
  /// l'app est ouverte doit deverrouiller le bouton sans redemarrage.
  Future<void> refresh({String fallbackName = ''}) async {
    final previous = state;
    final knownName =
        previous is SessionSignedIn && previous.displayName.isNotEmpty
            ? previous.displayName
            : fallbackName;

    try {
      final me = await ref.read(apiClientProvider).get('/me');

      // LES RAPPORTS DE PLANTAGE SE RATTACHENT ICI, et nulle part ailleurs :
      // c'est le seul endroit qui lit /me a chaque ouverture, a chaque reprise
      // et a chaque tirer-pour-rafraichir. L'identifiant seul — ni le nom, ni
      // le telephone.
      Plantages.livreur(me['id'] as String?);

      // LA FILE APPREND A QUI ELLE APPARTIENT. Sans cette ligne, une action
      // mise en file par ce livreur pourrait etre rejouee par le suivant a se
      // connecter sur cet appareil — avec le jeton du suivant. Voir
      // FileDActions.purger.
      await ref
          .read(fileDActionsProvider)
          .noterLeProprietaire(me['id'] as String? ?? '');

      state = SessionSignedIn(
        displayName: me['displayName'] as String? ?? knownName,
        kycApproved: me['kycApproved'] as bool? ?? false,
      );
    } on Object {
      // DEUX CAS SOUS LA MEME BRANCHE, ET C'EST VOULU : le reseau qui hoquete,
      // et le profil pas encore ne — il nait d'un evenement Kafka, donc
      // quelques instants apres le compte. Dans les deux cas l'app reste
      // connectee et affiche « dossier en cours », ce qui est vrai.
      if (previous is! SessionSignedIn) {
        state = SessionSignedIn(displayName: knownName, kycApproved: false);
      }
    }
  }

  /// Apres la verification OTP. Le nom saisi sert de repli tant que le profil
  /// livreur n'existe pas encore.
  Future<void> signedIn(SignedInDriver driver) async {
    state = SessionSignedIn(displayName: driver.displayName, kycApproved: false);
    await refresh(fallbackName: driver.displayName);
  }

  void signedOut() {
    // ON DETACHE AVANT DE CHANGER D'ETAT : un plantage survenu pendant la
    // deconnexion ne doit plus porter l'identifiant de celui qui part.
    Plantages.livreur(null);

    // LE COMPTEUR AVANCE ICI, ET NULLE PART AILLEURS. Le faire avancer a la
    // connexion ou au rafraichissement du dossier rendrait le nombre sensible a
    // des changements qui ne changent PAS de compte, et chaque piece validee
    // ferait repartir toutes les requetes de l'application.
    _generation++;

    state = const SessionSignedOut();
  }

  /// Deconnexion demandee par le livreur.
  ///
  /// LA FILE PART AVANT LES JETONS, ET L'ORDRE COMPTE. Purger d'abord garantit
  /// que le code de remise du livreur qui s'en va ne reste pas sur le
  /// telephone, et qu'aucune de ses actions ne sera rejouee par le suivant. Si
  /// la purge echoue, on se deconnecte quand meme — rester connecte de force
  /// serait pire — et le filtre de FileDActions.vider rattrape le reste.
  Future<void> signOut() async {
    await _purgerLaFile();
    await ref.read(authRepositoryProvider).signOut();
    signedOut();
  }

  Future<void> _purgerLaFile() async {
    try {
      await ref.read(fileDActionsProvider).purger();
    } on Object catch (erreur, pile) {
      Plantages.noter(erreur, pile, contexte: 'purge de la file a la deconnexion');
    }
  }
}

/// Cle d'idempotence reutilisable par les ecrans.
String newActionKey() => ApiClient.newIdempotencyKey();
