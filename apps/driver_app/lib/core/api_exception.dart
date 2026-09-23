/// Erreur renvoyee par le BFF, telle qu'elle doit etre montree.
class ApiException implements Exception {
  const ApiException({
    required this.code,
    required this.message,
    this.statusCode,
  });

  /// Code metier du service : INSUFFICIENT_BALANCE, QUOTE_EXPIRED, etc.
  final String code;

  /// Message deja redige cote service, en francais.
  final String message;

  final int? statusCode;

  /// Une offre perdue n'est pas une panne : un autre livreur a ete plus rapide.
  /// L'ecran l'annonce calmement, sans rouge ni alerte.
  bool get isOfferLost => statusCode == 409;

  bool get isUnauthorized => statusCode == 401;

  @override
  String toString() => 'ApiException($code, $message)';
}

/// Le reseau n'a pas repondu. Distinct d'une erreur metier : c'est ce cas qui
/// met l'action en file plutot que de l'abandonner.
class OfflineException implements Exception {
  const OfflineException();

  @override
  String toString() => 'OfflineException';
}
