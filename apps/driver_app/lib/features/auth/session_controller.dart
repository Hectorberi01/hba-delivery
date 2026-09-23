import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api_client.dart';
import '../../core/providers.dart';
import '../../core/token_store.dart';
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
  @override
  SessionState build() => const SessionUnknown();

  TokenStore get _tokens => ref.read(tokenStoreProvider);

  /// Appele une fois au demarrage.
  Future<void> restore() async {
    await _tokens.load();

    if (_tokens.accessToken == null) {
      state = const SessionSignedOut();
      return;
    }

    try {
      final me = await ref.read(apiClientProvider).get('/me');
      state = SessionSignedIn(
        displayName: me['displayName'] as String? ?? '',
        kycApproved: me['kycApproved'] as bool? ?? false,
      );
    } on Object {
      // Hors connexion au lancement : on garde la session, l'ecran d'accueil
      // affichera ce qu'il a en cache. Un jeton invalide sera rejete au
      // premier appel et signedOut sera declenche par ApiClient.
      state = const SessionSignedIn(displayName: '', kycApproved: false);
    }
  }

  void signedIn(SignedInDriver driver) {
    state = SessionSignedIn(
      displayName: driver.displayName,
      kycApproved: driver.kycApproved,
    );
  }

  void signedOut() => state = const SessionSignedOut();

  Future<void> signOut() async {
    await ref.read(authRepositoryProvider).signOut();
    signedOut();
  }
}

/// Cle d'idempotence reutilisable par les ecrans.
String newActionKey() => ApiClient.newIdempotencyKey();
