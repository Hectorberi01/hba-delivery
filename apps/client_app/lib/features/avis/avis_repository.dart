import 'package:hba_core/hba_core.dart';

import 'avis_models.dart';

/// Les avis du client, et le pourboire qui les accompagne.
///
/// DEUX CHOSES DANS UN SEUL DEPOT, ET C'EST VOULU. La note part vers Rating, le
/// pourboire vers Payment : deux services, deux routes. Mais ils se decident
/// dans le MEME geste, sur le meme ecran, et les separer en deux depots aurait
/// oblige l'ecran a en connaitre deux pour une seule action du client.
///
/// LA NOTE NE DOIT JAMAIS BLOQUER QUOI QUE CE SOIT (point 41). L'appelant
/// attrape et affiche ; rien dans l'application ne depend de la reussite de ces
/// appels.
class AvisRepository {
  AvisRepository(this._api);

  final ApiClient _api;

  /// Ce que ce client peut encore noter.
  ///
  /// AUCUN IDENTIFIANT N'EST ENVOYE : le service reprend le sujet du jeton. Un
  /// identifiant de client dans la requete ne dirait pas qui commande.
  Future<List<AvisEnAttente>> enAttente() async {
    final data = await _api.get('/reviews/pending');

    return [
      if (data['reviews'] case final List brut)
        for (final e in brut)
          if (e is Map) AvisEnAttente.lire(e.cast<String, dynamic>()),
    ];
  }

  /// Pose la note.
  ///
  /// LE LIVREUR N'EST PAS DANS LE CORPS, ET IL NE PEUT PAS L'ETRE. Rating le
  /// tient de l'evenement de fin de mission : le laisser a l'application
  /// reviendrait a laisser noter quelqu'un qui n'a rien porte.
  ///
  /// LE COMMENTAIRE EST FACULTATIF ET BORNE A 200 CARACTERES COTE SERVICE. Le
  /// champ de l'ecran applique la meme borne, pour que le refus n'arrive pas
  /// apres coup.
  Future<void> noter({
    required String nature,
    required String reference,
    required int etoiles,
    String? commentaire,
  }) async {
    await _api.post(
      '/reviews',
      body: {
        'serviceType': nature,
        'referenceId': reference,
        'stars': etoiles,
        if (commentaire != null && commentaire.isNotEmpty)
          'comment': commentaire,
      },

      // UNE CLE TIREE ICI, ET RIEN NE S'APPUIE DESSUS. Le socle l'exige pour
      // tout appel mutant, mais la passerelle ne la relaie pas sur cette route :
      // ce qui empeche un second avis est la regle de Rating — une note par
      // mission, verifiee par un index unique. Un double appui revient donc en
      // refus, que l'ecran affiche tel quel.
      idempotencyKey: ApiClient.newIdempotencyKey(),
    );
  }

  /// Ouvre un pourboire et rend l'adresse de sa page de paiement.
  ///
  /// UNE SECONDE PAGE DE PAIEMENT, PAS UN SUPPLEMENT SUR LA PREMIERE. La course
  /// est encaissee avant la recherche d'un chauffeur (point 35) ; au moment du
  /// pourboire, cette transaction est close depuis longtemps et aucun
  /// fournisseur ne sait la rouvrir.
  ///
  /// NUL QUAND L'OUVERTURE A ECHOUE. L'ecran le dit plutot que d'ouvrir une
  /// page blanche — et la note, elle, est deja posee.
  ///
  /// LA CLE D'IDEMPOTENCE EST TIREE PAR L'APPELANT et conservee d'une tentative
  /// a l'autre : la rejouer rend LE MEME pourboire et la meme adresse, jamais
  /// un second don.
  Future<String?> pourboire({
    required String courseId,
    required int montantXof,
    required String idempotencyKey,
    String? telephone,
  }) async {
    final data = await _api.post(
      '/rides/$courseId/tip',
      body: {
        'amountXof': montantXof,
        if (telephone != null && telephone.isNotEmpty) 'payerPhone': telephone,
      },
      idempotencyKey: idempotencyKey,
    );

    return switch (data['payment']) {
      final Map paiement => switch (paiement['redirectUrl']) {
          final String url when url.isNotEmpty => url,
          _ => null,
        },
      _ => null,
    };
  }
}
