/// Erreur renvoyee par un BFF, telle qu'elle doit etre montree.
class ApiException implements Exception {
  const ApiException({
    required this.code,
    required this.message,
    this.statusCode,
  });

  /// Code metier du service : QUOTE_EXPIRED, INSUFFICIENT_BALANCE, etc.
  final String code;

  /// Message deja redige cote service, en francais.
  final String message;

  final int? statusCode;

  /// 409. Cote livreur, une offre prise par un autre : ce n'est pas une panne.
  bool get isConflict => statusCode == 409;

  bool get isUnauthorized => statusCode == 401;

  /// 404. Parfois une panne, parfois un objet qui n'existe pas encore et que
  /// l'appelant peut creer : les deux se distinguent par ce code, pas par le
  /// message.
  bool get isNotFound => statusCode == 404;

  /// Le serveur a-t-il EXAMINE la demande et l'a-t-il refusee ?
  ///
  /// LA QUESTION N'EST PAS « Y A-T-IL EU UNE ERREUR », C'EST « QUI A DECIDE ».
  /// Un refus est un jugement : la course avait deja avance, le code de remise
  /// etait faux, l'etat ne permettait pas l'action. Rejouer n'y changerait
  /// rien. Une panne, elle, n'est le jugement de personne : le serveur n'a pas
  /// regarde la demande, et la meme demande passera tout a l'heure.
  ///
  /// CE QUE CETTE DISTINCTION COUTAIT QUAND ELLE N'EXISTAIT PAS. La file
  /// d'actions hors ligne du livreur abandonnait toute ApiException comme un
  /// refus definitif. Or « _toApiException » y range aussi les 500, 502, 503,
  /// 504 et 429 : au retour du reseau, une passerelle en cours de
  /// redeploiement repondait 502, et la remise DEJA FAITE disparaissait de la
  /// file. Le client avait son colis, la course restait ouverte, et le livreur
  /// lisait « une erreur inattendue ».
  ///
  /// LES CAS DOUTEUX SONT DU COTE PRUDENT, c'est-a-dire FAUX :
  /// - 401 ne devrait pas arriver jusqu'ici — ApiClient rafraichit, et une
  ///   session reellement perdue purge la file. S'il arrive quand meme, ce
  ///   n'est pas un jugement sur l'action.
  /// - 408 et 429 disent « pas maintenant », jamais « non ».
  /// - un statut absent signifie qu'on ne sait pas, et on ne jette pas une
  ///   preuve de livraison sur une incertitude.
  bool get estUnRefusDuServeur {
    final code = statusCode;

    if (code == null || code == 401 || code == 408 || code == 429) {
      return false;
    }

    return code >= 400 && code < 500;
  }

  @override
  String toString() => 'ApiException($code, $message)';
}

/// Le reseau n'a pas repondu. Distinct d'une erreur metier : c'est ce cas qui
/// met une action en file plutot que de l'abandonner.
class OfflineException implements Exception {
  const OfflineException();

  @override
  String toString() => 'OfflineException';
}
