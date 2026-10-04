import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_ui/hba_ui.dart';

import 'models.dart';
import 'tracking_screen.dart';

/// L'ecran pendant qu'on cherche un livreur.
///
/// UN ECRAN A PART, ET PAS UNE CARTE DE PLUS DANS LA LISTE. C'est le seul
/// moment du suivi ou le client n'a rien a faire et rien a lire : il attend.
/// Une liste de renseignements lui demande de chercher l'information qui compte
/// alors qu'il n'y en a qu'une — « ou en est-on ». La carte et le halo la
/// donnent sans un mot.
///
/// PAS DE CLIPRRECT, PAS DE CLIPPATH, PAS DE BORDERRADIUS AVEC clipBehavior,
/// PAS DE MATERIAL AVEC UNE FORME, autour de cette carte ni d'aucune autre vue
/// native : sur iOS, tout ce qui est peint apres un rognage de vue native
/// disparait. Verifie au pixel le 28 septembre 2026.
/// Ce que le radar a besoin de savoir, quelle que soit la nature de la mission.
///
/// IL EXISTE PARCE QUE LA MEME ATTENTE SE JOUE DEUX FOIS. Cette vue a ete ecrite
/// pour un colis, et depuis le 3 octobre 2026 une COURSE DE PERSONNES la montre
/// aussi, apres paiement, sur l'accueil. En ecrire une seconde aurait donne deux
/// halos a maintenir, deux compteurs, et la certitude qu'ils divergent.
///
/// IL NE PORTE QUE CE QUE LA VUE AFFICHE. Pas de statut, pas d'identifiant de
/// chauffeur : ce qui decide d'AFFICHER cette vue appartient a l'appelant, pas
/// a elle.
@immutable
class MissionEnRecherche {
  const MissionEnRecherche({
    required this.depart,
    required this.arrivee,
    required this.departLibelle,
    required this.arriveeLibelle,
    required this.reference,
    required this.prixXof,
    required this.depuis,
    required this.titre,
    this.introuvable = false,
  });

  /// Les deux bouts du trajet. Nuls quand le service ne les a pas rendus : la
  /// carte retombe alors sur le centre de Cotonou.
  final LatLng? depart;
  final LatLng? arrivee;

  /// CE QU'ON ECRIT SOUS CHAQUE POINT. Un colis porte des reperes saisis par le
  /// client ; une course de personnes n'en a pas au contrat, et l'appelant y met
  /// alors ses coordonnees — c'est deja ce que montre l'ecran de commande.
  final String departLibelle;
  final String arriveeLibelle;

  final String reference;
  final int prixXof;

  /// Depuis quand on cherche.
  ///
  /// NULLABLE, COMME LE CHAMP D'OU IL VIENT : une reponse ancienne ou tronquee
  /// peut ne pas porter l'instant de la commande. [RechercheEnCours] perd alors
  /// son compteur et garde son explication — ce qui vaut mieux que de mettre
  /// « maintenant » a la place, qui afficherait zero seconde indefiniment.
  final DateTime? depuis;

  /// Le titre du recapitulatif : « Recapitulatif de l'envoi », « de la course ».
  final String titre;

  /// La recherche a-t-elle echoue faute de candidat ?
  final bool introuvable;
}

class RechercheLivreurVue extends StatefulWidget {
  const RechercheLivreurVue({
    required this.mission,
    required this.onAnnuler,
    super.key,
  });

  /// La mission d'un colis, telle que le suivi et l'accueil la connaissent.
  factory RechercheLivreurVue.pourLivraison({
    required Delivery delivery,
    required VoidCallback onAnnuler,
    Key? key,
  }) {
    final depart = delivery.pickupPoint;
    final arrivee = delivery.dropoffPoint;

    return RechercheLivreurVue(
      key: key,
      onAnnuler: onAnnuler,
      mission: MissionEnRecherche(
        depart: depart == null ? null : LatLng(depart.latitude, depart.longitude),
        arrivee: arrivee == null ? null : LatLng(arrivee.latitude, arrivee.longitude),
        departLibelle: delivery.pickupLandmark,
        arriveeLibelle: delivery.dropoffLandmark,
        reference: delivery.reference,
        prixXof: delivery.totalXof,
        depuis: delivery.createdAt,
        titre: "Récapitulatif de l'envoi",
        introuvable: delivery.status == DeliveryStatus.noDriverFound,
      ),
    );
  }

