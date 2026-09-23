import 'package:flutter/material.dart';

import '../tokens.dart';

enum HbaButtonTone { primary, neutral, danger }

/// Bouton pleine largeur des ecrans d'action.
///
/// Il porte lui-meme son etat d'attente : un livreur qui appuie sur
/// « Collecte » dans un tunnel doit voir immediatement que l'action est prise
/// en charge, meme si la reponse du serveur met dix secondes.
class HbaButton extends StatelessWidget {
  const HbaButton({
    required this.label,
    required this.onPressed,
    this.tone = HbaButtonTone.primary,
    this.icon,
    this.busy = false,
    super.key,
  });

  final String label;
  final VoidCallback? onPressed;
  final HbaButtonTone tone;
  final IconData? icon;
  final bool busy;

  @override
  Widget build(BuildContext context) {
    final enabled = onPressed != null && !busy;

    final background = switch (tone) {
      HbaButtonTone.primary => HbaColors.primary,
      HbaButtonTone.neutral => HbaColors.surface,
      HbaButtonTone.danger => HbaColors.dangerSoft,
    };

    final foreground = switch (tone) {
      HbaButtonTone.primary => Colors.white,
      HbaButtonTone.neutral => HbaColors.ink,
      HbaButtonTone.danger => HbaColors.danger,
    };

    return SizedBox(
      height: 56,
      width: double.infinity,
      child: DecoratedBox(
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(HbaRadius.button),
          border: tone == HbaButtonTone.neutral
              ? Border.all(color: HbaColors.border)
              : null,
        ),
        child: FilledButton(
          onPressed: enabled ? onPressed : null,
          style: FilledButton.styleFrom(
            backgroundColor: background,
            foregroundColor: foreground,
            disabledBackgroundColor: background.withValues(alpha: 0.45),
            disabledForegroundColor: foreground.withValues(alpha: 0.7),
            elevation: 0,
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(HbaRadius.button),
            ),
          ),
          child: busy
              ? SizedBox(
                  height: 22,
                  width: 22,
                  child: CircularProgressIndicator(
                    strokeWidth: 2.4,
                    valueColor: AlwaysStoppedAnimation(foreground),
                  ),
                )
              : Row(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    if (icon != null) ...[
                      Icon(icon, size: 20),
                      const SizedBox(width: HbaSpacing.sm),
                    ],
                    Text(
                      label,
                      style: const TextStyle(
                        fontSize: 16,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                  ],
                ),
        ),
      ),
    );
  }
}
