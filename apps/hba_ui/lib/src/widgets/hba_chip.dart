import 'package:flutter/material.dart';

import '../tokens.dart';
import 'hba_relief.dart';

enum HbaChipTone { neutral, success, warning, danger, primary }

/// Pastille d'etat. Ne porte jamais une action, seulement une information.
///
/// CREUSE, PAS POSEE. Une pastille est une inscription dans la matiere, pas un
/// bloc de plus : la creuser la distingue immediatement des cartes, qui sont
/// toutes en relief. C'est la regle qui tient tout le style — ce qui depasse
/// s'appuie, ce qui est creux se remplit.
///
/// LA COULEUR EST DANS LE POINT, LE LIBELLE EST EN COULEUR SOMBRE. Les quatre
/// teintes d'etat sont verifiees contre le fond du creux, la plus sombre des
/// trois surfaces du systeme : elles y tiennent 4,5:1. Le point, lui, n'a
/// besoin que de 3:1 — c'est un element d'interface, pas du texte.
class HbaChip extends StatelessWidget {
  const HbaChip({required this.label, this.tone = HbaChipTone.neutral, super.key});

  final String label;
  final HbaChipTone tone;

  @override
  Widget build(BuildContext context) {
    final (fond, encre) = switch (tone) {
      HbaChipTone.neutral => (HbaColors.surfaceSunken, HbaColors.inkMuted),
      HbaChipTone.success => (HbaColors.successSoft, HbaColors.success),
      HbaChipTone.warning => (HbaColors.warningSoft, HbaColors.warning),
      HbaChipTone.danger => (HbaColors.dangerSoft, HbaColors.danger),
      HbaChipTone.primary => (HbaColors.primarySoft, HbaColors.primaryInk),
    };

    return HbaCreux(
      radius: HbaRadius.chip,
      color: fond,
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 7),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (tone != HbaChipTone.neutral) ...[
            Container(
              height: 6,
              width: 6,
              decoration: BoxDecoration(color: encre, shape: BoxShape.circle),
            ),
            const SizedBox(width: HbaSpacing.sm),
          ],
          Text(
            label,
            style: TextStyle(
              color: encre,
              fontSize: 12,
              fontWeight: FontWeight.w700,
              letterSpacing: 0.2,
            ),
          ),
        ],
      ),
    );
  }
}
