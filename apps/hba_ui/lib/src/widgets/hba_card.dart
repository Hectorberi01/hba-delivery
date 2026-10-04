import 'package:flutter/material.dart';

import '../tokens.dart';
import 'hba_relief.dart';

/// Carte, brique de base de tous les ecrans.
///
/// LE FILET EST REVENU PARTOUT le 2 octobre 2026 : il est porte par HbaRelief
/// et HbaPressable, donc cette classe n'a rien a en dire. Ce qu'elle garde en
/// propre, c'est la mise en avant.
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

  /// La carte choisie, ou celle sur laquelle il faut agir maintenant.
  ///
  /// CONTOUR TERRE CUITE ET FOND TEINTE, comme dans les maquettes : c'est ce
  /// qui dit « Petit » parmi trois tailles de colis, ou « Tricycle » parmi
  /// trois vehicules. Le filet seul suffisait quand toutes les autres cartes
  /// etaient blanches sur ivoire ; depuis que le filet est partout, il ne
  /// distingue plus assez — d'ou l'aplat.
  ///
  /// UNE ELEVATION NE SAIT PAS DIRE « CELUI-CI PLUTOT QUE LES AUTRES » : elle
  /// se juge par comparaison, donc mal quand la carte est seule a l'ecran. Un
  /// contour colore se voit sans comparaison, et sous le soleil.
  final bool highlighted;

  /// Force une hauteur de relief. Par defaut : normale, ou haute si la carte
  /// est mise en avant.
  final HbaElevation? elevation;

  @override
  Widget build(BuildContext context) {
    final hauteur = elevation ??
        (highlighted ? HbaElevation.haute : HbaElevation.normale);

    final corps = Padding(padding: padding, child: child);
    final fond = highlighted ? HbaColors.primarySoft : HbaColors.surface;

    if (onTap == null) {
      return _contour(
        HbaRelief(elevation: hauteur, color: fond, child: corps),
      );
    }

    return _contour(
      HbaPressable(
        onTap: onTap,
        elevation: hauteur,
        color: fond,
        child: corps,
      ),
    );
  }

  Widget _contour(Widget enfant) {
    if (!highlighted) return enfant;

    // LE CONTOUR SE POSE PAR-DESSUS, PAS DANS LA DECORATION DU BLOC. Une
    // bordure declaree dans le meme BoxDecoration que l'ombre se fait rogner
    // par l'animation de pression, et le contour clignoterait a chaque appui.
    //
    // « passthrough » ET NON LE DEFAUT, CORRIGE LE 4 OCTOBRE 2026. Un Stack
    // donne des contraintes LACHES a ses enfants non positionnes : le bloc
    // colore se retrecissait donc a la largeur de son contenu, pendant que le
    // filet, lui, remplissait la carte. Une carte mise en avant dans une rangee
    // montrait un aplat vert sur sa moitie gauche et du blanc a droite — c'est
    // ce qu'on voyait sur les deux tuiles de l'accueil. « passthrough » fait
    // suivre au bloc les contraintes recues par la carte, donc la largeur
    // imposee par la rangee.
    //
    // CELA NE CHANGE RIEN LA OU LA CARTE EST LIBRE : contraintes laches en
    // entree, contraintes laches en sortie, et le bloc se dimensionne sur son
    // contenu comme avant.
    return Stack(
      fit: StackFit.passthrough,
      children: [
        enfant,
        Positioned.fill(
          child: IgnorePointer(
            child: DecoratedBox(
              decoration: BoxDecoration(
                borderRadius: BorderRadius.circular(HbaRadius.card),
                border: Border.all(color: HbaColors.primary, width: 1.6),
              ),
            ),
          ),
        ),
      ],
    );
  }
}
