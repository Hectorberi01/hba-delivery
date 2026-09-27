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
