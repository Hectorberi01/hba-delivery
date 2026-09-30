import 'dart:async';

import 'package:flutter/material.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_ui/hba_ui.dart';

import '../../core/signal.dart';
import 'carte_course.dart';
import 'models.dart';

/// Offre de course, presentee en feuille modale.
///
/// Le compte a rebours vient de `expiresAt` renvoye par le serveur : la duree
/// d'une vague est une decision de dispatch, pas une constante de
/// l'application. Si elle change cote serveur, l'ecran suit sans redeploiement.
class OfferSheet extends StatefulWidget {
  const OfferSheet({
    required this.offer,
    required this.onAccept,
    required this.onDecline,
    this.position,
    super.key,
  });

  final Offer offer;

  /// Derniere position connue du livreur, pour se situer par rapport a la
  /// collecte. Null tant que le telephone n'en a rendu aucune.
  final LatLng? position;
  final Future<void> Function() onAccept;
  final Future<void> Function() onDecline;

  @override
  State<OfferSheet> createState() => _OfferSheetState();
}

class _OfferSheetState extends State<OfferSheet> {
  /// Temps pendant lequel la feuille expiree reste visible avant de se fermer.
  ///
  /// ASSEZ LONG POUR ETRE LU, ASSEZ COURT POUR NE PAS COUTER UNE VAGUE. C'est
  /// le compromis de la correction S4 : la feuille ne disparait pas sous les
  /// yeux du livreur au moment ou il tend le doigt, et elle ne le retient pas
  /// non plus hors du circuit.
  static const _delaiDeFermeture = Duration(seconds: 4);

