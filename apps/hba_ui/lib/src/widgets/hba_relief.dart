import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../tokens.dart';

/// Un bloc EXTRUDE du fond.
///
/// C'est la brique de tout le style : une surface un peu plus claire que le
/// fond, une lumiere en haut a gauche, une ombre en bas a droite. Rien
/// d'autre. La bordure a disparu — c'est le relief qui separe maintenant.
///
/// IL NE SE PRESSE PAS. Un bloc en relief qui reagit au doigt est un bouton,
/// et un bouton a son propre widget. Melanger les deux produit des ecrans ou
/// tout a l'air cliquable et ou plus rien ne l'est vraiment.
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
          boxShadow: HbaOmbres.relief(elevation),
        ),
        child: child,
      );
}

/// Un bloc ENFONCE dans le fond.
///
/// L'INVERSE EXACT DU RELIEF, ET C'EST TOUT L'INTERET : la meme lumiere, la
/// meme ombre, de l'autre cote de la surface. C'est ce qui rend le style
/// lisible — ce qui depasse s'appuie, ce qui est creux se remplit. Un champ de
/// saisie, une pastille d'etat, un onglet deja choisi : tous creux.
///
/// FLUTTER N'A PAS D'OMBRE INTERIEURE, contrairement au CSS. On la peint donc
/// a la main : voir [_CreuxPeintre]. L'alternative repandue — un degrade
/// diagonal du sombre au clair — donne un resultat plat des que le bloc
/// s'allonge, parce qu'un degrade suit la diagonale du bloc quand une ombre,
/// elle, longe ses bords.
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
  Widget build(BuildContext context) => CustomPaint(
        painter: _CreuxPeintre(radius: radius, fond: color),

        // LE FLOU SE PAIE, ET DEUX INDICATIONS LE RENDENT GRATUIT DANS UNE
        // LISTE. « isComplex » autorise Flutter a garder l'image tramee de ce
        // dessin en cache ; « willChange: false » lui promet qu'elle ne
        // bougera pas. Sans les deux, chaque pastille d'une liste qui defile
        // refait son flou a chaque image — un cout invisible sur un telephone
        // de developpeur, tres visible sur celui d'un livreur.
        isComplex: true,
        willChange: false,
        child: Padding(padding: padding, child: child),
      );
}

/// Peint un fond creuse : la surface, puis deux ombres interieures opposees.
///
/// COMMENT ON PEINT UNE OMBRE VERS L'INTERIEUR. On ne peut pas flouter « le
/// vide » a l'interieur d'une forme. On dessine donc son COMPLEMENT — un
/// rectangle tres large troue de la forme, en regle de remplissage paire-impaire
/// — decale et floute, en limitant le dessin a la forme elle-meme. Ce qui
/// deborde a l'interieur du trou est exactement l'ombre portee par le bord.
class _CreuxPeintre extends CustomPainter {
  const _CreuxPeintre({required this.radius, required this.fond});

  final double radius;
  final Color fond;

  @override
  void paint(Canvas canvas, Size size) {
    // LE RAYON EST BORNE A LA MOITIE DU PLUS PETIT COTE. HbaRadius.chip vaut
    // 999 pour obtenir une pilule ; laisse tel quel, il depasse la forme, et
    // si le moteur de rendu le ramene lui-meme a l'echelle, le decalage de
    // l'ombre interieure, lui, ne suit pas.
    final rayon = math.min(radius, size.shortestSide / 2);

    final forme = RRect.fromRectAndRadius(
      Offset.zero & size,
      Radius.circular(rayon),
    );

    canvas.drawRRect(forme, Paint()..color = fond);

    // LE COMPLEMENT DE LA FORME. Le rectangle deborde largement pour que
    // l'ombre ait de la matiere a projeter depuis tous les cotes, meme apres
    // un decalage et un flou.
    final marge = rayon + HbaOmbres.flouCreux * 4;
    final complement = Path()
      ..addRect(Rect.fromLTRB(-marge, -marge, size.width + marge, size.height + marge))
      ..addRRect(forme)
      ..fillType = PathFillType.evenOdd;

    final flou = MaskFilter.blur(BlurStyle.normal, HbaOmbres.flouCreux);
    final decalage = HbaOmbres.decalageCreux;

    canvas.save();
    canvas.clipRRect(forme);

    // L'ombre, poussee depuis le haut a gauche : la lumiere vient de la, donc
    // c'est le bord oppose au soleil qui s'assombrit a l'interieur.
    canvas.save();
    canvas.translate(decalage.dx, decalage.dy);
    canvas.drawPath(
      complement,
      Paint()
        ..color = HbaColors.ombre
        ..maskFilter = flou,
    );
    canvas.restore();

    // La lumiere, poussee depuis le bas a droite.
    canvas.save();
    canvas.translate(-decalage.dx, -decalage.dy);
    canvas.drawPath(
      complement,
      Paint()
        ..color = HbaColors.lumiere
        ..maskFilter = flou,
    );
    canvas.restore();

    canvas.restore();
  }

  @override
  bool shouldRepaint(_CreuxPeintre ancien) =>
      ancien.radius != radius || ancien.fond != fond;
}

/// Un bloc qui s'enfonce quand on le presse.
///
/// C'EST L'AFFORDANCE QUI SAUVE LE NEUMORPHISME. Le reproche fait a ce style —
/// on ne sait plus ce qui est cliquable — tombe des lors que presser produit
/// un mouvement franc : le bloc passe du relief au creux, et remonte en le
/// relachant. Aucune couleur ne change, et c'est justement ce qui permet de
/// garder les couleurs pour dire autre chose.
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

  /// Null desactive le bloc : il reste en relief, sans reagir.
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

        // L'OMBRE DISPARAIT PENDANT LA PRESSION plutot que de s'inverser.
        // L'inverser demanderait de repeindre une ombre interieure a chaque
        // image de l'animation ; la retirer donne le meme signal — le bloc
        // n'est plus decolle — pour un cout nul.
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
