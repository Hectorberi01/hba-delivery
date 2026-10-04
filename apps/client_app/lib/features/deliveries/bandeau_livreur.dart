import 'dart:math' as maths;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_ui/hba_ui.dart';

import 'models.dart';
import 'suivi_temps_reel.dart';

/// Le bandeau de l'accueil, une fois le livreur affecte.
///
/// POURQUOI EN HAUT ET NON DANS LA COLONNE DU BAS. Une livraison en cours a
/// deja une carte en bas de l'accueil ; elle vit au milieu des boutons de
/// commande, et il faut la chercher. Les maquettes du 4 octobre 2026 la
/// remontent sous la marque, a l'endroit ou l'oeil tombe en ouvrant
/// l'application — ce qui correspond a la raison pour laquelle le client
/// l'ouvre : savoir ou en est son colis.
///
/// CE QU'IL AFFICHE EST CE QUI EST SU, ET RIEN DE PLUS.
///
/// UNE DUREE QUAND LE SERVICE EN MESURE UNE, UNE DISTANCE SINON. Depuis le
/// 4 octobre 2026, Delivery calcule l'arrivee a la lecture — derniere position
/// connue du livreur, puis duree demandee a Routing — et la rend avec un
/// drapeau qui dit si elle vient du repli a vol d'oiseau. Une mesure
/// geometrique est sous-estimee d'environ un tiers en ville : annoncee en
/// minutes, elle devient une promesse que la course ne tiendra pas. On retombe
/// donc sur la DISTANCE, annoncee pour ce qu'elle est — « à » et non
/// « dans ».
///
/// LA REGLE EST DANS LE MODELE, PAS ICI. Voir Delivery.arriveeDans : la
/// feuille de suivi montre la meme duree, et une regle recopiee deux fois est
/// une regle qui finit par diverger.
class BandeauLivreur extends ConsumerWidget {
  const BandeauLivreur({required this.course, super.key});

