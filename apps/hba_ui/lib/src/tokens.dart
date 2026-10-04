import 'package:flutter/widgets.dart';

/// Jetons de design. Aucune couleur, aucun rayon, aucune espace ne doit etre
/// ecrit en dur ailleurs : une valeur qui n'est pas ici est une valeur qui
/// divergera entre les deux applications.
///
/// LA MARQUE A CHANGE LE 4 OCTOBRE 2026 : ZEDJO, ET LES NOMS N'ONT PAS BOUGE.
///
/// « ZEDJO — Livraison & Transport, Bougeons ensemble, plus loin » remplace
/// l'identite HBA cote applications : vert a la place de la terre cuite, fond
/// froid a la place de l'ivoire chaud. Les JETONS gardent leurs noms, donc les
/// quatre-vingts appels a « HbaColors.primary » des deux applications suivent
/// sans etre reecrits — c'est exactement ce qui avait permis le changement de
/// style du 2 octobre.
///
/// REPEINDRE LE SOCLE REPEINT LES DEUX APPLICATIONS, et c'est assume : ZEDJO
/// est la marque de la plateforme, pas celle du seul client. Garder le
/// terracotta cote livreur demanderait une seconde palette resolue par le
/// theme, donc de faire passer ces quatre-vingts appels par
/// « Theme.of(context) » — un refactor, pas un reglage.
///
/// LE STYLE A CHANGE LE 2 OCTOBRE 2026, LES NOMS NON.
///
/// Les applications etaient en NEUMORPHISME : chaque bloc extrude du fond par
/// une double ombre — claire en haut a gauche, sombre en bas a droite — et ce
/// qu'on pressait s'enfoncait. Les maquettes retenues sont ailleurs : surfaces
/// BLANCHES posees sur un fond ivoire, filet fin plutot qu'ombre, grands
/// rayons, et une terre cuite qui remplace l'orange vif.
///
/// AUCUN NOM DE JETON NI DE COMPOSANT N'A BOUGE, et c'est ce qui permet a tous
/// les ecrans de suivre sans etre reecrits : « HbaCard », « HbaCreux »,
/// « HbaRelief », « HbaOmbres.relief » existent toujours et disent la meme
/// chose — un bloc, un champ, une separation — avec un autre dessin. C'est
/// exactement ce qui avait ete fait pour la console le meme jour.
///
/// CE QUE LE NOUVEAU STYLE GAGNE EN PLEIN SOLEIL. L'ancien commentaire de ce
/// fichier disait le defaut du neumorphisme : sous une forte lumiere ambiante,
/// l'ombre douce disparait la premiere, et avec elle toute separation. Le
/// blanc sur ivoire ne depend d'aucune ombre : il tient par la valeur.
abstract final class HbaColors {
  /// Vert ZEDJO. IL PORTE DU TEXTE BLANC, et c'est ce qui l'a choisi : 5,03:1,
  /// au-dessus du seuil de 4,5:1.
  ///
  /// CE N'EST PAS LE VERT DE LA MAQUETTE, ET C'EST VOULU. Le vert vif des
  /// images — autour de #16A34A — ne tient que 3,2:1 sous du blanc : un libelle
  /// de bouton y devient illisible au soleil de Cotonou, qui est le seul
  /// eclairage qui compte ici. Le meme arbitrage avait ete fait pour l'orange
  /// #F2690F, ecarte au profit de la terre cuite pour la meme raison. Le vert
  /// vif reste parfaitement utilisable la ou il ne porte pas de texte : le
  /// logo, le halo du radar, une epingle.
  static const primary = Color(0xFF138039);

  /// Le meme, assombri : fond d'un bouton presse, et teinte que Material pose
  /// sous du blanc. 6,38:1.
  static const primaryDeep = Color(0xFF0F6B30);

  /// Vert en TEXTE ou en ICONE sur fond clair. Une seule valeur pour les trois
  /// surfaces — un jeton qui change selon le support finit toujours par etre
  /// pose sur le mauvais. 7,9:1 sur blanc.
  static const primaryInk = Color(0xFF0B5F2B);

  /// Aplat tres clair : fond d'une pastille, carte selectionnee, puce active.
  /// C'est lui qui dit « choisi » dans les maquettes, avec le filet vert.
  static const primarySoft = Color(0xFFE7F4EC);

  /// Encre : titres et texte courant. 14,65:1 sur le fond, 17,38:1 sur blanc.
  static const ink = Color(0xFF111A2C);

  /// Texte secondaire. 4,80:1 sur le fond, 5,70:1 sur une surface blanche.
  static const inkMuted = Color(0xFF5F6775);

  /// JAMAIS POUR DU TEXTE QU'IL FAUT LIRE. Invites de saisie, elements
  /// desactives, filets decoratifs — les seuls cas ou Material dispense du
  /// seuil, parce qu'un element desactive doit se lire comme indisponible.
  static const inkFaint = Color(0xFF8A9099);

  /// Le fond de l'application. IVOIRE CHAUD JUSQU'AU 4 OCTOBRE 2026, VERT
  /// TRES PALE DEPUIS : un fond chaud sous une marque verte fait virer le vert
  /// au kaki, et c'est le defaut que l'on voit le plus vite sur un telephone
  /// bon marche. C'est la couleur que l'on voit AUTOUR des cartes, et derriere
  /// la carte geographique.
  static const background = Color(0xFFF1F5F2);

