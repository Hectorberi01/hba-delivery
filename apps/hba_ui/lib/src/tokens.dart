import 'package:flutter/widgets.dart';

/// Jetons de design. Aucune couleur, aucun rayon, aucune espace ne doit etre
/// ecrit en dur ailleurs : une valeur qui n'est pas ici est une valeur qui
/// divergera entre les deux applications.
///
/// TROIS SYSTEMES, ET UN ARBITRE
///
/// Le style de ces applications melange trois choses qui ne veulent pas tout a
/// fait la meme chose :
///
///  - le NEUMORPHISME donne la matiere : les blocs sont extrudes du fond par
///    une double ombre, l'une claire en haut a gauche, l'autre sombre en bas a
///    droite, et ce qu'on presse s'enfonce au lieu de changer de couleur ;
///  - le SOFT UI garde du contraste entre le fond et les surfaces, la ou le
///    neumorphisme pur les confond ;
///  - MATERIAL DESIGN arbitre. C'est lui qui impose les seuils de contraste,
///    les cibles tactiles de 48 dp et le fait qu'une action principale reste
///    un aplat franc.
///
/// L'ARBITRAGE EST TRANCHE PAR LE CONTEXTE D'USAGE, PAS PAR LE GOUT. Cette
/// application se tient a bout de bras, en plein soleil de Cotonou, souvent
/// par quelqu'un qui vient de descendre d'une moto et qui a trente secondes.
/// Le neumorphisme pur — surfaces exactement de la couleur du fond, aucune
/// bordure, tout le relief porte par l'ombre — s'effondre exactement dans ces
/// conditions : sous une forte lumiere ambiante, l'ombre douce disparait la
/// premiere. Les surfaces gardent donc une valeur propre, plus claire que le
/// fond, et l'ombre ne fait qu'AJOUTER du relief a une separation qui tient
/// deja sans elle.
abstract final class HbaColors {
  /// Orange HBA, LA COULEUR D'IDENTITE. Reservee aux REMPLISSAGES : degrade de
  /// la carte de solde, pouce de la bascule, pastilles. Jamais en couleur de
  /// texte ni d'icone sur le fond clair — elle n'y tient que 2,6:1, sous le
  /// seuil de 3:1 que Material demande deja pour un simple element
  /// d'interface.
  static const primary = Color(0xFFF2690F);

  /// Orange de REMPLISSAGE SOUS DU TEXTE BLANC. C'est le fond du bouton
  /// principal : le blanc y tient 4,51:1.
  ///
  /// L'ANCIENNE VERSION ECRIVAIT EN BLANC SUR #F2690F, soit 3,09:1 pour un
  /// libelle de 16 px en gras — sous le seuil de 4,5:1, et sous celui de
  /// « grand texte » qui commence a 18,66 px en gras. Le defaut existait
  /// depuis le premier ecran et ne se voyait pas : en interieur, l'orange vif
  /// parait lisible. En plein soleil, il ne l'est plus.
  static const primaryDeep = Color(0xFFC4550C);

  /// Orange en TEXTE ou en ICONE. UNE SEULE VALEUR POUR LES TROIS SURFACES —
  /// le fond, une surface en relief, un creux — parce qu'un jeton qui change
  /// selon le support finit toujours par etre pose sur le mauvais. Elle tient
  /// 4,55:1 sur la plus sombre des trois (le creux), donc davantage sur les
  /// deux autres.
  static const primaryInk = Color(0xFFA6480A);

  /// Aplat tres clair, pour un fond de pastille ou un etat selectionne.
  static const primarySoft = Color(0xFFFCEADC);

  /// Encre : titres et texte courant. 14,6:1 sur le fond.
  static const ink = Color(0xFF141B2D);

  /// Texte secondaire. 5,2:1 sur le fond, 5,7:1 sur une surface.
  ///
  /// ASSOMBRI DEPUIS #6B7488, qui tenait 4,45:1 — juste sous le seuil, et le
  /// genre d'ecart qu'on ne voit jamais a l'oeil.
  static const inkMuted = Color(0xFF5A6273);

  /// JAMAIS POUR DU TEXTE QU'IL FAUT LIRE. Reserve aux invites de saisie, aux
  /// elements desactives et aux traits decoratifs — les seuls cas ou Material
  /// dispense du seuil de contraste, parce qu'un element desactive doit
  /// justement se lire comme indisponible.
  static const inkFaint = Color(0xFF7C8494);

  /// LE FOND EST LA MATIERE. Tout est extrude de lui : c'est sa valeur qui
  /// determine celle des deux ombres.
  static const background = Color(0xFFF1ECE7);

  /// La surface d'un bloc en relief. Plus claire que le fond, volontairement :
  /// c'est ce qui fait tenir la separation quand l'ombre s'efface au soleil.
  static const surface = Color(0xFFFAF7F4);