  final Delivery course;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);

    // SANS LIVREUR AFFECTE, IL N'Y A PAS DE BANDEAU. Avant cela, l'accueil
    // prend la mission en charge plein cadre — voir
    // DeliveryStatus.suiviParAccueil.
    if (course.driver == null) return const SizedBox.shrink();

    // LA CONNEXION TEMPS REEL EST PARTAGEE AVEC L'ECRAN DE SUIVI : meme
    // famille, meme identifiant, donc un seul notificateur et une seule
    // connexion, qu'on regarde l'accueil ou le suivi.
    final suivi = ref.watch(suiviLivraisonProvider(course.id));

    // LA DUREE D'ABORD, LA DISTANCE EN SECOURS. « Dans 4 min » repond a la
    // question que le client se pose ; « a 1,2 km » lui demande de la convertir
    // lui-meme, au jugé, dans un trafic qu'il ne voit pas.
    // SUR PLACE, AUCUN CHIFFRE. « Est sur place dans 1 min » n'a pas de sens :
    // il y est. La regle vaut pour la duree comme pour la distance, et elle est
    // posee une fois ici plutot que dans chacune des deux.
    final surPlace = course.status == DeliveryStatus.driverAtPickup;
    final duree = surPlace ? null : course.arriveeDans;
    final chiffre = duree != null
        ? _duree(duree)
        : (surPlace ? null : _distance(suivi.position));

    return HbaCard(
      // EN FEUILLE, PAS EN PAGE. Le detail monte par-dessus la carte d'accueil,
      // qui continue de vivre dessous ; refermer la feuille ramene exactement
      // ou l'on etait, sans reconstruire la carte ni perdre la position. Voir
      // la route « /livraison/:id ».
      onTap: () => context.push('/livraison/${course.id}'),
      padding: const EdgeInsets.all(HbaSpacing.sm),
      child: Row(
        children: [
          Container(
            height: 44,
            width: 44,
            decoration: const BoxDecoration(
              color: HbaColors.primarySoft,
              shape: BoxShape.circle,
            ),
            child: const Icon(
              Icons.electric_moped_outlined,
              color: HbaColors.primary,
            ),
          ),
          const SizedBox(width: HbaSpacing.sm),
          Expanded(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text.rich(
                  TextSpan(
                    children: [
                      TextSpan(text: _titre),
                      if (chiffre != null)
                        TextSpan(
                          text: ' $chiffre',
                          style: const TextStyle(
                            color: HbaColors.primary,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                    ],
                  ),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodyLarge
                      ?.copyWith(fontWeight: FontWeight.w600),
                ),
                Text(
                  _detail,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodySmall
                      ?.copyWith(color: HbaColors.inkMuted),
                ),
              ],
            ),
          ),
          const Icon(Icons.chevron_right, color: HbaColors.inkMuted),
        ],
      ),
    );
  }

  /// « dans 4 min », « dans moins d'une minute ».
  ///
  /// ON ARRONDIT A LA MINUTE, ET JAMAIS A ZERO. « Dans 0 min » se lit comme une
  /// panne d'affichage ; sous la minute, on le dit en toutes lettres.
  static String _duree(Duration duree) {
    final minutes = (duree.inSeconds / 60).round();
    return minutes < 1 ? 'dans moins d’une minute' : 'dans $minutes min';
  }

  /// La premiere ligne, sans le chiffre.
  String get _titre => switch (course.status) {
        DeliveryStatus.driverAtPickup => 'Votre livreur est sur place',
        DeliveryStatus.pickedUp => 'Votre colis est en route',
        _ => 'Votre livreur arrive',
      };

  String get _detail => switch (course.status) {
        DeliveryStatus.driverAtPickup =>
          'Il récupère votre colis au point d’enlèvement.',
        DeliveryStatus.pickedUp => 'Il se dirige vers le destinataire.',
        _ => 'Il est en route vers le point d’enlèvement.',
      };

  /// Vers quoi le livreur roule, selon l'etape.
  PointLivraison? get _cible => switch (course.status) {
        DeliveryStatus.pickedUp => course.dropoffPoint,
        _ => course.pickupPoint,
      };

  /// « à 1,2 km », « à 300 m », ou nul tant qu'aucune position n'est arrivee.
  ///
  /// A VOL D'OISEAU, ET ARRONDIE. Sous cinq cents metres on arrondit a la
  /// centaine, au-dela au dixieme de kilometre : donner « 1 237 m » ferait
  /// passer une approximation pour une mesure.
  ///
  /// RIEN NE S'AFFICHE TANT QUE RIEN N'EST SU. Le canal temps reel peut mettre
  /// quelques secondes a pousser la premiere position, et une distance a zero
  /// pendant ce temps-la dirait que le livreur est arrive.
  String? _distance(LatLng? livreur) {
    final cible = _cible;
    if (livreur == null || cible == null) return null;

    const metresParDegre = 111320.0;
    const versRadians = 3.1415926535897932 / 180;

    final dLat = (livreur.latitude - cible.latitude).abs() * metresParDegre;

    // LE COSINUS SE CALCULE, IL N'EST PAS ECRIT EN DUR. Il serait tentant de
    // poser 0,994, le cosinus de Cotonou, et de n'y plus penser : l'application
    // est essayee en FRANCE avant d'etre deployee au Benin, ou le cosinus vaut
    // 0,66. Une constante choisie pour l'un fausserait d'un tiers les distances
    // est-ouest chez l'autre, dans un sens ou dans l'autre.
    final dLng = (livreur.longitude - cible.longitude).abs() *
        metresParDegre *
        maths.cos((livreur.latitude + cible.latitude) / 2 * versRadians).abs();

    final metres = maths.sqrt(dLat * dLat + dLng * dLng);

    if (metres < 500) {
      final centaines = (metres / 100).round() * 100;
      return 'à ${centaines == 0 ? 100 : centaines} m';
    }

    final kilometres = (metres / 100).round() / 10;
    return 'à ${kilometres.toStringAsFixed(1).replaceAll('.', ',')} km';
  }
}
