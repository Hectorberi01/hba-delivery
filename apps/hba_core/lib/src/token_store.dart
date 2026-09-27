import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Jetons de session.
///
/// LE JETON DE RAFRAICHISSEMENT NE SERT QU'UNE FOIS. Chaque rafraichissement en
/// renvoie un nouveau, et rejouer l'ancien revoque toute la session. Le nouveau
/// est donc ecrit AVANT que l'appel soit considere comme termine.
///
/// AUCUNE METHODE DE CETTE CLASSE NE LEVE, ET C'EST LE POINT DE CE FICHIER.
///
/// Le coffre d'Android n'est pas une simple base de donnees : il s'appuie sur
/// le keystore materiel, qui est INVALIDE dans plusieurs situations ordinaires
/// — mise a jour du systeme, restauration depuis une sauvegarde, changement du
/// verrouillage d'ecran. Dans ces cas, « read » ne rend pas null : il LEVE.
///
/// Tant que ces exceptions remontaient, une seule d'entre elles suffisait a
/// laisser l'application sur son ecran de demarrage — un rond qui tourne, sans
/// texte, sans bouton, sans sortie. Le livreur relancait et revoyait le meme
/// rond. Une panne du coffre doit couter une reconnexion par SMS, pas un
/// telephone inutilisable.
///
/// LA MEMOIRE VIVE PASSE AVANT LE COFFRE, partout. Quand l'ecriture echoue, la
/// session reste valable POUR CETTE EXECUTION : le livreur finit sa journee, et
/// ne perd que la memoire de sa connexion au prochain demarrage.
class TokenStore {
  TokenStore(this._storage, {required String prefix, this.onErreur})
      : _prefix = prefix;

  final FlutterSecureStorage _storage;

  /// Appele a chaque echec du coffre. Le paquet ne connait pas le rapporteur
  /// de plantages de l'application : elle le branche elle-meme.
  final void Function(Object erreur, StackTrace pile, String operation)? onErreur;

  /// Prefixe de cle, pour que deux applications HBA installees sur le meme
  /// telephone ne se marchent pas dessus.
  final String _prefix;

  String? _accessToken;
  bool _indisponible = false;

  String get _accessKey => '$_prefix.access';
  String get _refreshKey => '$_prefix.refresh';
  String get _deviceKey => '$_prefix.device';

  /// Garde en memoire : le jeton est lu a chaque requete, et un aller-retour
  /// vers le trousseau a chaque appel coute cher.
  String? get accessToken => _accessToken;

  /// Vrai des qu'une operation a echoue.
  ///
  /// CE N'EST PAS UNE CURIOSITE DE DIAGNOSTIC : il dit que la session NE SERA
  /// PAS CONSERVEE d'un demarrage a l'autre. L'application peut le dire au
  /// livreur plutot que de le lui faire decouvrir le lendemain matin.
  bool get coffreIndisponible => _indisponible;

  void _echec(Object erreur, StackTrace pile, String operation) {
    _indisponible = true;
    onErreur?.call(erreur, pile, operation);
  }

  Future<void> load() async {
    try {
      _accessToken = await _storage.read(key: _accessKey);
    } on Object catch (erreur, pile) {
      // ON REPART DECONNECTE, PAS BLOQUE. Sans jeton lisible, la seule verite
      // disponible est « on ne sait pas » — et de ces deux facons de la
      // traduire, envoyer le livreur vers l'ecran de connexion lui coute un
      // SMS, tandis que l'attendre indefiniment lui coute sa journee.
      _accessToken = null;
      _echec(erreur, pile, 'lecture du jeton');
    }
  }

  Future<String?> readRefreshToken() async {
    try {
      return await _storage.read(key: _refreshKey);
    } on Object catch (erreur, pile) {
      _echec(erreur, pile, 'lecture du jeton de rafraichissement');
      return null;
    }
  }

  Future<void> save({required String accessToken, required String refreshToken}) async {
    // LA MEMOIRE D'ABORD. Si l'ecriture echoue, les requetes de cette
    // execution doivent quand meme partir avec le jeton neuf : le placer
    // apres les ecritures laisserait l'application authentifiee avec un jeton
    // perime qu'elle vient elle-meme de remplacer.
    _accessToken = accessToken;

    try {
      // Le rafraichissement d'abord : si l'application est tuee entre les deux
      // ecritures, mieux vaut un jeton d'acces perime qu'un rafraichissement
      // perdu, qui obligerait a se reconnecter.
      await _storage.write(key: _refreshKey, value: refreshToken);
      await _storage.write(key: _accessKey, value: accessToken);
    } on Object catch (erreur, pile) {
      _echec(erreur, pile, 'ecriture des jetons');
    }
  }

  Future<void> clear() async {
    _accessToken = null;

    try {
      await _storage.delete(key: _accessKey);
      await _storage.delete(key: _refreshKey);
    } on Object catch (erreur, pile) {
      // LE JETON RESTE SUR LE DISQUE, ET CE N'EST PAS SANS CONSEQUENCE : au
      // prochain demarrage l'application croira a une session. Elle ne durera
      // pas — la deconnexion a revoque le jeton de rafraichissement cote
      // serveur, et le premier 401 la fermera. On le signale plutot que de le
      // taire.
      _echec(erreur, pile, 'effacement des jetons');
    }
  }

  /// Identifiant d'appareil, stable pour la duree de l'installation.
  ///
  /// EN CAS D'ECHEC DU COFFRE, IL DEVIENT EPHEMERE : chaque demarrage en
  /// produira un nouveau. Le serveur verra un appareil de plus, ce qui est
  /// desagreable ; refuser de se connecter le serait davantage.
  Future<String> deviceId(String Function() generate) async {
    try {
      final existing = await _storage.read(key: _deviceKey);
      if (existing != null) return existing;
    } on Object catch (erreur, pile) {
      _echec(erreur, pile, 'lecture de l\'identifiant d\'appareil');
      return generate();
    }

    final created = generate();

    try {
      await _storage.write(key: _deviceKey, value: created);
    } on Object catch (erreur, pile) {
      _echec(erreur, pile, 'ecriture de l\'identifiant d\'appareil');
    }

    return created;
  }
}