  final MissionEnRecherche mission;
  final VoidCallback onAnnuler;

  @override
  State<RechercheLivreurVue> createState() => _RechercheLivreurVueState();
}

/// L'ETAT N'EXISTE QUE POUR LES EPINGLES. Cette vue n'avait rien a retenir
/// jusqu'ici ; dessiner une goutte demande d'attendre la densite de l'ecran,
/// puis une rasterisation — deux choses qu'un widget sans etat ne peut pas
/// porter.
class _RechercheLivreurVueState extends State<RechercheLivreurVue> {
  MissionEnRecherche get mission => widget.mission;

  /// Les deux points de la course, dessines plutot qu'empruntes a Google.
  ///
  /// ORANGE ON VA CHERCHER, VERT ON ARRIVE : le meme code que l'application
  /// livreur, depuis que les epingles vivent dans le socle. Le client et le
  /// livreur regardent la meme course ; ils ne devraient pas l'apprendre deux
  /// fois.
  BitmapDescriptor? _collecte;
  BitmapDescriptor? _livraison;
  double? _dessineeA;

  /// LE CONTROLEUR N'ARRIVE QU'APRES LA PREMIERE IMAGE. Le bouton de recadrage
  /// peut etre presse avant : le Completer le fait attendre au lieu de lancer
  /// une exception sur un controleur nul.
  final _carte = Completer<GoogleMapController>();

  /// Plan ou satellite.
  ///
  /// POURQUOI CE BOUTON EXISTE. A Cotonou, beaucoup de points d'enlevement ne
  /// sont pas sur une rue nommee : la vue satellite montre la tole, la cour,
  /// le mur — ce que le repere ecrit decrit. Le plan ne montre qu'un vide.
  MapType _plan = MapType.normal;

  /// Recadre sur les deux bouts de la course.
  ///
  /// LES DEUX, PAS LE MILIEU. Recentrer sur le barycentre a un zoom fixe
  /// laisse les deux epingles hors de l'ecran des que la course est longue ;
  /// un cadre qui les contient repond a « ou va mon colis » en une image.
  Future<void> _recadrer() async {
    final depart = mission.depart;
    final arrivee = mission.arrivee;
    final carte = await _carte.future;

    if (depart == null || arrivee == null) {
      await carte.animateCamera(CameraUpdate.newLatLngZoom(_centre, 14));
      return;
    }

    await carte.animateCamera(
      CameraUpdate.newLatLngBounds(
        LatLngBounds(
          southwest: LatLng(
            depart.latitude < arrivee.latitude
                ? depart.latitude
                : arrivee.latitude,
            depart.longitude < arrivee.longitude
                ? depart.longitude
                : arrivee.longitude,
          ),
          northeast: LatLng(
            depart.latitude > arrivee.latitude
                ? depart.latitude
                : arrivee.latitude,
            depart.longitude > arrivee.longitude
                ? depart.longitude
                : arrivee.longitude,
          ),
        ),

        // LA MARGE EST LARGE PARCE QUE LES DEUX CARTES MANGENT L'ECRAN. Sans
        // elle, une epingle se retrouve sous le recapitulatif, donc nulle part.
        96,
      ),
    );
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();

    final ratio = MediaQuery.devicePixelRatioOf(context);
    if (ratio == _dessineeA) return;

    _dessineeA = ratio;
    unawaited(_dessiner(ratio));
  }

