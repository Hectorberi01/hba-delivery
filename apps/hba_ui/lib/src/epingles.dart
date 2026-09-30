import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart' show rootBundle;
import 'package:google_maps_flutter/google_maps_flutter.dart';

/// Les epingles de la carte, DESSINEES PLUTOT QU'EMPRUNTEES A GOOGLE.
///
/// POURQUOI NE PAS GARDER defaultMarkerWithHue. Les ballons de Google n'ont que
/// dix teintes possibles, toutes saturees, et aucune ne fait partie de notre
/// palette : sur une carte volontairement sourde, trois ballons violet, vert et
/// orange vif deviennent les objets les plus criards de l'ecran — et ils se
/// ressemblent tous les trois de loin, alors qu'ils disent trois choses tres
/// differentes. Dessines, ils portent la couleur du systeme ET une forme
/// distincte : un disque pour un acteur, une goutte pour un point de la course.
///
/// LE DISQUE ET LA GOUTTE NE SE CONFONDENT PAS AU SOLEIL. C'est la forme qui
/// les separe, pas la teinte : un ecran lave par la lumiere garde les contours
/// bien apres avoir perdu les nuances.
///
/// DANS LE SOCLE DEPUIS LE 30 SEPTEMBRE 2026, et pas pour le rangement. Ce
/// fichier vivait dans l'application livreur, ou la carte du client ne pouvait
/// pas l'atteindre : elle en etait restee aux ballons de Google. Les deux
/// cartes disent maintenant la meme chose de la meme facon, et le client
/// apprend le meme code que le livreur — orange, on va chercher ; vert, on
/// arrive.
///
/// LES TABLES DE VEHICULES SONT ICI, ET C'EST TOUT L'INTERET DU DEPLACEMENT.
/// Elles ont d'abord ete laissees aux applications, au nom d'un socle qui ne
/// connaitrait que des formes et des couleurs. C'etait une erreur : le client
/// et le livreur doivent montrer LE MEME dessin pour le meme type, sinon la
/// carte du client apprend un code que celle du livreur contredit. Deux tables
/// finiraient par diverger — au premier type de vehicule ajoute d'un seul cote.
///
/// UNE SEULE TABLE, DONC, ET UN SEUL ENDROIT A CORRIGER quand un vehicule
/// s'ajoute. Ce qui reste aux applications, c'est ce qu'elles FONT du dessin :
/// le livreur se montre en ligne ou en pause, le client ne montre que des
/// livreurs disponibles.
abstract final class Epingles {
  /// UN CACHE PAR APPARENCE. Rasteriser coute quelques millisecondes, et la
  /// carte reconstruit ses marqueurs a chaque changement d'etat — sans cache,
  /// chaque battement de position redessinerait les trois epingles.
  static final Map<String, BitmapDescriptor> _cache = {};

  /// L'image du vehicule, ou null quand il n'y en a pas.
  ///
  /// [type] EST LE TYPE BRUT RENDU PAR LE SERVEUR : « Motorcycle », ou son
  /// entier de contrat sous forme de texte. Les deux sont acceptes parce que
  /// les deux circulent — la passerelle cliente rend le nom de l'enumere, et
  /// d'anciens enregistrements portent encore l'entier. Un type inconnu rend
  /// null au lieu de choisir : voir plus bas.
  ///
  /// LE CHEMIN EST PREFIXE « packages/hba_ui/ », ET IL DOIT L'ETRE. Un asset
  /// declare par un paquet ne s'adresse pas autrement depuis l'application qui
  /// en depend ; sans ce prefixe, le chargement echoue a l'execution et non a
  /// la compilation, donc en production.
  ///
  /// LA CAMIONNETTE N'A PAS D'IMAGE, et ce n'est pas un oubli a combler en
  /// douce : tant qu'elle n'en a pas, elle retombe sur le disque dessine. Lui
  /// donner la voiture ferait afficher un vehicule que personne n'a declare.
  static String? fichierDuVehicule(String? type) => switch (type) {
        'Motorcycle' || '1' => 'packages/hba_ui/assets/vehicules/moto.png',
        'Car' || '2' => 'packages/hba_ui/assets/vehicules/voiture.png',
        'Bicycle' || '4' => 'packages/hba_ui/assets/vehicules/velo.png',
        'Tricycle' || '5' => 'packages/hba_ui/assets/vehicules/tricycle.png',
        _ => null,
      };

