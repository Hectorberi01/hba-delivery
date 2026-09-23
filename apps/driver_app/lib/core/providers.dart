import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../features/auth/session_controller.dart';
import 'api_client.dart';
import 'token_store.dart';

/// Base d'API. Surchargee au lancement :
/// flutter run --dart-define=HBA_API_BASE=https://driver.hba.bj/api/driver/v1
const apiBaseUrl = String.fromEnvironment(
  'HBA_API_BASE',
  defaultValue: 'http://localhost:5102/api/driver/v1',
);

final secureStorageProvider = Provider<FlutterSecureStorage>(
  (ref) => const FlutterSecureStorage(
    aOptions: AndroidOptions(encryptedSharedPreferences: true),
  ),
);

final tokenStoreProvider = Provider<TokenStore>(
  (ref) => TokenStore(ref.watch(secureStorageProvider)),
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