  Future<void> _dessiner(double ratio) async {
    final collecte = await Epingles.goutte(couleur: HbaColors.primary, ratio: ratio);
    final livraison = await Epingles.goutte(couleur: HbaColors.success, ratio: ratio);

    // Deux dessins peuvent se croiser : le plus lent ecraserait le plus recent.
    if (!mounted || ratio != _dessineeA) return;

    setState(() {
      _collecte = collecte;
      _livraison = livraison;
    });
  }

  /// Repli quand le service n'a pas rendu les points : le centre de Cotonou.
  static const _cotonou = LatLng(6.3703, 2.3912);

  LatLng get _centre {
    final depart = mission.depart;
    final arrivee = mission.arrivee;

    if (depart != null && arrivee != null) {
      return LatLng(
        (depart.latitude + arrivee.latitude) / 2,
        (depart.longitude + arrivee.longitude) / 2,
      );
    }

    return depart ?? arrivee ?? _cotonou;
  }

  Set<Marker> get _reperes {
    final marqueurs = <Marker>{};
    final depart = mission.depart;
    final arrivee = mission.arrivee;

    // LE BALLON DE GOOGLE RESTE LE SECOURS tant que le dessin n'est pas pret,
    // ou s'il a echoue : une carte sans reperes serait pire qu'une carte mal
    // habillee.
    if (depart != null) {
      marqueurs.add(Marker(
        markerId: const MarkerId('depart'),
        position: depart,
        icon: _collecte ??
            BitmapDescriptor.defaultMarkerWithHue(BitmapDescriptor.hueOrange),
      ));
    }

    if (arrivee != null) {
      marqueurs.add(Marker(
        markerId: const MarkerId('arrivee'),
        position: arrivee,
        icon: _livraison ??
            BitmapDescriptor.defaultMarkerWithHue(BitmapDescriptor.hueGreen),
      ));
    }

    return marqueurs;
  }

  @override
  Widget build(BuildContext context) {
    final introuvable = mission.introuvable;
    final peutRevenir = Navigator.of(context).canPop();

    return Stack(
      children: [
        Positioned.fill(
          child: GoogleMap(
            initialCameraPosition: CameraPosition(target: _centre, zoom: 13),
            markers: _reperes,
            mapType: _plan,
            onMapCreated: (carte) {
              if (!_carte.isCompleted) _carte.complete(carte);
            },
            myLocationEnabled: false,
            myLocationButtonEnabled: false,
            zoomControlsEnabled: false,
            mapToolbarEnabled: false,
            liteModeEnabled: false,
          ),
        ),

        // LA FLECHE DE RETOUR, ET SEULEMENT S'IL Y A OU REVENIR.
        //
        // Cette vue est posee a deux endroits : sur l'ACCUEIL, qui est la
        // racine d'un onglet — rien a depiler, et une fleche y mentirait — et
        // sur le SUIVI, ouvert par-dessus autre chose. « canPop » distingue les
        // deux sans qu'aucun des deux appelants n'ait a le dire.
        //
        // PAS D'AppBar : elle poserait un bandeau opaque sur une carte qui va
        // jusqu'en haut. Un bouton rond flottant tient le meme role.
        if (peutRevenir)
          Positioned(
            top: HbaSpacing.sm,
            left: HbaSpacing.sm,
            child: SafeArea(
              child: Material(
                color: HbaColors.surface,
                shape: const CircleBorder(),
                elevation: 2,
                child: IconButton(
                  icon: const Icon(Icons.arrow_back),
                  color: HbaColors.ink,
                  tooltip: 'Retour',
                  onPressed: () => Navigator.of(context).pop(),
                ),
              ),
            ),
          ),

        // LES DEUX RONDS DE DROITE : recadrer, et changer de plan.
        //
        // A DROITE, PARCE QUE LA FLECHE EST A GAUCHE. Ce sont des outils de
        // carte, pas de navigation ; les melanger ferait chercher le retour
        // parmi trois ronds identiques.
        Positioned(
          top: HbaSpacing.sm,
          right: HbaSpacing.sm,
          child: SafeArea(
            child: Column(
              children: [
                _OutilDeCarte(
                  icone: Icons.my_location,
                  etiquette: 'Recadrer sur la course',
                  onTap: () => unawaited(_recadrer()),
                ),
                const SizedBox(height: HbaSpacing.sm),
                _OutilDeCarte(
                  icone: Icons.layers_outlined,
                  etiquette: _plan == MapType.normal
                      ? 'Vue satellite'
                      : 'Vue plan',
                  onTap: () => setState(
                    () => _plan = _plan == MapType.normal
                        ? MapType.hybrid
                        : MapType.normal,
                  ),
                ),
              ],
            ),
          ),
        ),

        // LE BANDEAU NE PARAIT QUE QUAND C'EST VRAI. La maquette le montrait
        // pendant la recherche ; il dit pourtant que la recherche a ECHOUE.
        // L'afficher des le depart apprendrait au client a ne plus le lire.
        if (introuvable)
          Positioned(
            top: HbaSpacing.md,
            // Le bandeau s'ecarte de la fleche quand elle est la, au lieu de
            // passer dessous.
            left: peutRevenir ? 64 : HbaSpacing.gutter,
            right: HbaSpacing.gutter,
            child: SafeArea(child: _Bandeau()),
          ),

        if (!introuvable)
          Align(
            alignment: const Alignment(0, -0.35),
            child: Padding(
              padding: const EdgeInsets.symmetric(
                horizontal: HbaSpacing.sm,
              ),
              // LE COMPTEUR ET SES TEXTES VIENNENT DU WIDGET DEJA TESTE. Les
              // reecrire ici aurait produit une seconde horloge, et laisse les
              // sept tests surveiller du code que plus rien n'affiche.
              //
              // LE HALO EST PASSE DEDANS, le 4 octobre 2026. Pose au-dessus et
              // large de deux cents points, il occupait a lui seul le tiers de
              // l'ecran pour dire une chose que le titre dit deja. Reduit et
              // mis a gauche du titre, il garde son role — montrer que ca
              // tourne — sans prendre la place de la carte.
              child: RechercheEnCours(
                depuis: mission.depuis,
                vignette: const _Halo(taille: 104),
              ),
            ),
          ),

        Align(
          alignment: Alignment.bottomCenter,
          child: _Recapitulatif(mission: mission, onAnnuler: widget.onAnnuler),
        ),
      ],
    );
  }
}

