import 'package:flutter/material.dart';
import 'package:hba_ui/hba_ui.dart';

import 'map_picker_screen.dart';

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

/// Champ « point sur la carte ».
///
/// Il n'affiche jamais de champ de saisie : une latitude ne se tape pas. Il
/// ouvre la carte et rend deux nombres.
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
  final ValueChanged<PickedPoint> onChanged;

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
        final picked = await Navigator.of(context).push<PickedPoint>(
          MaterialPageRoute(
            builder: (_) => MapPickerScreen(title: label, initial: point),
          ),
        );

        if (picked != null) onChanged(picked);
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
