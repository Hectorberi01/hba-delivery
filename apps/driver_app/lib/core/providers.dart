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

final apiClientProvider = Provider<ApiClient>((ref) {
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
