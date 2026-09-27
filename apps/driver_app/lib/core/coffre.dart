import 'package:hba_core/hba_core.dart';

/// Ce que la file d'actions demande a un coffre, et rien de plus.
///
/// POURQUOI CETTE INTERFACE EXISTE : POUR POUVOIR TESTER.
///
/// « FlutterSecureStorage » est un greffon : il parle a du code natif par un
/// canal de plateforme. Dans un test Dart il n'y a ni Android, ni keystore, ni
/// canal — la seule facon de s'en servir serait de simuler ce canal a la main,
/// c'est-a-dire de coder en dur le nom d'un canal interne au greffon, qui peut
/// changer a la version suivante sans que rien ne previenne.
///
/// LA FILE D'ACTIONS EST LA CLASSE LA PLUS DELICATE DE L'APPLICATION : elle
/// porte des codes de remise, elle deduplique, elle plafonne, elle ecarte ce
/// qui appartient a un autre livreur. La laisser sans test parce qu'un greffon
/// est difficile a simuler serait un mauvais echange. Trois methodes derriere
/// une interface coutent moins cher que le defaut qu'on ne verra pas passer.
abstract interface class Coffre {
  Future<String?> read({required String key});
  Future<void> write({required String key, required String value});
  Future<void> delete({required String key});
}

/// Le vrai coffre, celui du telephone.
class CoffreSecurise implements Coffre {
  const CoffreSecurise(this._stockage);

  final FlutterSecureStorage _stockage;

  @override
  Future<String?> read({required String key}) => _stockage.read(key: key);

  @override
  Future<void> write({required String key, required String value}) =>
      _stockage.write(key: key, value: value);

  @override
  Future<void> delete({required String key}) => _stockage.delete(key: key);
}
