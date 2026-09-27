/// Plans de numerotation des pays desservis.
///
/// ILS VIVENT ICI, PAS DANS UNE APPLICATION. La regle est la meme pour le
/// client et pour le livreur, et le renumerotage beninois a montre ce que
/// coute sa duplication : corrige d'un cote, oublie de l'autre, et personne ne
/// s'en apercoit avant qu'un livreur ne recoive pas son code.
///
/// LE SERVEUR N'ACCEPTE QUE L'E.164 : PhoneNumber refuse un numero sans « + »
/// plutot que de le completer, et ne devine aucun indicatif. C'est donc ici
/// que la saisie locale devient un numero international, et nulle part
/// ailleurs.
library;

/// Benin. PLAN A DIX CHIFFRES DEPUIS LE 30 NOVEMBRE 2024 : l'ARCEP a ajoute le
/// prefixe 01 devant tous les numeros, quel que soit l'operateur. Un numero a
/// huit chiffres est donc un ancien numero, plus un numero valide.
///
/// La saisie a huit chiffres reste acceptee parce que les gens composent
/// encore de memoire l'ancien numero ; elle est completee, pas refusee. Ce qui
/// part au serveur est toujours la forme actuelle.
String? beninNational(String digits) {
  if (digits.length == 8) {
    return '01$digits';
  }

  if (digits.length == 10 && digits.startsWith('01')) {
    return digits;
  }

  return null;
}

/// France. Dix chiffres avec le zero initial, que la forme internationale
/// remplace par l'indicatif : 06 12 34 56 78 devient +33612345678.
String? franceNational(String digits) {
  if (digits.length == 10 && digits.startsWith('0')) {
    return digits.substring(1);
  }

  // Saisie deja internationale, sans le zero.
  if (digits.length == 9 && !digits.startsWith('0')) {
    return digits;
  }

  return null;
}

/// Un pays desservi, et la facon d'ecrire ses numeros.
class PhonePlan {
  const PhonePlan({
    required this.name,
    required this.dialingCode,
    required this.hint,
    required this.maxDigits,
    required this.toNational,
  });

  final String name;
  final String dialingCode;
  final String hint;
  final int maxDigits;
  final String? Function(String digits) toNational;

  /// Numero E.164, ou null si la saisie ne correspond pas au plan du pays.
  String? toE164(String digits) {
    final national = toNational(digits);
    return national == null ? null : '$dialingCode$national';
  }
}

const PhonePlan beninPlan = PhonePlan(
  name: 'Bénin',
  dialingCode: '+229',
  hint: '01 97 00 00 00',
  maxDigits: 10,
  toNational: beninNational,
);

/// La France n'est pas un marche : c'est le pays depuis lequel HBA teste, et
/// celui d'une partie de la diaspora qui envoie des colis vers Cotonou.
const PhonePlan francePlan = PhonePlan(
  name: 'France',
  dialingCode: '+33',
  hint: '06 12 34 56 78',
  maxDigits: 10,
  toNational: franceNational,
);

const List<PhonePlan> phonePlans = <PhonePlan>[beninPlan, francePlan];
