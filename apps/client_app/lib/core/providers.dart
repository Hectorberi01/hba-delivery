import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';

import '../features/auth/session_controller.dart';

/// Base d'API. Surchargee au lancement :
/// flutter run --dart-define=HBA_API_BASE=https://api.hba.delivery/api/client/v1
///
/// localhost designe le telephone, pas le Mac : sur un appareil physique il
/// faut l'adresse de la machine sur le reseau local.
const apiBaseUrl = String.fromEnvironment(
  'HBA_API_BASE',
  defaultValue: 'http://localhost:5100/api/client/v1',
);

final secureStorageProvider = Provider<FlutterSecureStorage>(
  (ref) => const FlutterSecureStorage(
    aOptions: AndroidOptions(encryptedSharedPreferences: true),
  ),
);

final tokenStoreProvider = Provider<TokenStore>(
  (ref) => TokenStore(ref.watch(secureStorageProvider), prefix: 'hba.client'),
);

final dioProvider = Provider<Dio>(
  (ref) => buildDio(baseUrl: apiBaseUrl, tokens: ref.watch(tokenStoreProvider)),
);

/// COMBIEN DE FOIS UN COMPTE A QUITTE CE TELEPHONE.
///
/// UN COMPTEUR, ET SURTOUT PAS UN BOOLEEN — la premiere version de cette
/// correction en etait un (« y a-t-il quelqu'un de connecte »), et ELLE NE
/// MARCHAIT PAS. Le test « et avec lui TOUS les depots qui en dependent » l'a
/// prise sur le fait.
///
/// POURQUOI UN BOOLEEN NE PEUT PAS MARCHER ICI. Riverpod ne reconstruit un
/// dependant que si la valeur recalculee DIFFERE de la precedente, au sens de
/// « == ». Le compte A part, le compte B arrive : le booleen passe de vrai a
/// faux puis de nouveau a vrai. Personne ne l'a lu entre les deux — l'ecran de
/// connexion ne demande ni courses ni adresses —, donc au premier acces de B,
/// Riverpod compare vrai a vrai, conclut que rien n'a change, et rend a B LE
/// DEPOT DE A, son cache avec. Le passage par « faux » n'a jamais existe pour
/// personne.
///
/// ET C'EST EXACTEMENT LA SEQUENCE REELLE, pas un cas de laboratoire : se
/// deconnecter renvoie a l'ecran de connexion, qui ne lit aucune donnee de
/// compte. Le defaut ne pouvait donc PAS se manifester autrement que dans le
/// dos de tout le monde.
///
/// UN COMPTEUR NE REVIENT JAMAIS SUR SES PAS. C'est sa seule propriete utile :
/// deux etats separes par une deconnexion ne portent jamais le meme nombre, et
/// aucune comparaison ne peut les confondre.
///
/// IL N'AVANCE QU'A LA DECONNEXION, et cela suffit parce que « signedOut » est
/// la SEULE porte de sortie : on ne peut pas arriver sur le compte B sans etre
/// passe par la. C'est aussi ce qui garde le chemin normal gratuit — corriger
/// son prenom ne fait pas repartir les requetes de l'application, puisque le
/// nombre, lui, ne bouge pas.
final generationDuCompteProvider = Provider<int>((ref) {
  // ON SURVEILLE L'ETAT POUR ETRE RECALCULE, ON REND LE COMPTEUR. Surveiller le
  // compteur directement n'est pas possible : il vit dans le controleur, et
  // c'est la bascule d'etat qui signale qu'il a pu changer.
  ref.watch(sessionProvider);
  return ref.read(sessionProvider.notifier).generation;
});

/// Le client d'API, et la racine de tout ce qui porte des donnees du compte.
///
/// IL SURVEILLE « generationDuCompteProvider », ET C'EST LA CORRECTION DU CONSTAT
/// S6 (audit du 30 septembre 2026).
///
/// CE QUI N'ALLAIT PAS. Aucun fournisseur de donnees n'etait autoDispose, et ni
/// la deconnexion ni la perte de session n'en invalidait un seul. Deux personnes
/// partagent un telephone, ou un vendeur en fait la demonstration : le compte A
/// se deconnecte, le compte B se connecte, et B voit le profil de A, ses
/// adresses enregistrees, ses courses, et LES NUMEROS DE TELEPHONE DES
/// DESTINATAIRES DE A. Rien ne s'affichait comme une erreur ; l'ecran etait
/// simplement celui de quelqu'un d'autre.
///
/// POURQUOI LA REGLE EST POSEE ICI ET NULLE PART AILLEURS. On aurait pu lister
/// les fournisseurs a vider a la deconnexion. Cette liste aurait ete juste le
/// jour ou on l'ecrit, et fausse au premier fournisseur ajoute ensuite — c'est
/// EXACTEMENT comme cela que le defaut est ne. Ici, la regle tient a une
/// dependance : tout ce qui lit des donnees du compte passe par un depot, tout
/// depot passe par CE client, donc tout est reconstruit quand le compte change.
/// Un fournisseur ajoute demain est couvert sans que personne y pense.
///
/// CELA NE COUTE RIEN AU CHEMIN NORMAL : le compteur n'avance qu'a la
/// deconnexion. Dio, lui, n'est pas reconstruit — la socket est gardee.
///
/// CE QUE CE MECANISME NE COUVRE PAS : l'etat qui ne vient pas du reseau, comme
/// « courseEnAttenteProvider ». Il est vide par « SessionController.signedOut »,
/// par ou passent les deux sorties.
final apiClientProvider = Provider<ApiClient>((ref) {
  ref.watch(generationDuCompteProvider);

  return ApiClient(
    dio: ref.watch(dioProvider),
    tokens: ref.watch(tokenStoreProvider),
    onSessionLost: () async {
      await ref.read(tokenStoreProvider).clear();

      // « signedOut » FAIT LE RESTE, et c'est voulu : les deux sorties de
      // l'application — celle-ci et le bouton « Se deconnecter » — doivent
      // oublier la meme chose. Deux purges paralleles finiraient par diverger.
      ref.read(sessionProvider.notifier).signedOut();
    },
  );
});