/// Le halo qui bat, et le pictogramme au centre.
class _Halo extends StatefulWidget {
  const _Halo({this.taille = 200});

  /// Le cote du carre ou bat le halo. Le pictogramme suit la meme proportion :
  /// fixe, il paraitrait enorme a cent points et perdu a deux cents.
  final double taille;

  @override
  State<_Halo> createState() => _HaloState();
}

class _HaloState extends State<_Halo> with SingleTickerProviderStateMixin {
  late final AnimationController _controleur = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 1800),
  )..repeat();

  @override
  void dispose() {
    _controleur.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => SizedBox(
        height: widget.taille,
        width: widget.taille,
        child: AnimatedBuilder(
          animation: _controleur,
          builder: (_, enfant) => CustomPaint(
            painter: _PeintreHalo(_controleur.value),
            child: enfant,
          ),
          // L'ENFANT EST CONSTRUIT UNE FOIS, PAS SOIXANTE FOIS PAR SECONDE :
          // c'est le halo qui bouge, pas le pictogramme.
          child: Center(
            child: Container(
              height: widget.taille * 0.32,
              width: widget.taille * 0.32,
              decoration: const BoxDecoration(
                color: HbaColors.primary,
                shape: BoxShape.circle,
              ),
              child: Icon(
                Icons.electric_moped_outlined,
                color: Colors.white,
                size: widget.taille * 0.15,
              ),
            ),
          ),
        ),
      );
}

class _PeintreHalo extends CustomPainter {
  const _PeintreHalo(this.avancement);

  final double avancement;

