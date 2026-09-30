import 'dart:async';

import 'package:flutter/material.dart';
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
class RechercheLivreurVue extends StatefulWidget {
  const RechercheLivreurVue({
    required this.delivery,
    required this.onAnnuler,
    super.key,
  });

  final Delivery delivery;
  final VoidCallback onAnnuler;

  @override
  State<RechercheLivreurVue> createState() => _RechercheLivreurVueState();
}

/// L'ETAT N'EXISTE QUE POUR LES EPINGLES. Cette vue n'avait rien a retenir
/// jusqu'ici ; dessiner une goutte demande d'attendre la densite de l'ecran,
/// puis une rasterisation — deux choses qu'un widget sans etat ne peut pas
/// porter.
class _RechercheLivreurVueState extends State<RechercheLivreurVue> {
  Delivery get delivery => widget.delivery;

  /// Les deux points de la course, dessines plutot qu'empruntes a Google.
  ///
  /// ORANGE ON VA CHERCHER, VERT ON ARRIVE : le meme code que l'application
  /// livreur, depuis que les epingles vivent dans le socle. Le client et le
  /// livreur regardent la meme course ; ils ne devraient pas l'apprendre deux
  /// fois.
  BitmapDescriptor? _collecte;
  BitmapDescriptor? _livraison;
  double? _dessineeA;

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
    final depart = delivery.pickupPoint;
    final arrivee = delivery.dropoffPoint;

    if (depart != null && arrivee != null) {
      return LatLng(
        (depart.latitude + arrivee.latitude) / 2,
        (depart.longitude + arrivee.longitude) / 2,
      );
    }

    final seul = depart ?? arrivee;
    if (seul != null) return LatLng(seul.latitude, seul.longitude);

    return _cotonou;
  }

  Set<Marker> get _reperes {
    final marqueurs = <Marker>{};
    final depart = delivery.pickupPoint;
    final arrivee = delivery.dropoffPoint;

    // LE BALLON DE GOOGLE RESTE LE SECOURS tant que le dessin n'est pas pret,
    // ou s'il a echoue : une carte sans reperes serait pire qu'une carte mal
    // habillee.
    if (depart != null) {
      marqueurs.add(Marker(
        markerId: const MarkerId('depart'),
        position: LatLng(depart.latitude, depart.longitude),
        icon: _collecte ??
            BitmapDescriptor.defaultMarkerWithHue(BitmapDescriptor.hueOrange),
      ));
    }

    if (arrivee != null) {
      marqueurs.add(Marker(
        markerId: const MarkerId('arrivee'),
        position: LatLng(arrivee.latitude, arrivee.longitude),
        icon: _livraison ??
            BitmapDescriptor.defaultMarkerWithHue(BitmapDescriptor.hueGreen),
      ));
    }

    return marqueurs;
  }

  @override
  Widget build(BuildContext context) {
    final introuvable = delivery.status == DeliveryStatus.noDriverFound;
    final peutRevenir = Navigator.of(context).canPop();

    return Stack(
      children: [
        Positioned.fill(
          child: GoogleMap(
            initialCameraPosition: CameraPosition(target: _centre, zoom: 13),
            markers: _reperes,
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
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const _Halo(),
                const SizedBox(height: HbaSpacing.md),
                Padding(
                  padding: const EdgeInsets.symmetric(
                    horizontal: HbaSpacing.gutter,
                  ),
                  // LE COMPTEUR ET SES TEXTES VIENNENT DU WIDGET DEJA TESTE.
                  // Les reecrire ici aurait produit une seconde horloge, et
                  // laisse les sept tests surveiller du code que plus rien
                  // n'affiche.
                  child: RechercheEnCours(depuis: delivery.createdAt),
                ),
              ],
            ),
          ),

        Align(
          alignment: Alignment.bottomCenter,
          child: _Recapitulatif(delivery: delivery, onAnnuler: widget.onAnnuler),
        ),
      ],
    );
  }
}

/// Le halo qui bat, et le pictogramme au centre.
class _Halo extends StatefulWidget {
  const _Halo();

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
        height: 200,
        width: 200,
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
              height: 64,
              width: 64,
              decoration: const BoxDecoration(
                color: HbaColors.primary,
                shape: BoxShape.circle,
              ),
              child: const Icon(
                Icons.electric_moped_outlined,
                color: Colors.white,
                size: 30,
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
  const _Recapitulatif({required this.delivery, required this.onAnnuler});

  final Delivery delivery;
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
                    "Récapitulatif de l'envoi",
                    style: theme.textTheme.titleMedium,
                  ),
                ),
                HbaChip(label: delivery.reference),
              ],
            ),
            const SizedBox(height: HbaSpacing.md),

            _Etape(
              plein: false,
              titre: 'Départ',
              detail: delivery.pickupLandmark,
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
              detail: delivery.dropoffLandmark,
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
                  Xof.format(delivery.totalXof),
                  style: theme.textTheme.titleLarge
                      ?.copyWith(color: HbaColors.primary),
                ),
              ],
            ),

            const SizedBox(height: HbaSpacing.md),
            OutlinedButton(
              onPressed: onAnnuler,
              style: OutlinedButton.styleFrom(
                minimumSize: const Size.fromHeight(HbaSpacing.cible),
                foregroundColor: HbaColors.inkMuted,
                side: const BorderSide(color: HbaColors.border),
                shape: const StadiumBorder(),
              ),
              child: const Text('Annuler la demande'),
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
