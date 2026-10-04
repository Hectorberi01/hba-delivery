import 'package:flutter/material.dart';

/// Le vehicule que le client PEUT demander pour son colis.
///
/// FACULTATIF, ET C'EST TOUT L'ECRAN QUI EN DEPEND. Le 4 octobre 2026 le
/// selecteur a d'abord ete retire — « le client n'est pas oblige de choisir le
/// type de vehicule » — puis remis le meme jour sous sa forme juste : il peut
/// choisir, ou ne pas choisir. Rien n'est preselectionne, et presser la carte
/// deja choisie la relache.
///
/// CE QUE LE CHOIX CHANGE : LA GRILLE, ET RIEN D'AUTRE. Le vehicule ne part
/// qu'au devis ; il ne figure pas dans le corps de creation et le dispatch ne
/// l'a jamais lu. Sans choix, Pricing applique sa regle ecrite — la moto, la
/// grille la moins chere — donc ne rien choisir ne coute jamais plus cher.
///
/// LES NOMS SONT CEUX QUE LA PASSERELLE SAIT LIRE. Voir
/// ClientDeliveryEndpoints.Vehicule : « moto », « tricycle », « voiture » ;
/// tout autre mot y devient Unspecified en silence.
enum VehiculeDuColis {
  moto('moto', 'Moto', 'Rapide et économique', Icons.two_wheeler),
  tricycle('tricycle', 'Tricycle', 'Idéal pour les colis moyens',
      Icons.electric_rickshaw),
  voiture('voiture', 'Voiture', 'Pour les gros colis', Icons.directions_car);

  const VehiculeDuColis(this.nom, this.libelle, this.detail, this.icone);

  /// Le mot envoye au devis.
  final String nom;

  final String libelle;

  /// Ce que le client peut y comparer sa charge.
  final String detail;

  final IconData icone;
}
