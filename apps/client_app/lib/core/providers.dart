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

final apiClientProvider = Provider<ApiClient>((ref) {
  return ApiClient(
    dio: ref.watch(dioProvider),
    tokens: ref.watch(tokenStoreProvider),
    onSessionLost: () async {
      await ref.read(tokenStoreProvider).clear();
      ref.read(sessionProvider.notifier).signedOut();
    },
  );
});