  /// Le pictogramme du vehicule, pour le disque quand l'image manque.
  ///
  /// IL COUVRE PLUS DE CAS QUE LES IMAGES, VOLONTAIREMENT. La camionnette n'a
  /// pas de rendu en relief mais a un glyphe : c'est ce qui permet a une
  /// camionnette d'apparaitre comme une camionnette sur le disque, au lieu de
  /// se confondre avec un vehicule non declare.
  ///
  /// NULL RESTE NULL, ET LE DISQUE RESTE NU. Un type que le serveur ne nomme
  /// pas — pas encore declare, ou plus recent que cette version de
  /// l'application — ne recoit pas de moto par defaut. Le disque nu dit « on
  /// ne sait pas » ; une moto par defaut dirait « c'est une moto », ce qui est
  /// faux une fois sur deux et invisible a la relecture.
  static IconData? iconeDuVehicule(String? type) => switch (type) {
        'Motorcycle' || '1' => Icons.two_wheeler,
        'Car' || '2' => Icons.directions_car,
        'Van' || '3' => Icons.local_shipping,
        'Bicycle' || '4' => Icons.pedal_bike,
        'Tricycle' || '5' => Icons.electric_rickshaw,
        _ => null,
      };

  /// Le livreur : un disque plein, cercle de blanc.
  ///
  /// AVEC SON VEHICULE DEDANS QUAND ON LE CONNAIT. Le pictogramme n'est pas
  /// decoratif : c'est ce qui permet a l'exploitation, plus tard, de lire une
  /// carte pleine de livreurs sans cliquer sur chacun — et au livreur de se
  /// reconnaitre du premier coup d'oeil. Le disque grossit alors de 30 a 40 px
  /// pour que le dessin reste lisible : un pictogramme de huit pixels sur un
  /// telephone en plein soleil n'est plus qu'une tache.
  ///
  /// SANS VEHICULE DECLARE, LE DISQUE RESTE NU ET PETIT. Mettre une icone par
  /// defaut — une moto, parce que c'est le cas le plus courant — afficherait
  /// une information que personne n'a saisie.
  static Future<BitmapDescriptor?> disque({
    required Color couleur,
    required double ratio,
    IconData? vehicule,
  }) {
    final avecIcone = vehicule != null;
    final cote = avecIcone ? 40.0 : 30.0;
    final rayonBlanc = avecIcone ? 16.0 : 11.0;
    final rayonPlein = avecIcone ? 13.0 : 8.0;

    return _dessiner(
      cle: 'disque-${couleur.hashCode}-${vehicule?.codePoint ?? 0}-$ratio',
      largeur: cote,
      hauteur: cote,
      ratio: ratio,
      peindre: (canvas, taille) {
        final centre = Offset(taille.width / 2, taille.height / 2);

        _ombre(canvas, centre.translate(0, 1.5), rayonBlanc);
        canvas.drawCircle(centre, rayonBlanc, Paint()..color = Colors.white);
        canvas.drawCircle(centre, rayonPlein, Paint()..color = couleur);

        if (vehicule != null) _glyphe(canvas, centre, vehicule, 16);
      },
    );
  }

  /// Les images deja decodees. Decoder coute des millisecondes, et la carte
  /// redessine ses marqueurs a chaque changement d'etat.
  static final Map<String, ui.Image> _images = {};

  static Future<ui.Image?> _charger(String chemin) async {
    final connue = _images[chemin];
    if (connue != null) return connue;

    try {
      final octets = await rootBundle.load(chemin);
      final codec = await ui.instantiateImageCodec(octets.buffer.asUint8List());
      final frame = await codec.getNextFrame();

      _images[chemin] = frame.image;
      return frame.image;
    } on Object {
      // UN ASSET MANQUANT N'EST PAS UNE PANNE D'ECRAN : l'appelant retombe sur
      // le disque dessine, qui n'a besoin de rien.
      return null;
    }
  }

