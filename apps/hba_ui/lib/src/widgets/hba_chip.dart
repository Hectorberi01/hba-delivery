import 'package:flutter/material.dart';

import '../tokens.dart';

enum HbaChipTone { neutral, success, warning, danger, primary }

/// Pastille d'etat. Ne porte jamais une action, seulement une information.
class HbaChip extends StatelessWidget {
  const HbaChip({required this.label, this.tone = HbaChipTone.neutral, super.key});

  final String label;
  final HbaChipTone tone;

  @override
  Widget build(BuildContext context) {
    final (background, foreground) = switch (tone) {
      HbaChipTone.neutral => (HbaColors.background, HbaColors.inkMuted),
      HbaChipTone.success => (HbaColors.successSoft, HbaColors.success),
      HbaChipTone.warning => (HbaColors.warningSoft, HbaColors.warning),
      HbaChipTone.danger => (HbaColors.dangerSoft, HbaColors.danger),
      HbaChipTone.primary => (HbaColors.primarySoft, HbaColors.primary),
    };

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
      decoration: BoxDecoration(
        color: background,
        borderRadius: BorderRadius.circular(HbaRadius.chip),
      ),
      child: Text(
        label,
        style: TextStyle(
          color: foreground,
          fontSize: 12,
          fontWeight: FontWeight.w700,
          letterSpacing: 0.2,
        ),
      ),
    );
  }
}
