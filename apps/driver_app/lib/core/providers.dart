import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';

import '../features/auth/session_controller.dart';
import 'coffre.dart';
import 'file_actions.dart';
import 'plantages.dart';

/// Base d'API. Surchargee au lancement :
/// flutter run --dart-define=HBA_API_BASE=https://driver.hba.bj/api/driver/v1
const apiBaseUrl = String.fromEnvironment(
  'HBA_API_BASE',
  defaultValue: 'http://localhost:5100/api/driver/v1',
);

final secureStorageProvider = Provider<FlutterSecureStorage>(
  (ref) => const FlutterSecureStorage(
    aOptions: AndroidOptions(encryptedSharedPreferences: true),
  ),
);

final tokenStoreProvider = Provider<TokenStore>(
  (ref) => TokenStore(
    ref.watch(secureStorageProvider),
    prefix: 'hba.driver',

    // LE COFFRE NE LEVE PLUS, DONC PERSONNE NE VERRAIT SES PANNES. Un echec
    // du keystore ne bloque plus l'application — il lui coute seulement une
    // reconnexion —, mais il reste la trace d'un telephone sur lequel la
    // session ne tient pas. Sans ce branchement, il disparaitrait sans bruit.
    onErreur: (erreur, pile, operation) =>
        Plantages.noter(erreur, pile, contexte: 'coffre : $operation'),
  ),
);

final dioProvider = Provider<Dio>(
  (ref) => buildDio(baseUrl: apiBaseUrl, tokens: ref.watch(tokenStoreProvider)),
);

final fileDActionsProvider = Provider<FileDActions>(
  (ref) => FileDActions(CoffreSecurise(ref.watch(secureStorageProvider))),
);

/// COMBIEN DE FOIS UN COMPTE A QUITTE CE TELEPHONE.
///
/// IL NE REND PAS LA SESSION ELLE-MEME : « SessionSignedIn » est reconstruit
/// quand le statut du dossier change, et surveiller l'etat entier ferait
/// repartir toutes les requetes de l'application a chaque validation de piece.
/// Ce qui doit tout effacer, c'est un CHANGEMENT DE COMPTE — et il passe
/// forcement par « deconnecte ».
///
/// UN COMPTEUR, ET SURTOUT PAS UN BOOLEEN. Ce fournisseur en a ete un, comme
/// son jumeau de l'application cliente, ou un test l'a pris sur le fait :
/// Riverpod ne reconstruit un dependant que si la valeur recalculee DIFFERE de
/// la precedente. Le compte A part, le compte B arrive, le booleen repasse de
/// vrai a vrai — et comme personne ne l'a lu entre les deux, Riverpod conclut
/// que rien n'a change et rend a B le depot de A. Un compteur ne revient jamais
/// sur ses pas.
///
/// ICI C'ETAIT UNE PREVENTION QUI NE PREVENAIT RIEN, ce qui est pire qu'une
/// absence de filet : tous les fournisseurs de donnees de cette application
/// sont autoDispose et le routeur les jette a la deconnexion, donc le defaut ne
/// se voyait pas — mais le premier fournisseur NON autoDispose ajoute demain
/// aurait fuite en silence, sous un commentaire affirmant le contraire.
final generationDuCompteProvider = Provider<int>((ref) {
  // ON SURVEILLE L'ETAT POUR ETRE RECALCULE, ON REND LE COMPTEUR. Surveiller le
  // compteur directement n'est pas possible : il vit dans le controleur, et
  // c'est la bascule d'etat qui signale qu'il a pu changer.
  ref.watch(sessionProvider);
  return ref.read(sessionProvider.notifier).generation;
});

/// Le client d'API, et la racine de tout ce qui porte des donnees du compte.
///
/// LE « watch » CI-DESSOUS EST UNE PREVENTION, PAS UNE REPARATION, et la nuance
/// merite d'etre ecrite. Le constat S6 de l'audit du 30 septembre 2026 disait
/// que cette application avait le meme defaut que celle du client : verification
/// faite, NON. Tous ses fournisseurs de donnees — releve, demandes, profil,
/// courses, dossier — sont autoDispose, le routeur renvoie vers la connexion des
/// que la session tombe, et la file d'actions est purgee aux deux sorties. Rien
/// ne fuyait.
///
/// MAIS CETTE SURETE TIENT A UNE HABITUDE. Elle demande que chaque fournisseur
/// ajoute pense a « autoDispose » ; le premier qui l'oubliera fera reapparaitre
/// le defaut, sans que rien ne le signale. Ici la regle tient a une dependance :
/// tout ce qui lit des donnees du compte passe par un depot, tout depot passe
/// par CE client, donc tout est reconstruit quand le compte change. Une ligne,
/// et plus personne n'a a y penser.
///
/// CELA NE COUTE RIEN AU CHEMIN NORMAL : le compteur n'avance qu'a la
/// deconnexion. Dio n'est pas reconstruit — la socket est gardee.
final apiClientProvider = Provider<ApiClient>((ref) {
  ref.watch(generationDuCompteProvider);

  return ApiClient(
    dio: ref.watch(dioProvider),
    tokens: ref.watch(tokenStoreProvider),
    onSessionLost: () async {
      // LA SESSION PERDUE EST UNE DECONNEXION, ET ELLE DOIT EN FAIRE AUTANT.
      // C'est l'autre porte de sortie de l'application — un jeton de
      // rafraichissement revoque, une session close ailleurs —, et elle ne
      // passe pas par SessionController.signOut. Sans cette purge, le code de
      // remise du livreur ejecte resterait sur le telephone et serait rejoue
      // par le suivant.
      try {
        await ref.read(fileDActionsProvider).purger();
      } on Object {
        // Le filtre de FileDActions.vider rattrape ce cas.
      }

      await ref.read(tokenStoreProvider).clear();
      ref.read(sessionProvider.notifier).signedOut();
    },
  );
});
