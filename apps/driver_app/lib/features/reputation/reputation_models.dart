import 'package:flutter/foundation.dart';

/// La reputation d'un livreur, telle que le service la rend. Point 41.
///
/// LA MOYENNE N'EST PAS TOUJOURS MONTRABLE, ET C'EST LE SERVICE QUI LE DIT.
/// « assez » vient de Rating : dix courses ne font pas une moyenne, et afficher
/// 2,0 sur un seul avis est plus faux que de n'afficher rien. Le seuil ne se
/// rejoue pas ici — trois ecrans avec trois seuils finiraient par se
/// contredire, et le livreur lirait « 4,0 » la ou le client lit « pas encore
/// note ».
@immutable
class Reputation {
  const Reputation({
    required this.moyenne,
    required this.nombre,
    required this.assez,
  });

  /// Sur cinq, a deux decimales. ZERO QUAND IL N'Y A AUCUN AVIS — ce n'est pas
  /// une note de zero, le bareme commence a une etoile.
  final double moyenne;

  final int nombre;

  /// La moyenne est-elle assez fournie pour etre montree ?
  final bool assez;

  static const vide = Reputation(moyenne: 0, nombre: 0, assez: false);

  static Reputation lire(Map<String, dynamic> json) => Reputation(
        moyenne: switch (json['average']) {
          final num v => v.toDouble(),
          final String v => double.tryParse(v) ?? 0,
          _ => 0,
        },
        nombre: switch (json['count']) {
          final int v => v,
          final num v => v.toInt(),
          final String v => int.tryParse(v) ?? 0,
          _ => 0,
        },
        assez: json['enough'] == true,
      );
}

/// Un avis recu, tel que le livreur le lit.
///
/// AUCUN IDENTIFIANT DE CLIENT N'Y FIGURE, et la passerelle ne l'envoie pas :
/// le livreur apprend ce qu'on lui reproche, pas QUI le lui reproche. Une note
/// nominative se regle entre quatre yeux, et un avis qui se regle ne sert plus
/// a rien.
///
/// LA MISSION EST NOMMEE, ELLE. Sans elle, un livreur lirait « deux etoiles »
/// sans savoir de quel trajet on parle — donc sans pouvoir y repondre.
@immutable
class AvisRecu {
  const AvisRecu({
    required this.nature,
    required this.reference,
    required this.etoiles,
    this.commentaire,
    this.deposeLe,
  });

  /// « ride » ou « delivery ».
  final String nature;

  final String reference;

  final int etoiles;

  /// Vide le plus souvent : le commentaire est facultatif cote client.
  final String? commentaire;

  final DateTime? deposeLe;

  bool get estUneCourse => nature == 'ride';

  static AvisRecu lire(Map<String, dynamic> json) => AvisRecu(
        nature: switch (json['serviceType']) {
          final String v => v.trim().toLowerCase(),
          _ => '',
        },
        reference: switch (json['referenceId']) {
          final String v => v,
          _ => '',
        },
        etoiles: switch (json['stars']) {
          final int v => v,
          final num v => v.toInt(),
          final String v => int.tryParse(v) ?? 0,
          _ => 0,
        },
        commentaire: switch (json['comment']) {
          final String v when v.trim().isNotEmpty => v.trim(),
          _ => null,
        },
        deposeLe: switch (json['submittedAt']) {
          final String v when v.isNotEmpty => DateTime.tryParse(v)?.toLocal(),
          _ => null,
        },
      );
}
