import 'dart:math' as maths;
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter_image_compress/flutter_image_compress.dart';
import 'package:hba_ui/hba_ui.dart';

/// Cadrer sa photo avant de l'envoyer.
///
/// POURQUOI CET ECRAN EXISTE. La photo finit dans un rond, affiche en
/// BoxFit.cover : d'une image qui n'est pas carree, le telephone garde le
/// CENTRE et jette le reste. Un visage un peu haut, un peu a gauche, et le
/// client se retrouve avec un portrait de son epaule. Il n'avait aucun moyen
/// d'y changer quoi que ce soit — la seule issue etait de recadrer dans une
/// autre application avant de revenir.
///
/// PAS DE PAQUET DE RECADRAGE, ET C'EST UN CHOIX. « image_cropper » aurait
/// fait le travail, au prix d'une activite Android a declarer, d'un pod iOS et
/// d'une interface qui ne ressemble a rien d'autre dans l'application. Ce qu'il
/// faut ici tient en un geste — deplacer, pincer — et le cercle montre
/// EXACTEMENT ce qui sera garde. Un recadreur generique montre un carre, puis
/// le rond arrive apres coup et surprend.
///
/// LE RESULTAT EST CARRE, et c'est ce qui rend l'affichage honnete. Un rond se
/// decoupe dans un carre sans rien perdre de plus ; envoyer un rectangle
/// reporterait le probleme au serveur, qui n'a aucune idee de ce qui compte
/// dans l'image.
class CadragePhoto extends StatefulWidget {
  const CadragePhoto({required this.image, super.key});

  /// L'image choisie, deja decodee.
  final ui.Image image;

  /// Cote de l'image rendue.
  ///
  /// MILLE PIXELS POUR UN ROND DE QUATRE-VINGT-DOUZE, et ce n'est pas du
  /// gaspillage : l'ecran d'un iPhone dessine trois pixels physiques par
  /// pixel logique, et la meme photo servira un jour dans une fiche plus
  /// grande. Au-dela, on paie des donnees pour du detail que personne ne voit.
  static const cote = 1000;

  /// Ouvre l'ecran et rend le JPEG cadre, ou null si le client renonce.
  static Future<Uint8List?> ouvrir(BuildContext context, Uint8List octets) async {
    final ui.Image image;

    try {
      image = await decodeImageFromList(octets);
    } on Object {
      return null;
    }

    if (!context.mounted) {
      image.dispose();
      return null;
    }

    try {
      // LE NAVIGATOR RACINE : les onglets ont chacun le leur, loge SOUS la
      // barre du bas. Sans cela, l'ecran de cadrage s'ouvrirait dans le cadre
      // de l'onglet, barre comprise, et le rond serait decentre.
      return await Navigator.of(context, rootNavigator: true).push<Uint8List>(
        MaterialPageRoute(builder: (_) => CadragePhoto(image: image)),
      );
    } finally {
      image.dispose();
    }
  }

  @override
  State<CadragePhoto> createState() => _CadragePhotoState();
}

/// La taille que prend [image] pour couvrir un carre de [cote].
///
/// C'EST BoxFit.cover, ECRIT UNE SEULE FOIS. RawImage l'applique a l'ecran, et
/// le rendu final doit refaire le meme calcul au pixel pres : s'ils divergent,
/// le fichier ne montre pas ce que le cercle montrait. Le maths.max final
/// n'absorbe que l'arrondi — une image large d'un millieme de pixel de moins
/// que le carre laisserait un lisere.
Size _taillePourCouvrir(ui.Image image, double cote) {
  final facteur = maths.max(cote / image.width, cote / image.height);

  return Size(
    maths.max(image.width * facteur, cote),
    maths.max(image.height * facteur, cote),
  );
}

class _CadragePhotoState extends State<CadragePhoto> {
  final _controleur = TransformationController();

  bool _rend = false;

  /// Cote du hublot a l'ecran, calcule au premier rendu.
  double _hublot = 0;

