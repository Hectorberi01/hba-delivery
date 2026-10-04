import 'package:hba_core/hba_core.dart';

import 'course_models.dart';

/// Le transport de personnes, vu du passager.
///
/// LE DEVIS N'EST PAS ICI, ET CE N'EST PAS UN OUBLI. Il se demande par
/// DeliveryRepository.quote avec « serviceType: course » : le prix a UN chemin,
/// et c'est celui qu'on ne veut surtout pas voir diverger. La grille, elle,
/// differe depuis le 1er octobre 2026 — la nature fait partie de sa cle — donc
/// le champ n'est pas facultatif pour une course : sans lui, Pricing rend un
/// devis de colis et la creation echoue sur QUOTE_SERVICE_MISMATCH.
class CourseRepository {
  CourseRepository(this._api);

  final ApiClient _api;

  /// Demande une course et rend l'adresse de paiement quand il y en a une.
  ///
  /// LA CLE D'IDEMPOTENCE EST TIREE UNE FOIS PAR TENTATIVE et conservee par
  /// l'appelant : la rejouer rend la MEME course et la MEME adresse de
  /// paiement, jamais une seconde course.
  Future<CourseCreee> demander({
    required String idempotencyKey,
    required String quoteId,
    required double departLatitude,
    required double departLongitude,
    required double destinationLatitude,
    required double destinationLongitude,
    required String vehicleType,
  }) async {
    return envoyer(
      idempotencyKey: idempotencyKey,
      corps: {
        'quoteId': quoteId,
        'pickupLatitude': departLatitude,
        'pickupLongitude': departLongitude,
        'destinationLatitude': destinationLatitude,
        'destinationLongitude': destinationLongitude,
        'vehicleType': vehicleType,
      },
    );
  }

  /// Envoie le corps tel quel, et rend la course avec son adresse de paiement.
  ///
  /// IL EXISTE POUR QUE LA REPRISE REJOUE EXACTEMENT LA MEME REQUETE. Payment
  /// reconnait sa propre cle et rend l'intention deja ouverte avec son adresse ;
  /// sans le corps d'origine, il n'y a rien a rejouer — et aucune route ne rend
  /// l'adresse de paiement d'une course deja creee, a dessein : elle expire.
  Future<CourseCreee> envoyer({
    required String idempotencyKey,
    required Map<String, Object?> corps,
  }) async {
    final data = await _api.post(
      '/rides',
      body: corps,
      idempotencyKey: idempotencyKey,
    );

    return CourseCreee.lire(data);
  }

  /// Les courses en cours, AU PLURIEL.
  ///
  /// Le point 35 autorise un passager a en avoir plusieurs — une pour lui, une
  /// pour un proche. L'ecran choisit celle qu'il suit et s'y tient : prendre la
  /// premiere de la liste la ferait changer des qu'une autre est commandee.
  Future<List<Course>> actives() async {
    final data = await _api.get('/rides/active');

    return [
      if (data['rides'] case final List brut)
        for (final e in brut)
          if (e is Map) Course.lire(e.cast<String, dynamic>()),
    ];
  }

  Future<Course> lire(String id) async =>
      Course.lire(await _api.get('/rides/$id'));

  /// L'historique, page par page.
  Future<(List<Course>, int)> historique({int pageSize = 20, int offset = 0}) async {
    final data = await _api.get(
      '/rides',
      query: {'pageSize': pageSize, 'offset': offset},
    );

    final courses = [
      if (data['rides'] case final List brut)
        for (final e in brut)
          if (e is Map) Course.lire(e.cast<String, dynamic>()),
    ];

    return (courses, switch (data['total']) {
      final int v => v,
      final num v => v.toInt(),
      final String v => int.tryParse(v) ?? courses.length,
      _ => courses.length,
    });
  }

  /// Annule la course.
  ///
  /// LE SERVICE DECIDE, PAS L'ECRAN. La politique de frais n'est pas tranchee
  /// (point 3) : l'application propose le geste et affiche le refus s'il vient.
  Future<Course> annuler(String id, {String? motif}) async => Course.lire(
        await _api.post(
          '/rides/$id/cancel',
          body: {if (motif != null && motif.isNotEmpty) 'reason': motif},
          idempotencyKey: ApiClient.newIdempotencyKey(),
        ),
      );
}