  /// Le livreur, en relief : le vehicule seul, sans disque ni anneau.
  ///
  /// SANS CONTENANT, L'OMBRE N'EST PLUS UN ORNEMENT — C'EST ELLE QUI DETACHE.
  /// L'anneau blanc faisait ce travail : il separait l'epingle des traits de la
  /// carte et des noms de rue. Retire, un vehicule sombre se fond dans une voie
  /// blanche et une carrosserie claire disparait sur un fond clair. L'ombre
  /// portee est ce qui reste pour tenir le contour ; sans elle, la voiture
  /// s'efface (verifie en maquette a 52 px).
  ///
  /// L'ETAT PASSE DANS L'IMAGE, PUISQUE LA COULEUR N'A PLUS DE SUPPORT. Hors
  /// ligne, le vehicule est desature, eclairci et efface a un peu plus de la
  /// moitie. C'est la seule chose qui distingue encore les deux etats sur la
  /// carte — et elle reste lisible au soleil, parce qu'elle joue sur le
  /// contraste et non sur la teinte.
  ///
  /// LE CHEMIN ARRIVE DU DEHORS, et « fichierDuVehicule » le donne. Les deux
  /// sont separes parce qu'un appelant peut vouloir dessiner une image qui ne
  /// vient pas de cette table — une marque partenaire, un jour.
  ///
  /// CINQUANTE-DEUX PIXELS, ET NON QUARANTE. Un disque de quarante portait un
  /// pictogramme plat, qui ne perd rien en retrecissant ; un rendu en relief,
  /// si. A 52 les quatre vehicules se reconnaissent, a 40 ils commencent a se
  /// ressembler.
  static Future<BitmapDescriptor?> vehiculeEnRelief({
    required String chemin,
    required double ratio,
    required bool enLigne,
  }) async {
    final image = await _charger(chemin);
    if (image == null) return null;

    const cote = 52.0;
    const marge = 5.0;

    return _dessiner(
      cle: 'relief-$chemin-$enLigne-$ratio',
      largeur: cote + marge * 2,
      hauteur: cote + marge * 2,
      ratio: ratio,
      peindre: (canvas, taille) {
        // CONSTANT : « cote » et « marge » le sont, donc ce rectangle aussi.
        // Il est calcule une fois a la compilation plutot qu'a chaque dessin.
        const cible = Rect.fromLTWH(marge, marge, cote, cote);
        final source = Rect.fromLTWH(
          0,
          0,
          image.width.toDouble(),
          image.height.toDouble(),
        );

        // L'OMBRE EST LA SILHOUETTE DE L'IMAGE, FLOUTEE. On ne peut pas poser
        // un MaskFilter sur une image : on la peint donc en une seule couleur
        // — srcIn garde la forme et jette les pixels — dans un calque que le
        // flou traverse.
        canvas.saveLayer(
          null,
          Paint()
            ..imageFilter = ui.ImageFilter.blur(sigmaX: 2.5, sigmaY: 2.5)
            ..colorFilter = const ColorFilter.mode(Color(0x6E141B2D), BlendMode.srcIn),
        );
        canvas.drawImageRect(image, source, cible.translate(0, 2), Paint());
        canvas.restore();

        if (enLigne) {
          canvas.drawImageRect(image, source, cible, Paint());
          return;
        }

        // HORS LIGNE : gris, eclairci, efface. Le calque porte l'opacite —
        // « Paint.color » ne teinte pas une image, il ne sert qu'aux formes.
        canvas.saveLayer(null, Paint()..color = const Color(0x8CFFFFFF));
        canvas.drawImageRect(
          image,
          source,
          cible,
          Paint()..colorFilter = const ColorFilter.matrix(<double>[
                // Desaturation par les coefficients de luminance, puis un
                // leger eclaircissement (le +28 de la derniere colonne).
                0.2126, 0.7152, 0.0722, 0, 28,
                0.2126, 0.7152, 0.0722, 0, 28,
                0.2126, 0.7152, 0.0722, 0, 28,
                0, 0, 0, 1, 0,
              ]),
        );
        canvas.restore();
      },
    );
  }

  /// Peint une icone Material sur un canvas.
  ///
  /// UNE ICONE EST UN CARACTERE DANS UNE FONTE, et c'est la seule facon de la
  /// dessiner hors d'un widget Icon : on la compose comme du texte.
  ///
  /// ATTENTION AU MODE RELEASE. Flutter elague les fontes d'icones : seules
  /// celles referencees par une IconData CONSTANTE survivent a la compilation.
  /// Les appelants passent donc « Icons.two_wheeler » et compagnie, qui sont
  /// des constantes ; construire une IconData a partir d'un code point lu dans
  /// une reponse du serveur ferait echouer la construction, avec un message
  /// qui parle de « --no-tree-shake-icons » et pas du tout de ce fichier.
  static void _glyphe(Canvas canvas, Offset centre, IconData icone, double taille) {
    final peintre = TextPainter(textDirection: TextDirection.ltr)
      ..text = TextSpan(
        text: String.fromCharCode(icone.codePoint),
        style: TextStyle(
          fontSize: taille,
          fontFamily: icone.fontFamily,
          package: icone.fontPackage,
          color: Colors.white,

          // SANS height: 1, LA FONTE AJOUTE SON INTERLIGNE au-dessus et en
          // dessous du glyphe, et le centrage tombe deux ou trois pixels trop
          // bas — assez pour que l'icone ait l'air de glisser hors du disque.
          height: 1,
        ),
      )
      ..layout();

    peintre.paint(
      canvas,
      Offset(centre.dx - peintre.width / 2, centre.dy - peintre.height / 2),
    );
  }

