import 'package:hba_core/hba_core.dart';

import 'reputation_models.dart';

/// Ce que le livreur sait de sa propre reputation.
///
/// AUCUN IDENTIFIANT DE LIVREUR NE TRANSITE, dans aucun sens, comme pour le
/// compte : la passerelle lit « driver_id » dans le jeton et Rating le force
/// cote service. Une route qui accepterait un identifiant en parametre
/// laisserait un livreur lire les avis d'un autre en changeant une chaine.
class ReputationRepository {
  ReputationRepository(this._api);

  final ApiClient _api;

  Future<Reputation> moyenne() async =>
      Reputation.lire(await _api.get('/rating'));

  /// Ses avis, un par un.
  ///
  /// UNE MOYENNE SANS DETAIL EST UNE SANCTION SANS MOTIF : les deux lectures
  /// vont ensemble, et la seconde n'est pas un confort.
  Future<List<AvisRecu>> avis({int limite = 20}) async {
    final data = await _api.get('/reviews', query: {'limit': limite});

    return [
      if (data['reviews'] case final List brut)
        for (final e in brut)
          if (e is Map) AvisRecu.lire(e.cast<String, dynamic>()),
    ];
  }
}
