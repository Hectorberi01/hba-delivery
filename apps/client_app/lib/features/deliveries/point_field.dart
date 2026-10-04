import 'package:flutter/material.dart';
import 'package:hba_ui/hba_ui.dart';

import 'choix_du_point.dart';

/// Un point GPS choisi par l'utilisateur.
class PickedPoint {
  const PickedPoint({required this.latitude, required this.longitude});

  final double latitude;
  final double longitude;

  /// Affichage de controle. Six decimales valent environ onze centimetres :
  /// au-dela, on affiche du bruit de mesure.
  String get pretty =>
      '${latitude.toStringAsFixed(6)}, ${longitude.toStringAsFixed(6)}';
}

/// Champ « point », qui ouvre la feuille de choix.
///
/// Il n'affiche jamais de champ de saisie : une latitude ne se tape pas.
///
/// IL N'OUVRE PLUS LA CARTE DIRECTEMENT, depuis le 4 octobre 2026. Il ouvrait
/// le selecteur de carte, donc il imposait de trainer une epingle au doigt —
/// meme vers un endroit ou le client va toutes les semaines, meme quand la
/// reponse etait « la ou je suis ». La feuille pose d'abord les reponses qu'on
/// a deja : la position, les adresses enregistrees, les dernieres utilisees ;
/// la carte y figure, en dernier, pour ce qui n'a ni nom ni passe. Voir
/// [ChoixDuPoint].
class PointField extends StatelessWidget {
  const PointField({
    required this.label,
    required this.hint,
    required this.value,
    required this.onChanged,
    this.accentue = false,
    super.key,
  });

  final String label;

  /// Ce qu'on demande a l'utilisateur quand rien n'est choisi.
  final String hint;

  final PickedPoint? value;

  /// Appele avec le point ET le repere qui l'accompagne, quand il y en a un.
  ///
  /// LE REPERE VOYAGE AVEC LE POINT, et c'est tout l'interet de la feuille : une
  /// adresse enregistree porte « en face de la pharmacie », et le client ne doit
  /// pas avoir a le retaper sous pretexte qu'il a choisi dans une liste.
  final ValueChanged<PointChoisi> onChanged;

  /// Met la carte en avant alors qu'aucun point n'est encore choisi.
  ///
  /// SERT A DESIGNER LE PROCHAIN GESTE, pas a feter celui qui est fait. Un
  /// formulaire de six champs ou rien ne ressort laisse chercher par ou
  /// commencer ; une seule carte encadree repond a la question sans texte.
  final bool accentue;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final point = value;

    return HbaCard(
      highlighted: point != null || accentue,
      padding: const EdgeInsets.all(HbaSpacing.md),
      onTap: () async {
        final choisi = await Navigator.of(context).push<PointChoisi>(
          ModalBottomSheetRoute<PointChoisi>(
            builder: (_) => ChoixDuPoint(titre: label, actuel: point),

            // LES MEMES REGLAGES QUE PageEnFeuille, ET POUR LES MEMES RAISONS :
            // la feuille doit pouvoir depasser la moitie de l'ecran, laisser
            // voir ce qu'il y a dessous, et porter elle-meme son fond et ses
            // coins. Voir feuille_modale.dart.
            isScrollControlled: true,
            modalBarrierColor: const Color(0x33000000),
            backgroundColor: Colors.transparent,
            elevation: 0,
          ),
        );

        if (choisi != null) onChanged(choisi);
      },
      child: Row(
        children: [
          Container(
            height: 44,
            width: 44,
            decoration: BoxDecoration(
              color: point == null && !accentue
                  ? HbaColors.background
                  : HbaColors.primarySoft,
              borderRadius: BorderRadius.circular(14),
            ),
            child: Icon(
              point == null ? Icons.place_outlined : Icons.place,
              color: point == null && !accentue
                  ? HbaColors.inkFaint
                  : HbaColors.primary,
            ),
          ),
          const SizedBox(width: HbaSpacing.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  label.toUpperCase(),
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: HbaColors.inkFaint,
                    letterSpacing: 0.6,
                    fontWeight: FontWeight.w600,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  point == null ? hint : point.pretty,
                  style: theme.textTheme.titleMedium?.copyWith(
                    color: point == null ? HbaColors.inkFaint : HbaColors.ink,
                  ),
                ),
              ],
            ),
          ),
          const Icon(Icons.chevron_right, color: HbaColors.inkFaint),
        ],
      ),
    );
  }
}
