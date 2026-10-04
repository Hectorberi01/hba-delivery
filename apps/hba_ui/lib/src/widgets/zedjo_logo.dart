import 'package:flutter/widgets.dart';

import '../tokens.dart';

/// La marque ZEDJO, dessinee et non importee.
///
/// EN CODE PLUTOT QU'EN IMAGE, ET CE N'EST PAS DU ZELE. Un logo d'application
/// se montre a huit tailles differentes — 28 points dans une barre, 96 sur
/// l'accueil, 120 sur l'ecran de bienvenue — et une image matricielle est
/// floue partout sauf a la taille ou elle a ete exportee. Un dessin vectoriel
/// est net a toutes, et il suit la palette : le jour ou le vert bouge, le logo
/// bouge avec lui au lieu d'etre le seul endroit qui garde l'ancienne couleur.
///
/// CE QU'IL DESSINE : un carre arrondi en degrade vert, une epingle de carte
/// blanche posee dessus, et un Z vert dans la tete de l'epingle. L'epingle dit
/// le lieu, le Z dit le nom, et le degrade evite l'aplat qui fait « gabarit ».
///
/// SANS PLAQUE, C'EST L'EPINGLE QUI PORTE LE VERT, et le Z devient blanc. Les
/// maquettes du 4 octobre 2026 posent la marque ainsi en haut de l'accueil :
/// une epingle nue par-dessus la carte geographique. Le carre arrondi y
/// ajoutait un deuxieme rectangle a cote des deux ronds blancs de la barre,
/// et le logo se lisait comme un troisieme bouton.
class ZedjoLogo extends StatelessWidget {
  const ZedjoLogo({this.taille = 56, this.surPlaque = true, super.key});

  final double taille;

  /// Le carre arrondi derriere l'epingle. Il isole la marque d'un fond charge
  /// — un ecran de bienvenue, une icone — et il gene sur une carte, ou
  /// l'epingle nue dit deja tout.
  final bool surPlaque;

  @override
  Widget build(BuildContext context) => SizedBox(
        width: taille,
        height: taille,
        child: CustomPaint(
          painter: _PeintreDuLogo(taille: taille, surPlaque: surPlaque),
        ),
      );
}

class _PeintreDuLogo extends CustomPainter {
  const _PeintreDuLogo({required this.taille, required this.surPlaque});

  final double taille;
  final bool surPlaque;

  @override
  void paint(Canvas canvas, Size size) {
    final cote = size.shortestSide;
    final rect = Rect.fromLTWH(0, 0, cote, cote);

    final degrade = const LinearGradient(
      begin: Alignment.topLeft,
      end: Alignment.bottomRight,
      colors: HbaColors.balanceGradient,
    ).createShader(rect);

    if (surPlaque) {
      // LE RAYON SUIT LA TAILLE. Un rayon fixe donne un carre presque droit en
      // grand et un rond en petit : c'est la proportion qui fait la forme.
      canvas.drawRRect(
        RRect.fromRectAndRadius(rect, Radius.circular(cote * 0.28)),
        Paint()..shader = degrade,
      );
    }

    // L'EPINGLE : une tete ronde et une pointe. Un seul chemin, rempli par la
    // regle non nulle — les deux formes se fondent sans avoir a les unir.
    //
    // SANS PLAQUE ELLE OCCUPE TOUTE LA BOITE. Gardee a la taille qu'elle a sur
    // le carre, elle flotterait au milieu d'un vide invisible et paraitrait
    // deux fois plus petite que le nom pose a cote.
    final centre = Offset(cote * 0.5, cote * (surPlaque ? 0.42 : 0.38));
    final rayon = cote * (surPlaque ? 0.21 : 0.30);
    final pointe = Offset(cote * 0.5, cote * (surPlaque ? 0.80 : 0.96));

    final epingle = Path()
      ..addOval(Rect.fromCircle(center: centre, radius: rayon))
      ..moveTo(centre.dx - rayon * 0.74, centre.dy + rayon * 0.68)
      ..lineTo(pointe.dx, pointe.dy)
      ..lineTo(centre.dx + rayon * 0.74, centre.dy + rayon * 0.68)
      ..close();

    canvas.drawPath(
      epingle,
      surPlaque
          ? (Paint()..color = HbaColors.surface)
          : (Paint()..shader = degrade),
    );

    // LE Z, DESSINE AU TRAIT ET NON ECRIT. Une lettre de police changerait de
    // dessin d'un telephone a l'autre — et a cette taille, deux dessins de Z
    // ne se ressemblent pas du tout.
    final l = rayon * 0.92;
    final hautGauche = Offset(centre.dx - l / 2, centre.dy - l / 2.6);
    final hautDroit = Offset(centre.dx + l / 2, centre.dy - l / 2.6);
    final basGauche = Offset(centre.dx - l / 2, centre.dy + l / 2.6);
    final basDroit = Offset(centre.dx + l / 2, centre.dy + l / 2.6);

    canvas.drawPath(
      Path()
        ..moveTo(hautGauche.dx, hautGauche.dy)
        ..lineTo(hautDroit.dx, hautDroit.dy)
        ..lineTo(basGauche.dx, basGauche.dy)
        ..lineTo(basDroit.dx, basDroit.dy),
      Paint()
        ..color = surPlaque ? HbaColors.primaryDeep : HbaColors.surface
        ..style = PaintingStyle.stroke
        ..strokeWidth = rayon * 0.262
        ..strokeJoin = StrokeJoin.miter
        ..strokeCap = StrokeCap.square,
    );
  }

  @override
  bool shouldRepaint(_PeintreDuLogo ancien) =>
      ancien.taille != taille || ancien.surPlaque != surPlaque;
}

/// Le logo, le nom et l'accroche : ce qu'on pose en haut d'un ecran d'accueil.
///
/// L'ACCROCHE EST FACULTATIVE parce qu'elle ne tient pas partout. Sous 320
/// points de large, « Bougeons ensemble, plus loin » passe a la ligne et
/// deséquilibre le bloc ; l'appelant decide.
class ZedjoWordmark extends StatelessWidget {
  const ZedjoWordmark({
    this.taille = 48,
    this.avecAccroche = true,
    this.encre = HbaColors.ink,
    this.surPlaque = true,
    super.key,
  });

  final double taille;
  final bool avecAccroche;
  final Color encre;

  /// Voir [ZedjoLogo.surPlaque].
  final bool surPlaque;

  @override
  Widget build(BuildContext context) => Row(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          ZedjoLogo(taille: taille, surPlaque: surPlaque),
          SizedBox(width: taille * 0.26),
          Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'ZEDJO',
                style: TextStyle(
                  fontSize: taille * 0.62,
                  height: 1.05,
                  fontWeight: FontWeight.w800,
                  letterSpacing: taille * 0.012,
                  color: encre,
                ),
              ),
              if (avecAccroche) ...[
                SizedBox(height: taille * 0.04),
                Text(
                  'Livraison & Transport',
                  style: TextStyle(
                    fontSize: taille * 0.2,
                    height: 1.2,
                    fontWeight: FontWeight.w500,
                    color: encre == HbaColors.surface
                        ? HbaColors.surface
                        : HbaColors.inkMuted,
                  ),
                ),
              ],
            ],
          ),
        ],
      );
}