  @override
  void paint(Canvas canvas, Size size) {
    final centre = size.center(Offset.zero);
    const rayonMin = 34.0;
    final rayonMax = size.shortestSide / 2;

    // DEUX ONDES DECALEES D'UNE DEMI-PERIODE : une seule donne un battement
    // qui s'arrete visiblement avant de repartir.
    for (final decalage in [0.0, 0.5]) {
      final t = (avancement + decalage) % 1.0;
      final rayon = rayonMin + (rayonMax - rayonMin) * t;

      canvas.drawCircle(
        centre,
        rayon,
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = 2
          ..color = HbaColors.primary.withValues(alpha: (1 - t) * 0.55),
      );
    }
  }

  @override
  bool shouldRepaint(covariant _PeintreHalo ancien) =>
      ancien.avancement != avancement;
}

class _Bandeau extends StatelessWidget {
  @override
  Widget build(BuildContext context) => HbaCard(
        padding: const EdgeInsets.all(HbaSpacing.md),
        child: Row(
          children: [
            const Icon(Icons.person_off_outlined, color: HbaColors.inkMuted),
            const SizedBox(width: HbaSpacing.sm),
            Expanded(
              child: Text(
                'Aucun livreur dans ce secteur pour le moment.',
                style: Theme.of(context).textTheme.bodyMedium,
              ),
            ),
          ],
        ),
      );
}

/// Le recapitulatif, en bas de l'ecran.
class _Recapitulatif extends StatelessWidget {
  const _Recapitulatif({required this.mission, required this.onAnnuler});

