import 'package:flutter/foundation.dart';

/// Une mission terminee que ce client peut encore noter. Point 41.
///
/// ELLE N'EST PAS UNE NOTE, C'EST UNE INVITATION A EN LAISSER UNE. Rating
/// l'ouvre quand la mission s'acheve, depuis l'evenement du service concerne ;
/// elle reste ainsi, sans etoile, jusqu'a ce que le client en pose une — ou
/// pour toujours, si personne ne le fait.
///
/// AUCUN IDENTIFIANT DE LIVREUR N'Y FIGURE, et ce n'est pas un oubli de la
/// passerelle : le point 24 reserve l'identite du livreur a l'affectation, et
/// rien ne justifie de la rendre une fois la mission finie. Le client note la
/// COURSE ; Rating sait a qui la note revient.
@immutable
class AvisEnAttente {
  const AvisEnAttente({
    required this.nature,
    required this.reference,
    this.termineeLe,
  });

  /// « ride » ou « delivery », dans le mot de la passerelle.
  ///
  /// GARDE TEL QUEL, SANS REPLI SUR UNE VALEUR PLAUSIBLE. Supposer la livraison
  /// quand le champ manque ferait noter un colis qui porte l'identifiant d'une
  /// course : les deux services tirent leurs identifiants separement et rien ne
  /// les distingue sans ce mot. Inconnu, l'ecran ne propose pas de noter.
  final String nature;

  /// L'identifiant de la mission — de la course ou de la livraison.
  final String reference;

  /// Quand la mission s'est achevee. Sert a dire « votre course de ce matin »
  /// plutot qu'a trier : Rating rend deja les plus recentes d'abord.
  final DateTime? termineeLe;

  bool get estUneCourse => nature == 'ride';

  bool get notable => nature == 'ride' || nature == 'delivery';

  /// LE POURBOIRE N'EXISTE QUE POUR LES COURSES, ET LA PASSERELLE LE DIT AVANT
  /// L'ECRAN : la route « POST /rides/{id}/tip » n'a pas d'equivalent pour un
  /// colis. Proposer le geste sur une livraison ouvrirait une page de paiement
  /// qui ne peut qu'echouer.
  bool get pourboireOffrable => estUneCourse;

  static AvisEnAttente lire(Map<String, dynamic> json) => AvisEnAttente(
        nature: switch (json['serviceType']) {
          final String v => v.trim().toLowerCase(),
          _ => '',
        },
        reference: switch (json['referenceId']) {
          final String v => v,
          _ => '',
        },
        termineeLe: switch (json['missionCompletedAt']) {
          final String v when v.isNotEmpty => DateTime.tryParse(v)?.toLocal(),
          _ => null,
        },
      );
}
