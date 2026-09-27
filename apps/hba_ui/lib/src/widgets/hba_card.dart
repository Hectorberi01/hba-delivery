import 'package:flutter/material.dart';

import '../tokens.dart';
import 'hba_relief.dart';

/// Carte, brique de base de tous les ecrans.
///
/// PLUS DE BORDURE : le relief separe. Le filet d'un pixel qui tenait lieu de
/// contour a disparu partout sauf a un endroit — la carte « highlighted »,
/// voir plus bas.
class HbaCard extends StatelessWidget {
  const HbaCard({
    required this.child,
    this.padding = const EdgeInsets.all(HbaSpacing.md),
    this.onTap,
    this.highlighted = false,
    this.elevation,
    super.key,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final VoidCallback? onTap;

  /// La carte sur laquelle le livreur doit agir maintenant.
  ///
  /// ELLE GARDE UN CONTOUR, ET C'EST LA SEULE EXCEPTION DU SYSTEME. Le relief
  /// dit « ceci est un bloc » ; il ne sait pas dire « celui-ci plutot que les
  /// autres ». Monter d'un cran d'elevation aide, mais une elevation se juge
  /// par comparaison — donc mal, quand la carte est seule a l'ecran. Un
  /// contour orange se voit sans comparaison, et sous le soleil.
  final bool highlighted;

  /// Force une hauteur de relief. Par defaut : normale, ou haute si la carte
  /// est mise en avant.
  final HbaElevation? elevation;

  @override
  Widget build(BuildContext context) {
    final hauteur = elevation ??
        (highlighted ? HbaElevation.haute : HbaElevation.normale);

    final corps = Padding(padding: padding, child: child);

    if (onTap == null) {
      return _contour(
        HbaRelief(elevation: hauteur, child: corps),
      );
    }

    return _contour(
      HbaPressable(
        onTap: onTap,
        elevation: hauteur,
        child: corps,
      ),
    );
  }

  Widget _contour(Widget enfant) {
    if (!highlighted) return enfant;

    // LE CONTOUR SE POSE PAR-DESSUS, PAS DANS LA DECORATION DU BLOC. Une
    // bordure declaree dans le meme BoxDecoration que l'ombre se fait rogner
    // par l'animation de pression, et le contour clignoterait a chaque appui.
    return Stack(
      children: [
        enfant,
        Positioned.fill(
          child: IgnorePointer(
            child: DecoratedBox(
              decoration: BoxDecoration(
                borderRadius: BorderRadius.circular(HbaRadius.card),
                border: Border.all(color: HbaColors.primaryInk, width: 1.6),
              ),
            ),
          ),
        ),
      ],
    );
  }
}