  final MissionEnRecherche mission;
  final VoidCallback onAnnuler;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.fromLTRB(
        HbaSpacing.gutter,
        HbaSpacing.lg,
        HbaSpacing.gutter,
        HbaSpacing.lg,
      ),
      decoration: const BoxDecoration(
        color: HbaColors.surface,
        borderRadius: BorderRadius.vertical(top: Radius.circular(HbaRadius.card)),
      ),
      child: SafeArea(
        top: false,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(
                    mission.titre,
                    style: theme.textTheme.titleMedium,
                  ),
                ),
                _Reference(valeur: mission.reference),
              ],
            ),
            const SizedBox(height: HbaSpacing.md),

            _Etape(
              plein: false,
              titre: 'Départ',
              detail: mission.departLibelle,
            ),
            const Padding(
              padding: EdgeInsets.only(left: 5),
              child: SizedBox(
                height: 18,
                child: VerticalDivider(width: 2, color: HbaColors.border),
              ),
            ),
            _Etape(
              plein: true,
              titre: 'Arrivée',
              detail: mission.arriveeLibelle,
            ),

            const SizedBox(height: HbaSpacing.md),
            const Divider(height: 1, color: HbaColors.border),
            const SizedBox(height: HbaSpacing.md),

            Row(
              children: [
                Expanded(
                  child: Text(
                    'Prix de la course',
                    style: theme.textTheme.bodyMedium
                        ?.copyWith(color: HbaColors.inkMuted),
                  ),
                ),
                Text(
                  Xof.format(mission.prixXof),
                  style: theme.textTheme.titleLarge
                      ?.copyWith(color: HbaColors.primary),
                ),
                IconButton(
                  icon: const Icon(Icons.info_outline, size: 20),
                  color: HbaColors.inkMuted,
                  tooltip: 'À propos de ce prix',
                  onPressed: () => _expliquerLePrix(context),
                ),
              ],
            ),

            const SizedBox(height: HbaSpacing.md),
            // ROUGE, ET SEULEMENT EN CONTOUR.
            //
            // GRIS, IL SE LISAIT COMME UN BOUTON SECONDAIRE PARMI D'AUTRES ;
            // or c'est le seul geste de cet ecran qui defait quelque chose, et
            // il peut couter des frais. Le rouge le dit. Le contour plutot que
            // l'aplat, parce qu'un bouton rouge plein au bas d'un ecran
            // d'attente attire la main qui cherchait a valider.
            OutlinedButton.icon(
              onPressed: onAnnuler,
              icon: const Icon(Icons.close, size: 18),
              label: const Text('Annuler la demande'),
              style: OutlinedButton.styleFrom(
                minimumSize: const Size.fromHeight(HbaSpacing.cible),
                foregroundColor: HbaColors.danger,
                backgroundColor: HbaColors.dangerSoft,
                side: const BorderSide(color: HbaColors.danger),
                shape: const StadiumBorder(),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Etape extends StatelessWidget {
  const _Etape({required this.plein, required this.titre, required this.detail});

  final bool plein;
  final String titre;
  final String detail;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.only(top: 5),
          child: Container(
            height: 12,
            width: 12,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              color: plein ? HbaColors.primary : Colors.transparent,
              border: Border.all(color: HbaColors.primary, width: 2),
            ),
          ),
        ),
        const SizedBox(width: HbaSpacing.md),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(titre, style: theme.textTheme.titleSmall),
              Text(
                // UN REPERE VIDE SE DIT, IL NE SE TAIT PAS. Une ligne blanche
                // sous « Depart » laisse croire a un ecran a moitie charge.
                detail.isEmpty ? 'Repère non renseigné' : detail,
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: HbaColors.inkMuted,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

/// Ce que dit l'icone a cote du prix.
///
/// ELLE NE DONNE PAS LE DETAIL DU CALCUL. Le devis l'avait — distance, poids,
/// supplements — mais il est consomme : la course ne porte plus qu'un total.
/// Inventer une ventilation ici reviendrait a l'estimer, donc a mentir sur une
/// somme que le client a deja payee. Ce que l'ecran peut dire avec certitude,
/// il le dit.
Future<void> _expliquerLePrix(BuildContext context) => showDialog<void>(
      context: context,
      builder: (contexte) => AlertDialog(
        title: const Text('À propos de ce prix'),
        content: const Text(
          'Il a été fixé au moment de la commande et il est déjà réglé. '
          'Il ne bouge pas si la recherche dure, ni si le trajet prend plus '
          'de temps que prévu.\n\n'
          'Annuler peut entraîner des frais : le détail s’affiche avant que '
          'vous confirmiez.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(contexte).pop(),
            child: const Text('J’ai compris'),
          ),
        ],
      ),
    );

/// La reference, et le bouton qui la copie.
///
/// POURQUOI ON LA COPIE. C'est le seul mot que le client aura a donner au
/// support, par telephone ou par message. Le relever a la main sur un ecran de
/// telephone, caractere par caractere, est exactement le genre de tache ou l'on
/// se trompe — et une reference fausse fait chercher la mauvaise course.
class _Reference extends StatelessWidget {
  const _Reference({required this.valeur});

  final String valeur;

  @override
  Widget build(BuildContext context) => Semantics(
        button: true,
        label: 'Copier la référence $valeur',
        child: InkWell(
          borderRadius: BorderRadius.circular(HbaRadius.chip),
          onTap: () {
            unawaited(Clipboard.setData(ClipboardData(text: valeur)));

            // LE RETOUR EST IMMEDIAT ET LOCAL. Rien ne part au reseau : une
            // attente, ici, ferait douter que l'appui ait ete pris.
            ScaffoldMessenger.of(context).showSnackBar(
              const SnackBar(content: Text('Référence copiée')),
            );
          },
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              HbaChip(label: valeur),
              const SizedBox(width: HbaSpacing.xs),
              const Icon(
                Icons.copy_rounded,
                size: 16,
                color: HbaColors.inkMuted,
              ),
            ],
          ),
        ),
      );
}

/// Un rond blanc pose sur la carte : recadrer, changer de plan.
class _OutilDeCarte extends StatelessWidget {
  const _OutilDeCarte({
    required this.icone,
    required this.etiquette,
    required this.onTap,
  });

  final IconData icone;
  final String etiquette;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Material(
        color: HbaColors.surface,
        shape: const CircleBorder(),
        elevation: 2,
        child: IconButton(
          icon: Icon(icone),
          color: HbaColors.ink,
          tooltip: etiquette,
          onPressed: onTap,
        ),
      );
}
