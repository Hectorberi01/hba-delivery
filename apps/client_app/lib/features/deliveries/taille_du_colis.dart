import 'package:flutter/material.dart';

/// Les trois tailles proposees au client : ce qu'elles pesent au plus, et ce
/// qu'elles proposent par defaut.
///
/// LE SERVICE NE CONNAIT PAS LES TAILLES, IL NE CONNAIT QUE DES GRAMMES. La
/// grille tarifaire facture au poids ; « Petit », « Moyen » et « Grand » sont
/// un habillage d'ECRAN, pose ici et nulle part ailleurs. Rien n'en sort vers
/// la passerelle qu'un entier.
///
/// POURQUOI REMPLACER UN CHAMP LIBRE PAR TROIS CARTES. Personne ne sait ce que
/// pese son colis. Le champ demandait une precision que le client n'a pas, et
/// la valeur par defaut etait acceptee telle quelle neuf fois sur dix : le
/// choix n'existait donc pas vraiment, il etait juste penible. Trois objets
/// familiers posent la meme question dans des termes auxquels on peut repondre.
///
/// POURQUOI LE POIDS EST REVENU PAR-DESSUS, le 4 octobre 2026. Les maquettes
/// annoncent sur les cartes des PLAFONDS — « jusqu'à 5 kg », « 20 kg »,
/// « 50 kg » — la ou le code n'envoyait qu'un poids de reference de 1, 5 et
/// 15 kg. Afficher un plafond et facturer une reference, c'est un colis de
/// quatre kilos paye au tarif d'un seul : l'ecart part en perte a chaque
/// course, et il grandit avec la taille. Les deux ne pouvaient pas coexister.
///
/// CE QUI A ETE TRANCHE : LA TAILLE BORNE, LE POIDS FACTURE. La carte choisit
/// une plage et pose un poids de depart ; le client ajuste ce poids a
/// l'interieur de la plage s'il sait faire mieux, et c'est CE poids qui part a
/// la tarification. Celui qui ne touche a rien paie exactement ce qu'il payait
/// avant le 4 octobre — les poids de depart sont les anciens poids de
/// reference, au gramme pres — donc la correction ne rencherit personne par
/// surprise ; elle ouvre seulement la porte a une declaration honnete pour les
/// colis lourds, qui etaient jusqu'ici factures comme des legers.
///
/// LES NOMBRES SONT UN CHOIX PRODUIT, PAS UNE MESURE. Plafonds et poids de
/// depart ont ete fixes faute de releve terrain : une enveloppe et un sac
/// tiennent sous le kilo, un carton de marche tourne autour de cinq, et ce
/// qu'on charge sur un tricycle depasse la dizaine. CE SONT CES NOMBRES QUI
/// FIXENT LE PRIX VU PAR LE CLIENT : les corriger change la facture, et c'est
/// ici que ca se fait, en un seul endroit.
enum TailleDuColis {
  petit('Petit', 'Enveloppe, sac', 1000, 5000, Icons.mail_outline),
  moyen('Moyen', 'Carton', 5000, 20000, Icons.inventory_2_outlined),
  grand('Grand', 'Charge de tricycle', 15000, 50000, Icons.widgets_outlined);

  const TailleDuColis(
    this.libelle,
    this.exemple,
    this.defautGrammes,
    this.plafondGrammes,
    this.icone,
  );

  final String libelle;

  /// Ce a quoi le client peut comparer son colis.
  final String exemple;

  /// Le poids propose quand la taille est choisie.
  ///
  /// CE SONT LES ANCIENS POIDS DE REFERENCE, VOLONTAIREMENT. Ils etaient le
  /// prix paye avant que le poids devienne reglable : les garder comme point
  /// de depart fait que personne ne voit sa facture monter sans avoir touche a
  /// quoi que ce soit.
  final int defautGrammes;

  /// Le poids maximum que cette taille accepte, et ce que la carte annonce.
  ///
  /// IL BORNE LE REGLAGE, IL NE LE REMPLACE PAS. Au-dela, c'est la taille
  /// au-dessus qu'il faut choisir — c'est aussi ce que le livreur devra
  /// charger, et un carton de trente kilos n'est pas une affaire de moto.
  final int plafondGrammes;

  final IconData icone;

  /// « jusqu'à 5 kg », tel que la carte l'annonce.
  String get plafondLibelle => 'jusqu’à ${plafondGrammes ~/ 1000} kg';

  /// La plus petite taille qui accepte ce poids.
  ///
  /// SERT A REOUVRIR UNE COMMANDE SANS PERDRE LE CHOIX du client. Une reprise
  /// de paiement rend un poids en grammes, jamais une taille : sans cette
  /// correspondance, l'ecran se rouvrirait sur « Petit » quelle qu'ait ete la
  /// commande.
  ///
  /// LE PLAFOND DECIDE, PAS LA PROXIMITE. Chercher la reference la plus proche
  /// ramenerait un colis de six kilos sur « Petit » — dont le plafond est
  /// cinq — et le reglage se retrouverait hors de ses propres bornes.
  static TailleDuColis depuisLesGrammes(int grammes) {
    for (final taille in TailleDuColis.values) {
      if (grammes <= taille.plafondGrammes) return taille;
    }
    return TailleDuColis.grand;
  }
}
