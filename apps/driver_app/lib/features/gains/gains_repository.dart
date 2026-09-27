import 'package:hba_core/hba_core.dart';

import 'models.dart';

/// Acces au compte du livreur et a ses demandes de versement.
///
/// AUCUN IDENTIFIANT DE LIVREUR NE TRANSITE, dans aucun sens. Le service prend
/// le `driver_id` du jeton : une route qui accepterait un identifiant en
/// parametre laisserait un livreur lire le compte d'un autre en changeant une
/// chaine.
class GainsRepository {
  GainsRepository(this._api);

  final ApiClient _api;

  /// Le releve. [limite] plafonne les LIGNES, jamais les cumuls.
  Future<Releve> releve({int limite = 50}) async {
    final data = await _api.get('/earnings', query: {'limit': limite});
    return Releve.fromJson(data);
  }

  Future<List<DemandeVersement>> demandes({int limite = 10}) async {
    final data = await _api.get('/earnings/payouts', query: {'limit': limite});
    final liste = data['payouts'];

    return liste is List
        ? [
            for (final ligne in liste)
              if (ligne is Map<String, dynamic>) DemandeVersement.fromJson(ligne),
          ]
        : const <DemandeVersement>[];
  }

  /// Ouvre une demande de versement.
  ///
  /// LA CLE D'IDEMPOTENCE NE PROTEGE RIEN ICI, et il vaut mieux le savoir que
  /// le croire : la passerelle ne la relaie pas sur cette route. Ce qui arrete
  /// un double appui est la regle du serveur — une seule demande en cours par
  /// livreur, verifiee par un index unique en base. Le deuxieme appel revient
  /// donc avec PAYOUT_ALREADY_PENDING, ce que l'ecran affiche tel quel.
  Future<DemandeVersement> demanderVersement(int montantXof) async {
    final data = await _api.post(
      '/earnings/payouts',
      body: {'amountXof': montantXof},
      idempotencyKey: ApiClient.newIdempotencyKey(),
    );

    return DemandeVersement.fromJson(data);
  }
}
