import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_ui/hba_ui.dart';

/// Les epingles de la carte, DESSINEES PLUTOT QU'EMPRUNTEES A GOOGLE.
///
/// POURQUOI NE PAS GARDER defaultMarkerWithHue. Les ballons de Google n'ont que
/// dix teintes possibles, toutes saturees, et aucune ne fait partie de notre
/// palette : sur une carte volontairement sourde, trois ballons violet, vert et
/// orange vif deviennent les objets les plus criards de l'ecran — et ils se
/// ressemblent tous les trois de loin, alors qu'ils disent trois choses tres
/// differentes. Dessines, ils portent la couleur du systeme ET une forme
/// distincte : un disque pour le livreur, une goutte pour un point de la course.
///
/// LE DISQUE ET LA GOUTTE NE SE CONFONDENT PAS AU SOLEIL. C'est la forme qui
/// les separe, pas la teinte : un ecran lave par la lumiere garde les contours
/// bien apres avoir perdu les nuances.
abstract final class Epingles {
  /// UN CACHE PAR APPARENCE. Rasteriser coute quelques millisecondes, et la
  /// carte reconstruit ses marqueurs a chaque changement d'etat — sans cache,
  /// chaque battement de position redessinerait les trois epingles.
  static final Map<String, BitmapDescriptor> _cache = {};

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

  /// Le pictogramme d'un type de vehicule, ou nul quand il n'est pas declare.
  ///
  /// TROIS TYPES, PARCE QUE LE DOMAINE N'EN CONNAIT QUE TROIS.
  /// `hba.common.v1.VehicleType` vaut UNSPECIFIED, MOTORCYCLE, CAR, VAN — il
  /// n'y a ni velo ni tricycle. Les ajouter n'est pas une affaire de dessin :
  /// ce type est la CLE DU TARIF dans Pricing (index zone + vehicule + date),
  /// il filtre les livreurs dans la recherche de Driver, et les pieces
  /// obligatoires du dossier — carte grise, plaque, permis — n'ont pas de sens
  /// pour un velo. Voir les points a trancher.
  ///
  /// LES DEUX LIGNES MANQUANTES SONT PRETES : velo -> Icons.pedal_bike,
  /// tricycle -> Icons.electric_rickshaw. Le jour ou le contrat les portera,
  /// c'est ici et dans Vehicule.libelle que cela se rajoute, nulle part
  /// ailleurs cote application.
  static IconData? iconeDuVehicule(String? type) => switch (type) {
        'Motorcycle' || '1' => Icons.two_wheeler,
        'Car' || '2' => Icons.directions_car,
        'Van' || '3' => Icons.local_shipping,
        _ => null,
      };

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

/// Les epingles de la carte, prechargees ensemble.
class JeuDEpingles {
  const JeuDEpingles({this.moiEnLigne, this.moiHorsLigne, this.collecte, this.livraison});

  final BitmapDescriptor? moiEnLigne;
  final BitmapDescriptor? moiHorsLigne;
  final BitmapDescriptor? collecte;
  final BitmapDescriptor? livraison;

  /// [vehicule] est le type brut rendu par le serveur — « Motorcycle », ou son
  /// entier. Nul ou inconnu : le disque reste nu.
  static Future<JeuDEpingles> charger(double ratio, {String? vehicule}) async {
    final icone = Epingles.iconeDuVehicule(vehicule);

    return JeuDEpingles(
      moiEnLigne: await Epingles.disque(
        couleur: HbaColors.success,
        ratio: ratio,
        vehicule: icone,
      ),

      // HORS LIGNE, LE VEHICULE RESTE DESSINE. Il ne dit pas « je travaille »,
      // il dit « avec quoi » — et cela ne change pas quand on se met en pause.
      // C'est la couleur, elle, qui porte l'etat.
      moiHorsLigne: await Epingles.disque(
        couleur: HbaColors.inkMuted,
        ratio: ratio,
        vehicule: icone,
      ),

        // LA COLLECTE PORTE L'ORANGE DE LA MARQUE, LA LIVRAISON LE VERT DE
        // L'ARRIVEE. C'est le meme code couleur que les deux lignes de la
        // carte de course, et il ne doit pas diverger : le livreur apprend
        // une fois « orange = ou je vais chercher ».
      collecte: await Epingles.goutte(couleur: HbaColors.primary, ratio: ratio),
      livraison: await Epingles.goutte(couleur: HbaColors.success, ratio: ratio),
    );
  }
}
