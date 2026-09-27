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
  }) async {
    final data = await _api.post(
      '/quotes',
      body: {
        'pickupLatitude': pickupLatitude,
        'pickupLongitude': pickupLongitude,
        'dropoffLatitude': dropoffLatitude,
        'dropoffLongitude': dropoffLongitude,
        'packageWeightGrams': packageWeightGrams,
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
    final data = await _api.post(
      '/deliveries',
      idempotencyKey: idempotencyKey,
      body: {
        'quoteId': quoteId,
        'pickup': pickup,
        'dropoff': dropoff,
        'recipientName': recipientName,
        'recipientPhone': recipientPhone,
        'packageDescription': packageDescription,
        'packageWeightGrams': packageWeightGrams,
      },
    );

    final delivery = Delivery.fromJson(
      (data['delivery'] as Map?)?.cast<String, dynamic>() ?? const {},
    );

    final payment = data['payment'];
    final redirect = payment is Map ? payment['redirectUrl'] as String? : null;

    return (delivery, (redirect ?? '').isEmpty ? null : redirect);
  }

  Future<Delivery> byId(String id) async {
    final data = await _api.get('/deliveries/$id');
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
