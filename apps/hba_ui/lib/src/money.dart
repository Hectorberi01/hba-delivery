/// Mise en forme des montants.
///
/// LE FRANC CFA N'A PAS DE SOUS-UNITE. Aucun montant ne doit jamais s'afficher
/// avec une decimale, et aucun calcul d'interface ne doit produire un double :
/// les montants arrivent du serveur en entiers et repartent tels quels.
abstract final class Xof {
  /// Espace fine insecable : « 1 500 F » ne se coupe pas en fin de ligne.
  static const _groupSeparator = ' ';

  static String format(int amount) {
    final digits = amount.abs().toString();
    final buffer = StringBuffer();

    for (var i = 0; i < digits.length; i++) {
      if (i > 0 && (digits.length - i) % 3 == 0) {
        buffer.write(_groupSeparator);
      }
      buffer.write(digits[i]);
    }

    final sign = amount < 0 ? '-' : '';
    return '$sign$buffer${_groupSeparator}F';
  }

  /// Variante longue, pour les recapitulatifs.
  static String formatLong(int amount) => '${format(amount)} CFA';
}