  /// Un point de la course : une goutte, cercle de blanc, avec un oeil clair.
  static Future<BitmapDescriptor?> goutte({
    required Color couleur,
    required double ratio,
  }) =>
      _dessiner(
        cle: 'goutte-${couleur.hashCode}-$ratio',
        largeur: 34,
        hauteur: 44,
        ratio: ratio,
        peindre: (canvas, taille) {
          const rayon = 13.0;
          final centre = Offset(taille.width / 2, rayon + 2);
          final pointe = Offset(taille.width / 2, taille.height - 2);

          // LA GOUTTE EST UN DISQUE PLUS UN TRIANGLE, pas une courbe de Bezier.
          // Le raccord se voit d'autant moins que la pointe part de l'INTERIEUR
          // du disque — les deux cotes du triangle rejoignent le cercle au lieu
          // de le tangenter, et l'oeil ne lit qu'une seule forme.
          final corps = Path()
            ..moveTo(centre.dx - rayon * 0.72, centre.dy + rayon * 0.62)
            ..lineTo(pointe.dx, pointe.dy)
            ..lineTo(centre.dx + rayon * 0.72, centre.dy + rayon * 0.62)
            ..close()
            ..addOval(Rect.fromCircle(center: centre, radius: rayon));

          _ombre(canvas, const Offset(0, 1.5), 0, chemin: corps);

          canvas.drawPath(corps, Paint()..color = Colors.white);

          final interieur = Path()
            ..moveTo(centre.dx - rayon * 0.52, centre.dy + rayon * 0.52)
            ..lineTo(pointe.dx, pointe.dy - 4)
            ..lineTo(centre.dx + rayon * 0.52, centre.dy + rayon * 0.52)
            ..close()
            ..addOval(Rect.fromCircle(center: centre, radius: rayon - 2.5));

          canvas.drawPath(interieur, Paint()..color = couleur);
          canvas.drawCircle(centre, 4.5, Paint()..color = Colors.white);
        },
      );

  /// L'ombre portee. La meme que celle des blocs flottants de l'application :
  /// une carte est un fond imprevisible, seule une ombre franche y tient.
  static void _ombre(Canvas canvas, Offset decalage, double rayon, {Path? chemin}) {
    final encre = Paint()
      ..color = const Color(0x33141B2D)
      ..maskFilter = const ui.MaskFilter.blur(ui.BlurStyle.normal, 3);

    if (chemin != null) {
      canvas.drawPath(chemin.shift(decalage), encre);
      return;
    }

    canvas.drawCircle(decalage, rayon, encre);
  }

  /// Rasterise un dessin en image, et l'emballe pour Google Maps.
  ///
  /// RIEN NE REMONTE SI CELA ECHOUE, ET C'EST VOULU. Une epingle est un
  /// ornement : si le rendu casse — moteur graphique indisponible pendant un
  /// test de widget, par exemple —, l'appelant retombe sur le ballon de Google
  /// et la carte reste utilisable. Faire echouer l'ecran entier pour une
  /// image de trente pixels serait hors de proportion.
  static Future<BitmapDescriptor?> _dessiner({
    required String cle,
    required double largeur,
    required double hauteur,
    required double ratio,
    required void Function(Canvas canvas, Size taille) peindre,
  }) async {
    final connu = _cache[cle];
    if (connu != null) return connu;

    try {
      final enregistreur = ui.PictureRecorder();
      final canvas = Canvas(enregistreur);

      // ON DESSINE EN COORDONNEES LOGIQUES ET ON MET A L'ECHELLE LE CANVAS.
      // L'alternative — multiplier chaque rayon par le ratio — se trompe une
      // fois sur deux et donne des epingles nettes sur un telephone et floues
      // sur un autre.
      canvas.scale(ratio);
      peindre(canvas, Size(largeur, hauteur));

      final image = await enregistreur.endRecording().toImage(
            (largeur * ratio).ceil(),
            (hauteur * ratio).ceil(),
          );

      final octets = await image.toByteData(format: ui.ImageByteFormat.png);
      image.dispose();

      if (octets == null) return null;

      final epingle = BitmapDescriptor.bytes(
        octets.buffer.asUint8List(),

        // SANS CE RATIO, L'EPINGLE S'AFFICHE A LA TAILLE EN PIXELS DE L'IMAGE :
        // trois fois trop grosse sur un ecran dense.
        imagePixelRatio: ratio,
      );

      _cache[cle] = epingle;
      return epingle;
    } on Object {
      return null;
    }
  }
}
