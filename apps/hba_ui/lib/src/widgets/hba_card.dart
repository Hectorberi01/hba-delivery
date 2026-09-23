import 'package:flutter/material.dart';

import '../tokens.dart';

/// Carte blanche a coins arrondis, brique de base de tous les ecrans.
class HbaCard extends StatelessWidget {
  const HbaCard({
    required this.child,
    this.padding = const EdgeInsets.all(HbaSpacing.md),
    this.onTap,
    this.highlighted = false,
    super.key,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final VoidCallback? onTap;

  /// Bordure orange : reserve a l'element sur lequel l'utilisateur doit agir.
  final bool highlighted;

  @override
  Widget build(BuildContext context) {
    final content = Padding(padding: padding, child: child);

    return DecoratedBox(
      decoration: BoxDecoration(
        color: HbaColors.surface,
        borderRadius: BorderRadius.circular(HbaRadius.card),
        border: Border.all(
          color: highlighted ? HbaColors.primary : HbaColors.border,
          width: highlighted ? 1.6 : 1,
        ),
      ),
      child: onTap == null
          ? content
          : Material(
              color: Colors.transparent,
              child: InkWell(
                onTap: onTap,
                borderRadius: BorderRadius.circular(HbaRadius.card),
                child: content,
              ),
            ),
    );
  }
}
