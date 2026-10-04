import 'package:hba_core/hba_core.dart';

import 'models.dart';

/// Acces aux devis et aux livraisons.
class DeliveryRepository {
  DeliveryRepository(this._api);

  final ApiClient _api;

  /// Demande un devis. Il expire : l'ecran doit le redemander plutot que de
  /// creer une livraison avec un devis perime, que le service refusera.
  Future<Quote> quote({
    required double pickupLatitude,
    required double pickupLongitude,
    required double dropoffLatitude,
    required double dropoffLongitude,
    required int packageWeightGrams,

    /// Le vehicule souhaite : « moto », « voiture », « camionnette », « velo »,
    /// « tricycle ».
    ///
    /// ABSENT, LA PASSERELLE REPLIE SUR LA MOTO — et jusqu'au 1er octobre 2026
    /// elle l'ecrivait en dur. Un client ne pouvait donc pas demander le prix
    /// d'une voiture, meme quand la grille existait : les quatre autres
    /// vehicules etaient inatteignables depuis le telephone.
    String? vehicleType,

    /// La nature : « colis » ou « course ».
    ///
    /// ENVOYEE EXPLICITEMENT, MEME SI C'EST LE DEFAUT. Pricing replie l'absence
    /// sur le colis, mais un devis de colis demande sans le dire se revelerait
    /// en QUOTE_SERVICE_MISMATCH a la creation d'une course — et personne ne
    /// saurait pourquoi. Le dire ici, c'est le verifier au devis.
    String? serviceType,
  }) async {
    final data = await _api.post(
      '/quotes',
      body: {
        'pickupLatitude': pickupLatitude,
        'pickupLongitude': pickupLongitude,
        'dropoffLatitude': dropoffLatitude,
        'dropoffLongitude': dropoffLongitude,
        'packageWeightGrams': packageWeightGrams,
        if (vehicleType != null) 'vehicleType': vehicleType,
        if (serviceType != null) 'serviceType': serviceType,
      },
      idempotencyKey: ApiClient.newIdempotencyKey(),
    );

    return Quote.fromJson(data);
  }

  /// Cree la livraison et renvoie l'URL de paiement quand il y en a une.
  ///
  /// La cle d'idempotence est tiree UNE FOIS par tentative de creation et
  /// conservee par l'appelant : la rejouer renvoie la meme livraison au lieu
  /// d'en creer une seconde.
  Future<(Delivery, String?)> create({
    required String idempotencyKey,
    required String quoteId,
    required Map<String, Object?> pickup,
    required Map<String, Object?> dropoff,
    required String recipientName,
    required String recipientPhone,
    required String packageDescription,
    required int packageWeightGrams,
  }) async {
    return envoyer(
      idempotencyKey: idempotencyKey,
      corps: {
        'quoteId': quoteId,
        'pickup': pickup,
        'dropoff': dropoff,
        'recipientName': recipientName,
        'recipientPhone': recipientPhone,
        'packageDescription': packageDescription,
        'packageWeightGrams': packageWeightGrams,
      },
    );
  }

  /// Envoie le corps tel quel, et rend la livraison avec son adresse de paiement.
  ///
  /// IL EXISTE POUR QUE LA REPRISE REJOUE EXACTEMENT LA MEME REQUETE. Le service
  /// reconnait sa propre cle et rend la MEME livraison avec la MEME adresse, sans
  /// rien creer ni consommer un second devis — c'est le chemin de rejeu de
  /// CreateDeliveryHandler, et il a besoin du corps d'origine. « create » n'est
  /// plus qu'une facon lisible de le composer la premiere fois.
  Future<(Delivery, String?)> envoyer({
    required String idempotencyKey,
    required Map<String, Object?> corps,
  }) async {
    final data = await _api.post(
      '/deliveries',
      idempotencyKey: idempotencyKey,
      body: corps,
    );

    final delivery = Delivery.fromJson(
      (data['delivery'] as Map?)?.cast<String, dynamic>() ?? const {},
    );

    // L'ADRESSE DE LA PAGE DE PAIEMENT. Vide, l'ouverture a echoue cote serveur
    // et il n'y a rien a ouvrir : l'ecran doit le dire plutot que d'afficher un
    // bouton qui ne mene nulle part.
    final payment = data['payment'];
    final adresse = payment is Map ? payment['redirectUrl'] as String? : null;

    return (delivery, (adresse ?? '').isEmpty ? null : adresse);
  }

  /// Une livraison, avec ou sans le trace de son itineraire.
  ///
  /// LA GEOMETRIE SE DEMANDE, ELLE N'ARRIVE PAS TOUTE SEULE. Elle pese quelques
  /// kilo-octets et seul un ecran qui DESSINE la route en a besoin : le bandeau
  /// de l'accueil relit la meme livraison toutes les trente secondes pour
  /// n'afficher que quatre mots. A Cotonou, ces octets-la sont payes par le
  /// client, au megaoctet.
  ///
  /// LA DUREE ARRIVE DANS LES DEUX CAS : seul le trace est conditionne.
  Future<Delivery> byId(String id, {bool avecItineraire = false}) async {
    final data = await _api.get(
      avecItineraire ? '/deliveries/$id?itineraire=1' : '/deliveries/$id',
    );
    return Delivery.fromJson(data);
  }

  Future<List<Delivery>> list() async {
    final data = await _api.get('/deliveries');
    final items = data['deliveries'];
    if (items is! List) return const [];

    return items
        .whereType<Map<String, dynamic>>()
        .map(Delivery.fromJson)
        .toList(growable: false);
  }

  /// Annule une livraison.
  ///
  /// Le montant eventuellement rembourse depend de la politique d'annulation,
  /// qui n'est pas tranchee. L'application n'en calcule aucun : elle affiche ce
  /// que le service repond.
  Future<Delivery> cancel(String id, String reason) async {
    final data = await _api.post(
      '/deliveries/$id/cancel',
      body: {'reason': reason},
      idempotencyKey: 'cancel:$id',
    );

    return Delivery.fromJson(data);
  }
}