  Timer? _ticker;
  Timer? _fermeture;
  late Duration _remaining;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _remaining = widget.offer.remainingFrom(DateTime.now());
    _ticker = Timer.periodic(const Duration(seconds: 1), (_) {
      if (!mounted) return;
      setState(() => _remaining = widget.offer.remainingFrom(DateTime.now()));

      if (_remaining == Duration.zero) {
        _ticker?.cancel();

        // L'OFFRE EXPIREE NE DOIT PLUS RIEN RECLAMER. Le livreur doit voir
        // POURQUOI le bouton est mort plutot que de voir la feuille disparaitre
        // sous ses yeux. Mais continuer a sonner pour une course qu'il ne peut
        // plus prendre serait une brimade.
        unawaited(Signal.silence());

        // ELLE SE FERME ENSUITE, ET C'EST LA CORRECTION S4.
        //
        // Elle restait ouverte pour toujours. Or la feuille est montee en
        // « isDismissible: false, enableDrag: false » et ne porte aucun bouton
        // de fermeture : les deux seules sorties sont Accepter et Refuser, tous
        // deux morts une fois l'offre expiree. Tant qu'elle tenait l'ecran,
        // l'accueil gardait « _sheetOpen » a vrai et SUSPENDAIT LE SONDAGE —
        // pendant que le battement de position continuait d'annoncer au serveur
        // un livreur disponible. Un telephone pose deux minutes, et le livreur
        // sortait du circuit sans rien voir, tout en restant proposable : trente
        // secondes perdues pour chaque client d'une vague qu'il ne verrait pas.
        //
        // ON GARDE DONC L'INTENTION D'ORIGINE — montrer pourquoi — et on lui
        // donne une fin.
        _fermeture = Timer(_delaiDeFermeture, () {
          // PAS PENDANT UNE ACTION EN COURS : si le livreur a glisse pour
          // accepter a la seconde ou l'offre expirait, l'aller-retour reseau
          // est encore en vol et c'est lui qui fermera la feuille.
          if (!mounted || _busy) return;
          Navigator.of(context).pop();
        });
      }
    });
  }

  @override
  void dispose() {
    _ticker?.cancel();
    _fermeture?.cancel();

    // FILET DE SECURITE. L'acceptation et le refus coupent deja le signal, et
    // l'expiration aussi ; il reste les fermetures qu'on n'a pas prevues.
    // Aucune ne doit laisser un telephone sonner tout seul.
    unawaited(Signal.silence());
    super.dispose();
  }

  Future<void> _run(Future<void> Function() action) async {
    // LE SIGNAL S'ARRETE AU DOIGT, PAS A LA REPONSE DU SERVEUR. L'acceptation
    // demande un aller-retour reseau qui dure parfois deux secondes : les
    // laisser sonner apres l'appui donne l'impression que l'appui n'a pas
    // porte.
    unawaited(Signal.silence());

    setState(() => _busy = true);
    try {
      await action();
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final offer = widget.offer;
    final expired = _remaining == Duration.zero;

    return SafeArea(
      top: false,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          // LES DEUX BOUTONS NE DEFILENT PAS, LE RESTE OUI.
          //
          // Depuis que la carte occupe cent cinquante pixels, la feuille peut
          // depasser la hauteur d'un petit telephone. Si « Accepter » passait
          // sous le bord, le livreur chercherait a faire glisser l'ecran
          // pendant que le compte a rebours tourne, et perdrait la course en
          // essayant de la prendre.
          Flexible(
            child: SingleChildScrollView(
              padding: const EdgeInsets.fromLTRB(
                HbaSpacing.gutter,
                HbaSpacing.gutter,
                HbaSpacing.gutter,
                0,
              ),
              child: Column(
                mainAxisSize: MainAxisSize.min,

                // ETIREE, PAS ALIGNEE A GAUCHE. Avec « start », chaque enfant
                // se dimensionne a son contenu : la carte de remuneration ne
                // faisait que la largeur du mot le plus long, et flottait a
                // gauche sous un titre pleine largeur. Les blocs de cet ecran
                // sont des blocs, pas des etiquettes.
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          'Nouvelle course',
                          style: theme.textTheme.headlineMedium,
                        ),
                      ),
                      HbaChip(
                        label: expired ? 'EXPIREE' : '${_remaining.inSeconds} s',
                        tone: expired ? HbaChipTone.neutral : HbaChipTone.primary,
                      ),
                    ],
                  ),
                  const SizedBox(height: HbaSpacing.lg),
                  HbaCard(
                    padding: const EdgeInsets.all(HbaSpacing.lg),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('Votre rémunération', style: theme.textTheme.bodyMedium),
                        const SizedBox(height: HbaSpacing.xs),
                        Text(
                          Xof.format(offer.driverEarningXof),
                          style: theme.textTheme.displaySmall
                              ?.copyWith(color: HbaColors.primaryInk),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: HbaSpacing.md),

                  // LA CARTE NE MONTRE QUE LA COLLECTE. Le contrat ne
                  // transporte pas la destination avant acceptation, et ce
                  // n'est pas un oubli : sans cette regle, un livreur trierait
                  // les courses par quartier d'arrivee. Ce qu'il lui faut pour
                  // decider tient ici — ou aller maintenant, et a quelle
                  // distance il se trouve.
                  if (offer.aUnPointDeCollecte) ...[
                    CarteCourse(
                      collecte: LatLng(offer.pickupLatitude, offer.pickupLongitude),
                      moi: widget.position,
                      hauteur: 150,
                    ),
                    const SizedBox(height: HbaSpacing.md),
                  ],

                  _Row(
                    icon: Icons.place_outlined,
                    title: 'Collecte',
                    value: offer.pickupLandmark,
                  ),
                  const SizedBox(height: HbaSpacing.md),
                  _Row(
                    icon: Icons.directions_bike_outlined,
                    title: "Jusqu'à la collecte",
                    value: _distance(offer.distanceToPickupMeters),
                  ),
                  const SizedBox(height: HbaSpacing.md),
                  _Row(
                    icon: Icons.route_outlined,
                    title: 'Course',
                    value: _distance(offer.tripDistanceMeters),
                  ),
                  const SizedBox(height: HbaSpacing.lg),

                  // L'adresse de destination n'est pas affichee : elle n'est
                  // pas dans l'offre, et elle n'arrive qu'apres acceptation.
                  Text(
                    "L'adresse de livraison s'affiche dès que vous acceptez.",
                    style: theme.textTheme.bodySmall
                        ?.copyWith(color: HbaColors.inkMuted),
                  ),
                  const SizedBox(height: HbaSpacing.lg),
                ],
              ),
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(
              HbaSpacing.gutter,
              0,
              HbaSpacing.gutter,
              HbaSpacing.sm,
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                // ACCEPTER SE GLISSE, REFUSER S'APPUIE, ET L'ASYMETRIE EST
                // VOULUE. Accepter engage le livreur devant un client et se
                // declenchait jusqu'ici d'un appui — celui que produit un
                // telephone secoue dans une poche, ou un pouce pose au hasard
                // sur une feuille qui vient de s'ouvrir toute seule. Refuser
                // ne coute qu'une offre, qui serait de toute facon partie a
                // l'expiration.
                HbaGlissiere(
                  libelle: expired ? 'Offre expirée' : 'Glissez pour accepter',
                  libelleConfirme: 'Acceptation...',
                  busy: _busy,
                  onConfirme: expired ? null : () => _run(widget.onAccept),
                ),
                const SizedBox(height: HbaSpacing.sm),
                HbaButton(
                  label: 'Refuser',
                  tone: HbaButtonTone.neutral,
                  onPressed: _busy ? null : () => _run(widget.onDecline),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  static String _distance(int meters) => meters < 1000
      ? '$meters m'
      : '${(meters / 1000).toStringAsFixed(1).replaceAll('.', ',')} km';
}

class _Row extends StatelessWidget {
  const _Row({required this.icon, required this.title, required this.value});

  final IconData icon;
  final String title;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // UNE TUILE CREUSEE, PAS UN APLAT POSE : l'aplat doux n'a presque
        // aucun ecart de luminance avec la matiere, c'est le creux qui lui
        // donne une forme.
        HbaCreux(
          radius: 14,
          color: HbaColors.primarySoft,
          child: SizedBox(
            height: 40,
            width: 40,
            child: Icon(icon, size: 20, color: HbaColors.primaryInk),
          ),
        ),
        const SizedBox(width: HbaSpacing.md),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(title, style: theme.textTheme.bodySmall),
              Text(
                value.isEmpty ? '--' : value,
                style: theme.textTheme.titleMedium,
              ),
            ],
          ),
        ),
      ],
    );
  }
}
