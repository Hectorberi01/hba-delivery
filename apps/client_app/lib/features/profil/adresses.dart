import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';

import '../../core/providers.dart';

/// Une adresse favorite du client.
///
/// LE REPERE ECRIT COMPTE AUTANT QUE LE POINT GPS, et souvent davantage. A
/// Cotonou, « Carre 442, en face de la pharmacie » conduit un livreur la ou
/// deux coordonnees seules ne suffisent pas : il n'y a pas de numero de rue a
/// lire sur les maisons.
class AdresseFavorite {
  const AdresseFavorite({
    required this.id,
    required this.libelle,
    required this.repere,
    required this.latitude,
    required this.longitude,
    required this.parDefaut,
  });

  final String id;
  final String libelle;
  final String repere;
  final double latitude;
  final double longitude;
  final bool parDefaut;

  static AdresseFavorite? depuis(Object? json) {
    if (json is! Map) return null;

    final adresse = json['address'];
    final lieu = adresse is Map ? adresse['location'] : null;
    final point = lieu is Map ? lieu['point'] : null;

    return AdresseFavorite(
      id: json['id'] is String ? json['id'] as String : '',
      libelle: json['label'] is String ? json['label'] as String : '',
      repere: lieu is Map && lieu['landmark'] is String
          ? lieu['landmark'] as String
          : '',
      latitude: point is Map ? ((point['latitude'] as num?)?.toDouble() ?? 0) : 0,
      longitude:
          point is Map ? ((point['longitude'] as num?)?.toDouble() ?? 0) : 0,
      parDefaut: json['isDefault'] == true,
    );
  }
}

class AdressesRepository {
  AdressesRepository(this._api);

  final ApiClient _api;

  /// LES QUATRE ROUTES RENDENT LA LISTE COMPLETE, pas l'element touche. C'est
  /// le service qui decide de l'ordre et du drapeau « par defaut » — poser une
  /// nouvelle adresse par defaut retire le drapeau de l'ancienne, et seule la
  /// liste entiere le montre. L'application ne recalcule donc rien.
  List<AdresseFavorite> _liste(Object? brut) {
    if (brut is! List) return const [];
    return brut
        .map(AdresseFavorite.depuis)
        .whereType<AdresseFavorite>()
        .toList(growable: false);
  }

  Future<List<AdresseFavorite>> lire() async =>
      _liste((await _api.get('/addresses'))['addresses']);

  Future<List<AdresseFavorite>> ajouter({
    required String libelle,
    required String repere,
    required double latitude,
    required double longitude,
    required bool parDefaut,
  }) async {
    // LA CLE D'IDEMPOTENCE EST FAITE DU CONTENU, PAS D'UN HASARD.
    //
    // Une cle aleatoire ne protege de rien : le deuxieme envoi en aurait une
    // autre et creerait un doublon. Faite du libelle et du point, elle rend le
    // reenvoi apres une coupure inoffensif — c'est la meme adresse, le service
    // rend la meme liste — tout en laissant passer deux adresses reellement
    // differentes.
    final cle = 'adresse:$libelle:${latitude.toStringAsFixed(6)}'
        ':${longitude.toStringAsFixed(6)}';

    final data = await _api.post('/addresses', idempotencyKey: cle, body: {
      'label': libelle,
      'latitude': latitude,
      'longitude': longitude,
      'landmark': repere,

      // LE TELEPHONE ET LE CONTACT PARTENT VIDES, ET LA PASSERELLE LES REMPLIT.
      //
      // CE COMMENTAIRE DISAIT AUTRE CHOSE, ET IL AVAIT TORT. Il affirmait que
      // les laisser vides n'etait pas un oubli, au motif qu'une adresse
      // favorite n'est pas un point de collecte. Le raisonnement tenait ; le
      // domaine, lui, disait non : Directory refuse une adresse sans telephone
      // valide (INVALID_PHONE) ni nom de contact (MISSING_CONTACT_NAME). Le
      // bouton « Ajouter » ne pouvait donc pas marcher, et le commentaire
      // expliquait pourquoi c'etait normal.
      //
      // La passerelle reprend desormais le telephone et le nom DU COMPTE, lus
      // dans le jeton. C'est vrai — c'est son adresse, c'est lui le contact —
      // et l'application n'a rien de plus a demander.
      'phone': '',
      'contactName': '',

      'setAsDefault': parDefaut,
    });
    return _liste(data['addresses']);
  }

  /// SUPPRIMER PUIS RELIRE, parce que « delete » ne rend rien de typé cote
  /// client. Une requete de plus, sur un geste rare : le prix est nul compare a
  /// une liste qui montrerait encore l'adresse effacee.
  Future<List<AdresseFavorite>> supprimer(String id) async {
    await _api.delete('/addresses/$id');
    return lire();
  }
}

final adressesRepositoryProvider = Provider<AdressesRepository>(
  (ref) => AdressesRepository(ref.watch(apiClientProvider)),
);

final adressesProvider = FutureProvider<List<AdresseFavorite>>(
  (ref) => ref.watch(adressesRepositoryProvider).lire(),
);
