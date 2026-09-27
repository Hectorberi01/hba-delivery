import 'package:hba_core/hba_core.dart';

/// Defi OTP en cours. Ne porte jamais le code.
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

class SignedInCustomer {
  const SignedInCustomer({required this.displayName});

  final String displayName;
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

  /// Le nom part APRES verification, et pour TOUT LE MONDE.
  ///
  /// La route de demande repond la meme chose quel que soit le numero —
  /// inconnu, connu, suspendu. Demander « avez-vous un compte ? » avant
  /// reviendrait a dire quels numeros HBA connait. Le service ignore le nom si
  /// le compte en a deja un.
  Future<SignedInCustomer> verifyOtp({
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

    return SignedInCustomer(
      displayName: data['displayName'] as String? ?? displayName,
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