  /// La surface d'une carte ou d'une feuille : BLANC FRANC. Dans l'ancien
  /// style elle etait a peine plus claire que le fond, puisque l'ombre faisait
  /// le travail ; ici c'est elle qui separe.
  static const surface = Color(0xFFFFFFFF);

  /// Le fond d'un champ de saisie ou d'une pastille neutre. Plus sombre que la
  /// surface, plus clair que le fond : un creux qui ne creuse plus rien, et
  /// qui se lit quand meme comme une zone a remplir.
  static const surfaceSunken = Color(0xFFEDF2EE);

  /// Filet. DEVENU LA REGLE, alors qu'il etait l'exception : c'est lui qui
  /// borde les cartes, les champs et les pastilles non selectionnees.
  static const border = Color(0xFFE2E8E4);

  /// L'ombre portee, desormais UNIQUE et vers le bas. Le fond assombri, PAS DU
  /// NOIR TRANSPARENT : du noir a faible opacite sur un fond chaud vire au
  /// gris sale et eteint toute la chaleur de la palette.
  static const ombre = Color(0xFFD5DED8);

  /// CONSERVEE POUR LES APPELANTS, ET PLUS UTILISEE PAR LE RELIEF. Elle etait
  /// la lumiere en haut a gauche du neumorphisme. La retirer casserait des
  /// ecrans pour un gain nul ; la laisser sans l'employer est honnete tant que
  /// le commentaire le dit.
  static const lumiere = Color(0xFFFFFFFF);

  // LES TROIS COULEURS D'ETAT SONT VERIFIEES CONTRE LE CHAMP, la plus sombre
  // des trois surfaces : elles y tiennent plus de 5:1, donc davantage sur le
  // fond et sur le blanc.
  static const success = Color(0xFF0B7442);
  static const successSoft = Color(0xFFE6F1EA);

  static const danger = Color(0xFFB3322B);
  static const dangerSoft = Color(0xFFF9E9E7);

  static const warning = Color(0xFF8A5600);
  static const warningSoft = Color(0xFFFAEFDF);

  /// Degrade de la carte de solde, dans l'application livreur. Les deux
  /// teintes suivent la terre cuite : le texte y est en 34 px gras, donc
  /// au-dela du seuil de « grand texte ».
  static const balanceGradient = [Color(0xFF17964A), Color(0xFF0C6230)];
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

/// LES RAYONS ONT GRANDI AVEC LE STYLE. Les maquettes posent des feuilles et
/// des cartes nettement plus arrondies que l'ancien dessin ; le bouton, lui,
/// est une gelule franche.
abstract final class HbaRadius {
  static const card = 22.0;
  static const field = 16.0;
  static const button = 30.0;
  static const chip = 999.0;

  /// La feuille qui monte par-dessus la carte geographique. Arrondie en haut
  /// seulement : c'est ce coin-la qui dit qu'elle glisse.
  static const feuille = 28.0;
}

abstract final class HbaDuration {
  static const fast = Duration(milliseconds: 150);
  static const normal = Duration(milliseconds: 250);
}

/// Les trois hauteurs du relief.
///
/// TROIS, PAS DIX. Une echelle d'elevation qui compte dix crans ne se
/// distingue plus a l'oeil et se choisit au hasard. Celle-ci dit trois choses
/// differentes : pose, saillant, et ce qui flotte au-dessus de la carte.
enum HbaElevation {
  /// A peine decolle du fond : une ligne de liste, une tuile secondaire.
  douce,

  /// Le cran normal d'une carte.
  normale,

  /// Ce sur quoi l'oeil doit tomber en premier, ou ce qui flotte au-dessus de
  /// la carte geographique : feuille du bas, bandeau du haut.
  haute,
}

/// Les ombres.
///
/// UNE SEULE, ET VERS LE BAS. Le neumorphisme en posait deux, opposees, pour
/// simuler une source de lumiere en haut a gauche. Le nouveau style ne simule
/// rien : l'ombre ne fait que decoller legerement une surface BLANCHE d'un
/// fond ivoire, et la separation tient deja sans elle.
///
/// ELLES RESTENT TRES DISCRETES. Dans les maquettes, seule la feuille posee
/// sur la carte porte une ombre franche ; les cartes d'une liste n'en ont
/// presque pas, c'est leur filet qui les detache.
abstract final class HbaOmbres {
  static List<BoxShadow> relief(HbaElevation hauteur) => switch (hauteur) {
        HbaElevation.douce => const [
            BoxShadow(color: HbaColors.ombre, offset: Offset(0, 1), blurRadius: 3),
          ],
        HbaElevation.normale => const [
            BoxShadow(color: HbaColors.ombre, offset: Offset(0, 3), blurRadius: 10),
          ],
        HbaElevation.haute => const [
            BoxShadow(color: HbaColors.ombre, offset: Offset(0, -2), blurRadius: 18),
            BoxShadow(color: HbaColors.ombre, offset: Offset(0, 8), blurRadius: 24),
          ],
      };

  /// CONSERVES POUR LE PEINTRE DU CREUX, qui ne s'en sert plus : le champ est
  /// desormais un aplat borde. Les valeurs restent pour qu'aucun appelant ne
  /// casse, et le peintre explique pourquoi il les ignore.
  static const decalageCreux = Offset(2.5, 2.5);
  static const flouCreux = 5.0;
}
