import 'package:flutter/material.dart';

import '../tokens.dart';

/// Un bloc POSE sur le fond.
///
/// C'est la brique de tout le style. Elle a change de dessin le 2 octobre
/// 2026, pas de role : une surface BLANCHE sur le fond ivoire, un filet fin,
/// une ombre a peine marquee vers le bas.
///
/// LE NOM RESTE « RELIEF » ALORS QUE LE BLOC N'EST PLUS EXTRUDE, et c'est
/// delibere : une trentaine d'ecrans l'appellent. Renommer aurait produit un
/// diff de plusieurs centaines de lignes qui ne change rien a ce qui s'affiche,
/// et deux noms pour la meme chose pendant la transition.
///
/// CE QUE LE FILET APPORTE QUE L'OMBRE NE DONNAIT PAS. L'ancien style reposait
/// entierement sur une double ombre ; en plein soleil elle disparait, et les
/// blocs se confondaient avec le fond. Un filet d'un pixel ne depend d'aucune
/// lumiere.
///
/// IL NE SE PRESSE PAS. Un bloc qui reagit au doigt est un bouton, et un
/// bouton a son propre widget. Melanger les deux produit des ecrans ou tout a
/// l'air cliquable et ou plus rien ne l'est vraiment.
class HbaRelief extends StatelessWidget {
  const HbaRelief({
    required this.child,
    this.elevation = HbaElevation.normale,
    this.radius = HbaRadius.card,
    this.color = HbaColors.surface,
    this.padding = EdgeInsets.zero,
    super.key,
  });

  final Widget child;
  final HbaElevation elevation;
  final double radius;
  final Color color;
  final EdgeInsetsGeometry padding;

  @override
  Widget build(BuildContext context) => Container(
        padding: padding,
        decoration: BoxDecoration(
          color: color,
          borderRadius: BorderRadius.circular(radius),

          // PAS DE FILET SUR CE QUI FLOTTE. La feuille posee sur la carte
          // geographique et le bandeau du haut se detachent par leur ombre et
          // par le fond qu'ils couvrent ; un filet les cernerait comme des
          // etiquettes collees.
          border: elevation == HbaElevation.haute
              ? null
              : Border.all(color: HbaColors.border),
          boxShadow: HbaOmbres.relief(elevation),
        ),
        child: child,
      );
}

/// Un bloc EN RETRAIT : champ de saisie, pastille, onglet choisi.
///
/// IL NE CREUSE PLUS RIEN, ET LE NOM RESTE. Dans l'ancien style, c'etait une
/// ombre interieure peinte a la main — Flutter n'en offre pas, contrairement au
/// CSS. Les maquettes ne creusent rien : un champ est un aplat legerement plus
/// sourd que la surface, borde du meme filet que les cartes.
///
/// CE QUI SE PERD, ET CE QUE CA FAIT GAGNER. On perd la regle « ce qui depasse
/// s'appuie, ce qui est creux se remplit », qui etait jolie et couteuse : le
/// peintre floutait un chemin complementaire a chaque pastille d'une liste.
/// On gagne un dessin qui tient au soleil et une liste qui ne reflechit plus
/// rien a chaque image.
class HbaCreux extends StatelessWidget {
  const HbaCreux({
    required this.child,
    this.radius = HbaRadius.field,
    this.color = HbaColors.surfaceSunken,
    this.padding = EdgeInsets.zero,
    super.key,
  });

  final Widget child;
  final double radius;
  final Color color;
  final EdgeInsetsGeometry padding;

  @override
  Widget build(BuildContext context) => Container(
        padding: padding,
        decoration: BoxDecoration(
          color: color,
          borderRadius: BorderRadius.circular(radius),
          border: Border.all(color: HbaColors.border),
        ),
        child: child,
      );
}

/// Un bloc qui reagit au doigt.
///
/// CE QUI REMPLACE L'ENFONCEMENT. L'ancien style faisait passer le bloc du
/// relief au creux : un mouvement franc, qui repondait au reproche fait au
/// neumorphisme — on ne sait plus ce qui est cliquable. Sans relief, il faut
/// autre chose : la surface se teinte legerement et l'ombre s'efface. Le
/// signal reste immediat, et aucune couleur de sens n'est consommee pour le
/// produire.
///
/// LA CIBLE FAIT 48 dp MINIMUM, quelle que soit la taille dessinee.
class HbaPressable extends StatefulWidget {
  const HbaPressable({
    required this.child,
    required this.onTap,
    this.elevation = HbaElevation.normale,
    this.radius = HbaRadius.card,
    this.color = HbaColors.surface,
    this.padding = EdgeInsets.zero,
    this.semantique,
    this.alignment,
    super.key,
  });

  final Widget child;

  /// Null desactive le bloc : il reste pose, sans reagir.
  final VoidCallback? onTap;

  final HbaElevation elevation;
  final double radius;
  final Color color;
  final EdgeInsetsGeometry padding;
  final String? semantique;

  /// Alignement du contenu dans le bloc.
  ///
  /// ATTENTION, IL FAIT AUSSI GROSSIR LE BLOC. Un Container qui porte un
  /// alignement enveloppe son enfant dans un Align, lequel prend toute la
  /// largeur disponible : le bloc cesse alors d'epouser son contenu. C'est ce
  /// qu'on veut pour une barre pleine largeur, jamais pour un bouton compact —
  /// pour celui-la, il vaut mieux epaissir la marge interne jusqu'a atteindre
  /// les 48 dp de la cible tactile, ce qui centre le contenu sans rien
  /// etirer. Laisse nul, le contenu se pose en haut a gauche.
  final AlignmentGeometry? alignment;

  @override
  State<HbaPressable> createState() => _HbaPressableState();
}

class _HbaPressableState extends State<HbaPressable> {
  bool _presse = false;

  void _poser(bool presse) {
    if (widget.onTap == null || _presse == presse) return;
    setState(() => _presse = presse);
  }

  @override
  Widget build(BuildContext context) {
    final actif = widget.onTap != null;

    final corps = AnimatedContainer(
      duration: HbaDuration.fast,
      curve: Curves.easeOut,
      padding: widget.padding,
      alignment: widget.alignment,
      decoration: BoxDecoration(
        color: _presse ? HbaColors.surfaceSunken : widget.color,
        borderRadius: BorderRadius.circular(widget.radius),
        border: widget.elevation == HbaElevation.haute
            ? null
            : Border.all(color: HbaColors.border),

        // L'OMBRE DISPARAIT PENDANT LA PRESSION. Avec le fond teinte, cela
        // suffit a dire que le bloc est tenu ; la reconstruire inversee
        // couterait une repeinte a chaque image de l'animation.
        boxShadow: _presse ? const [] : HbaOmbres.relief(widget.elevation),
      ),
      child: widget.child,
    );

    return Semantics(
      button: actif,
      label: widget.semantique,
      child: GestureDetector(
        onTapDown: (_) => _poser(true),
        onTapUp: (_) => _poser(false),
        onTapCancel: () => _poser(false),
        onTap: widget.onTap,

        // OPAQUE : sans cela, les zones transparentes du bloc — les marges
        // internes — ne recoivent pas le doigt, et le livreur touche « a cote »
        // d'une carte qu'il vise pourtant.
        behavior: HitTestBehavior.opaque,
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: HbaSpacing.cible),
          child: corps,
        ),
      ),
    );
  }
}
