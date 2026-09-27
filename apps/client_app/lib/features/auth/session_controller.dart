import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
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

  void signedOut() => state = const SessionSignedOut();

  Future<void> signOut() async {
    await ref.read(authRepositoryProvider).signOut();
    signedOut();
  }
}
