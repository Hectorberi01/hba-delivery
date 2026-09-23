import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Jetons de session.
///
/// LE JETON DE RAFRAICHISSEMENT NE SERT QU'UNE FOIS. Chaque rafraichissement en
/// renvoie un nouveau, et rejouer l'ancien revoque toute la session. Le nouveau
/// jeton est donc ecrit AVANT que l'appel soit considere comme termine.
class TokenStore {
  TokenStore(this._storage);

  static const _accessKey = 'hba.driver.access';
  static const _refreshKey = 'hba.driver.refresh';
  static const _deviceKey = 'hba.driver.device';

  final FlutterSecureStorage _storage;

  String? _accessToken;

  /// Garde en memoire le jeton d'acces : il est lu a chaque requete, et un
  /// aller-retour vers le trousseau a chaque appel coute cher.
  String? get accessToken => _accessToken;

  Future<void> load() async {
    _accessToken = await _storage.read(key: _accessKey);
  }

  Future<String?> readRefreshToken() => _storage.read(key: _refreshKey);

  Future<void> save({required String accessToken, required String refreshToken}) async {
    // Le rafraichissement d'abord : si l'application est tuee entre les deux
    // ecritures, mieux vaut un jeton d'acces perime qu'un jeton de
    // rafraichissement perdu, qui obligerait a se reconnecter.
    await _storage.write(key: _refreshKey, value: refreshToken);
    await _storage.write(key: _accessKey, value: accessToken);
    _accessToken = accessToken;
  }

  Future<void> clear() async {
    await _storage.delete(key: _accessKey);
    await _storage.delete(key: _refreshKey);
    _accessToken = null;
  }

  /// Identifiant d'appareil, stable pour la duree de l'installation. Il
  /// accompagne chaque appel d'authentification.
  Future<String> deviceId(String Function() generate) async {
    final existing = await _storage.read(key: _deviceKey);
    if (existing != null) {
      return existing;
    }

    final created = generate();
    await _storage.write(key: _deviceKey, value: created);
    return created;
  }
}
