import '../../core/api_client.dart';
import '../../core/token_store.dart';

/// Defi OTP en cours. Ne porte jamais le code : il n'existe que sur le
/// telephone du livreur.
class OtpChallenge {
  const OtpChallenge({
    required this.challengeId,
    required this.expiresAt,
    required this.retryAfterSeconds,
  });

  final String challengeId;
  final DateTime expiresAt;
  final int retryAfterSeconds;
}

/// Ce que le BFF renvoie apres une verification reussie.
class SignedInDriver {
  const SignedInDriver({
    required this.displayName,
    required this.kycApproved,
  });

  final String displayName;

  /// Le compte existe des la premiere verification, mais le livreur ne peut
  /// pas travailler tant que ops n'a pas valide ses pieces. L'application doit
  /// vivre cet etat intermediaire plutot que de le traiter comme une panne.
  final bool kycApproved;
}

class AuthRepository {
  AuthRepository(this._api, this._tokens);

  final ApiClient _api;
  final TokenStore _tokens;

  Future<OtpChallenge> requestOtp(String phone) async {
    final deviceId = await _tokens.deviceId(ApiClient.newIdempotencyKey);

    final data = await _api.post(
      '/auth/otp/request',
      body: {'phone': phone, 'deviceId': deviceId},
      idempotencyKey: ApiClient.newIdempotencyKey(),
    );

    return OtpChallenge(
      challengeId: data['challengeId'] as String,
      expiresAt: DateTime.parse(data['expiresAt'] as String),
      retryAfterSeconds: (data['retryAfterSeconds'] as num?)?.toInt() ?? 60,
    );
  }

  /// Le nom est envoye a TOUT LE MONDE apres verification, inscription ou non :
  /// demander « etes-vous nouveau ? » avant reviendrait a dire quels numeros
  /// ont un compte. Le service l'ignore si le compte en a deja un.
  Future<SignedInDriver> verifyOtp({
    required String challengeId,
    required String code,
    required String displayName,
  }) async {
    final deviceId = await _tokens.deviceId(ApiClient.newIdempotencyKey);

    final data = await _api.post(
      '/auth/otp/verify',
      body: {
        'challengeId': challengeId,
        'code': code,
        'deviceId': deviceId,
        'displayName': displayName,
      },
      idempotencyKey: ApiClient.newIdempotencyKey(),
    );

    await _tokens.save(
      accessToken: data['accessToken'] as String,
      refreshToken: data['refreshToken'] as String,
    );

    return SignedInDriver(
      displayName: data['displayName'] as String? ?? displayName,
      kycApproved: data['kycApproved'] as bool? ?? false,
    );
  }

  Future<void> signOut() async {
    final refreshToken = await _tokens.readRefreshToken();
    if (refreshToken != null) {
      try {
        await _api.post(
          '/auth/logout',
          body: {'refreshToken': refreshToken},
          idempotencyKey: ApiClient.newIdempotencyKey(),
        );
      } on Object {
        // Une deconnexion locale ne doit jamais echouer parce que le reseau
        // est tombe : le jeton est efface quoi qu'il arrive.
      }
    }

    await _tokens.clear();
  }
}
