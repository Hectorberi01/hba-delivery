import 'package:flutter_test/flutter_test.dart';
import 'package:hba_ui/hba_ui.dart';

void main() {
  group('Montants en francs CFA', () {
    test('les milliers sont separes par une espace fine insecable', () {
      // U+202F : le montant ne se coupe pas en fin de ligne.
      expect(Xof.format(1500), '1 500 F');
      expect(Xof.format(120), '120 F');
      expect(Xof.format(1234567), '1 234 567 F');
    });

    test('zero s affiche', () {
      expect(Xof.format(0), '0 F');
    });

    test('un montant negatif garde son signe', () {
      // Un remboursement ou un ajustement peut etre negatif.
      expect(Xof.format(-1500), '-1 500 F');
    });

    test('aucune decimale n apparait jamais', () {
      // Le XOF n'a pas de sous-unite : si une virgule ou un point sortait
      // d'ici, c'est que le montant aurait transite par un double.
      for (final amount in [1, 10, 999, 1000, 10001, 999999]) {
        expect(Xof.format(amount), isNot(contains('.')));
        expect(Xof.format(amount), isNot(contains(',')));
      }
    });
  });
}