  /// Le fond d'un creux : un champ de saisie, une pastille, un puits. Plus
  /// sombre que le fond, comme si la matiere avait ete enfoncee.
  static const surfaceSunken = Color(0xFFE7E1DA);

  /// Filet. DEVENU RARE : le relief remplace la bordure presque partout. Il ne
  /// reste que pour separer des lignes dans une liste dense, ou l'ombre
  /// creerait un empilement de blocs illisible.
  static const border = Color(0xFFE2DBD4);

  /// L'ombre portee, en bas a droite. Le fond assombri, PAS DU NOIR TRANSPARENT :
  /// du noir a faible opacite sur un fond chaud vire au gris sale et eteint
  /// toute la chaleur de la palette.
  static const ombre = Color(0xFFCFC7BF);

  /// La lumiere, en haut a gauche. La source est en haut a gauche PARTOUT et
  /// ne bouge jamais : deux sources de lumiere differentes sur un meme ecran
  /// et le relief cesse d'etre lu comme du relief.
  static const lumiere = Color(0xFFFFFFFF);

  // LES TROIS COULEURS D'ETAT SUIVENT LA MEME REGLE QUE primaryInk : verifiees
  // contre le creux, la plus sombre des trois surfaces. Elles tiennent 4,5:1
  // dessus, 5,0:1 sur le fond et 5,5:1 sur une surface en relief.
  //
  // LES APLATS « SOFT », EUX, NE SE DISTINGUENT PAS DU FOND EN LUMINANCE — a
  // peine 1,01:1. C'est voulu : ce n'est pas leur valeur qui les detache mais
  // le creux qui les porte. Un aplat clair pose a plat sur cette matiere se
  // lirait comme une tache.
  static const success = Color(0xFF0B7442);
  static const successSoft = Color(0xFFE2F1E9);

  static const danger = Color(0xFFBB2F2A);
  static const dangerSoft = Color(0xFFFAE8E7);

  static const warning = Color(0xFF925600);
  static const warningSoft = Color(0xFFFBEFDC);

  /// Degrade de la carte de solde. Il porte l'identite de la marque, et c'est
  /// le seul endroit ou l'orange vif reste en grand aplat — le texte blanc y
  /// est en 34 px gras, donc largement au-dela du seuil de « grand texte ».
  static const balanceGradient = [Color(0xFFF2690F), Color(0xFFD03A2B)];
}

abstract final class HbaSpacing {
  static const xs = 4.0;
  static const sm = 8.0;
  static const md = 16.0;
  static const lg = 24.0;
  static const xl = 32.0;
  static const xxl = 48.0;

  /// Marge laterale de tous les ecrans.
  static const gutter = 20.0;

  /// CIBLE TACTILE MINIMALE DE MATERIAL. Tout ce qui se presse doit faire au
  /// moins cela de haut, meme quand le dessin parait plus petit : l'ecran se
  /// touche avec un pouce, parfois ganté.
  static const cible = 48.0;
}

abstract final class HbaRadius {
  static const card = 20.0;
  static const field = 16.0;
  static const button = 28.0;
  static const chip = 999.0;
}

abstract final class HbaDuration {
  static const fast = Duration(milliseconds: 150);
  static const normal = Duration(milliseconds: 250);
}

/// Les trois hauteurs du relief.
///
/// TROIS, PAS DIX. Une echelle d'elevation qui compte dix crans ne se
/// distingue plus a l'oeil et se choisit au hasard. Celle-ci dit trois choses
/// differentes : pose, saillant, et enfonce.
enum HbaElevation {
  /// A peine decolle du fond : une ligne de liste, une tuile secondaire.
  douce,

  /// Le cran normal d'une carte.
  normale,

  /// Ce sur quoi l'oeil doit tomber en premier : la carte d'action du moment.
  haute,
}

/// Les ombres, calculees a partir du fond.
abstract final class HbaOmbres {
  /// Le relief : lumiere en haut a gauche, ombre en bas a droite.
  static List<BoxShadow> relief(HbaElevation hauteur) => switch (hauteur) {
        HbaElevation.douce => const [
            BoxShadow(color: HbaColors.ombre, offset: Offset(2, 2), blurRadius: 5),
            BoxShadow(color: HbaColors.lumiere, offset: Offset(-2, -2), blurRadius: 5),
          ],
        HbaElevation.normale => const [
            BoxShadow(color: HbaColors.ombre, offset: Offset(5, 5), blurRadius: 12),
            BoxShadow(color: HbaColors.lumiere, offset: Offset(-4, -4), blurRadius: 10),
          ],
        HbaElevation.haute => const [
            BoxShadow(color: HbaColors.ombre, offset: Offset(8, 9), blurRadius: 20),
            BoxShadow(color: HbaColors.lumiere, offset: Offset(-6, -6), blurRadius: 14),
          ],
      };

  /// Decalage et flou de l'ombre INTERIEURE d'un creux, pour le peintre.
  static const decalageCreux = Offset(2.5, 2.5);
  static const flouCreux = 5.0;
}