  @override
  void dispose() {
    _controleur.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.black,
        foregroundColor: Colors.white,
        title: const Text('Cadrer la photo'),
      ),
      body: SafeArea(
        child: Column(
          children: [
            Expanded(
              child: LayoutBuilder(
                builder: (contexte, contraintes) {
                  // LE HUBLOT EST CARRE ET TIENT DANS LES DEUX DIMENSIONS,
                  // avec une marge : sans elle, sur un ecran etroit, les bords
                  // du cercle touchent ceux de l'ecran et on ne voit plus ce
                  // qu'on ecarte.
                  final cote = maths.min(
                        contraintes.maxWidth,
                        contraintes.maxHeight,
                      ) -
                      HbaSpacing.gutter * 2;

                  _hublot = cote;

                  // LA PHOTO A L'ECHELLE OU ELLE COUVRE LE CARRE, ET CE QUI
                  // DEPASSE DE PART ET D'AUTRE : la seule chose qu'on ait le
                  // droit d'aller chercher en deplacant, puisque c'est la
                  // seule qui soit peinte.
                  final taille = _taillePourCouvrir(widget.image, cote);
                  final debord = EdgeInsets.symmetric(
                    horizontal: (taille.width - cote) / 2,
                    vertical: (taille.height - cote) / 2,
                  );

                  return Center(
                    child: SizedBox(
                      width: cote,
                      height: cote,
                      child: Stack(
                        fit: StackFit.expand,
                        children: [
                          // LE BLANC SOUS L'IMAGE DIT LA VERITE. Le rendu peint
                          // un fond blanc ; un ecran qui laisserait voir du
                          // noir la ou il n'y a pas d'image mentirait sur le
                          // resultat. La marge ci-dessous rend le cas
                          // impossible — ce fond est la pour qu'il le reste si
                          // elle change un jour.
                          const ColoredBox(color: Colors.white),

                          // LE CLIP EST SANS DANGER ICI : ce Stack n'entoure
                          // aucune vue native. La consigne iOS qui l'interdit
                          // ne vise que les vues du systeme, la carte Google
                          // en tete.
                          ClipRect(
                            child: InteractiveViewer(
                              transformationController: _controleur,
                              minScale: 1,
                              maxScale: 6,
                              // LA MARGE VAUT EXACTEMENT LE DEBORDEMENT DE
                              // L'IMAGE, ET PAS UN PIXEL DE PLUS. C'est elle
                              // qui interdit de sortir la photo du hublot :
                              // au-dela de ses bords il n'y a plus d'image, et
                              // ce que le client decouvrait alors etait du noir
                              // a l'ecran pour du blanc dans le fichier.
                              //
                              // CE QU'ON PERD : un point colle au bord de la
                              // photo ne peut plus etre amene au CENTRE du
                              // cercle, seulement contre son bord. C'est la
                              // contrainte de tous les recadreurs, et elle est
                              // preferable a un portrait a moitie vide.
                              boundaryMargin: debord,
                              clipBehavior: Clip.none,

                              // L'IMAGE EST PEINTE EN ENTIER, ET C'EST TOUT
                              // L'OBJET DE CET OverflowBox. « BoxFit.cover »
                              // ne fait PAS deborder l'image de sa boite :
                              // paintImage DECOUPE LA SOURCE et ne dessine
                              // que le carre central. Ce qui depasse n'etait
                              // donc jamais peint — deplacer ne decouvrait
                              // rien, juste le fond. En imposant a la boite
                              // la taille exacte de l'image couvrante, on
                              // peint la photo entiere ; le ClipRect au-dessus
                              // se charge de n'en montrer que le carre.
                              //
                              // BoxFit.fill NE DEFORME PAS ICI : la taille
                              // demandee est proportionnelle a la source, par
                              // construction de _taillePourCouvrir.
                              child: OverflowBox(
                                minWidth: taille.width,
                                maxWidth: taille.width,
                                minHeight: taille.height,
                                maxHeight: taille.height,
                                child: RawImage(
                                  image: widget.image,
                                  fit: BoxFit.fill,
                                  width: taille.width,
                                  height: taille.height,
                                ),
                              ),
                            ),
                          ),

                          // LE MASQUE PAR-DESSUS, QUI NE PREND AUCUN TOUCHER.
                          // IgnorePointer est ce qui laisse le geste atteindre
                          // l'image dessous ; sans lui le masque avalerait
                          // tout et rien ne bougerait.
                          const IgnorePointer(
                            child: _Hublot(),
                          ),
                        ],
                      ),
                    ),
                  );
                },
              ),
            ),
            Padding(
              padding: const EdgeInsets.all(HbaSpacing.gutter),
              child: Column(
                children: [
                  Text(
                    'Déplacez et pincez pour cadrer. Ce que montre le cercle '
                    'est ce qui sera enregistré.',
                    textAlign: TextAlign.center,
                    style: theme.textTheme.bodyMedium
                        ?.copyWith(color: Colors.white70),
                  ),
                  const SizedBox(height: HbaSpacing.md),
                  FilledButton(
                    onPressed: _rend ? null : _valider,
                    style: FilledButton.styleFrom(
                      minimumSize: const Size.fromHeight(HbaSpacing.cible),
                    ),
                    child: Text(_rend ? 'Préparation…' : 'Utiliser cette photo'),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _valider() async {
    setState(() => _rend = true);

    try {
      final octets = await _rendre();

      if (!mounted) return;

      if (octets == null) {
        setState(() => _rend = false);
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text("Le cadrage n'a pas pu être appliqué.")),
        );
        return;
      }

      Navigator.of(context).pop(octets);
    } on Object {
      if (!mounted) return;
      setState(() => _rend = false);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text("Le cadrage n'a pas pu être appliqué.")),
      );
    }
  }

  /// Redessine la portion visible, en carre, et la compresse en JPEG.
  ///
  /// ON REDESSINE, ON NE CAPTURE PAS L'ECRAN. Un RepaintBoundary aurait rendu
  /// ce que l'ecran montre — donc la definition de l'ecran, avec le masque
  /// par-dessus et la qualite d'un apercu. Ici on repart de l'image d'origine
  /// et on lui applique la MEME transformation, a mille pixels de cote : ce
  /// que le client a cadre, en pleine definition.
  Future<Uint8List?> _rendre() async {
    if (_hublot <= 0) return null;

    final recorder = ui.PictureRecorder();
    final canvas = Canvas(recorder);

    // « final » ET NON « const » : toDouble() est un APPEL DE METHODE, et
    // Dart n'en evalue aucun dans une expression constante. La conversion
    // implicite int -> double ne vaut que pour un LITTERAL, pas pour une
    // reference a une constante — « const double cible = CadragePhoto.cote »
    // ne compile pas davantage.
    final cible = CadragePhoto.cote.toDouble();
    final echelle = cible / _hublot;

    // LE FOND EST PEINT AVANT TOUT. InteractiveViewer autorise a ecarter
    // l'image du hublot ; sans ce fond, la zone decouverte sortirait en
    // transparent, que le JPEG rendrait en noir sans prevenir.
    canvas.drawRect(
      Rect.fromLTWH(0, 0, cible, cible),
      Paint()..color = Colors.white,
    );

    canvas.save();
    canvas.scale(echelle);

    // LA MEME MATRICE QUE CELLE DU GESTE : c'est ce qui garantit que le
    // resultat est exactement ce que le cercle montrait.
    canvas.transform(_controleur.value.storage);

    // LA MEME COUVERTURE QU'A L'ECRAN, PRISE AU MEME ENDROIT. RawImage
    // l'applique la-bas ; la refaire de memoire ici, c'est se garantir un
    // decalage le jour ou l'une des deux versions bouge seule.
    final source = widget.image;
    final taille = _taillePourCouvrir(source, _hublot);

    canvas.drawImageRect(
      source,
      Rect.fromLTWH(0, 0, source.width.toDouble(), source.height.toDouble()),
      Rect.fromLTWH(
        (_hublot - taille.width) / 2,
        (_hublot - taille.height) / 2,
        taille.width,
        taille.height,
      ),
      Paint()..filterQuality = FilterQuality.high,
    );

    canvas.restore();

    final image = await recorder
        .endRecording()
        .toImage(CadragePhoto.cote, CadragePhoto.cote);

    try {
      final donnees = await image.toByteData(format: ui.ImageByteFormat.png);
      if (donnees == null) return null;

      // LE PNG PASSE EN JPEG, ET CE N'EST PAS COSMETIQUE. Un PNG de mille
      // pixels de cote pese un a deux megaoctets ; le meme en JPEG a 82 en
      // fait cent cinquante. Le client paie ses donnees a la recharge.
      //
      // LES DEUX DIMENSIONS SONT DONNEES EXPLICITEMENT, alors que l'image fait
      // deja exactement cette taille. Les valeurs par defaut du paquet sont
      // 1920 x 1080 et sa documentation ne dit PAS ce qu'il advient d'une
      // image plus petite — redimensionnee ou laissee telle quelle. Demander
      // la taille qu'on a deja retire la question : quoi qu'il fasse, il ne
      // peut que la garder.
      // « await » DANS LE try, ET CE N'EST PAS DU CONFORT. Sans lui, la
      // methode rend la Future et le « finally » libere l'image AVANT que la
      // compression ne soit terminee. Elle ne s'en sert pas aujourd'hui, donc
      // rien ne casse — mais c'est exactement le genre de dependance tacite
      // qui tombe a la premiere retouche.
      return await FlutterImageCompress.compressWithList(
        donnees.buffer.asUint8List(),
        minWidth: CadragePhoto.cote,
        minHeight: CadragePhoto.cote,
        quality: 82,
        format: CompressFormat.jpeg,
      );
    } finally {
      image.dispose();
    }
  }
}

/// Le masque : tout s'assombrit sauf le disque.
class _Hublot extends StatelessWidget {
  const _Hublot();

  @override
  Widget build(BuildContext context) => CustomPaint(painter: _PeintreHublot());
}

class _PeintreHublot extends CustomPainter {
  @override
  void paint(Canvas canvas, Size size) {
    final disque = Path()
      ..addOval(Rect.fromCircle(
        center: size.center(Offset.zero),
        radius: size.shortestSide / 2,
      ));

    final voile = Path.combine(
      PathOperation.difference,
      Path()..addRect(Offset.zero & size),
      disque,
    );

    // ASSEZ SOMBRE POUR DIRE « CECI SERA JETE », assez clair pour qu'on voie
    // encore ce qu'on ecarte : c'est en regardant ce qui sort du cercle qu'on
    // sait dans quel sens deplacer.
    canvas.drawPath(voile, Paint()..color = const Color(0xB3000000));

    canvas.drawPath(
      disque,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 2
        ..color = Colors.white70,
    );
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}
