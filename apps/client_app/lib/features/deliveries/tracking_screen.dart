import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import '../profil/support.dart';
import 'annulation.dart';
import 'delivery_providers.dart';
import 'models.dart';
import 'recherche_livreur_vue.dart';

/// Suivi d'une livraison.
class TrackingScreen extends ConsumerStatefulWidget {
  const TrackingScreen({required this.deliveryId, super.key});

  final String deliveryId;

  @override
  ConsumerState<TrackingScreen> createState() => _TrackingScreenState();
}

class _TrackingScreenState extends ConsumerState<TrackingScreen> {
  Timer? _poller;

  @override
  void initState() {
    super.initState();
    _poller = Timer.periodic(
      const Duration(seconds: 10),
      (_) => ref.invalidate(deliveryProvider(widget.deliveryId)),
    );
  }

  @override
  void dispose() {
    _poller?.cancel();
    super.dispose();
  }


  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final async = ref.watch(deliveryProvider(widget.deliveryId));

    // L'ATTENTE A SON PROPRE ECRAN, PLEIN CADRE.
    //
    // Tant qu'aucun livreur n'est attribue, le client n'a rien a faire et rien
    // a decider : la liste de renseignements lui demande de chercher
    // l'information qui compte alors qu'il n'y en a qu'une. Des qu'un livreur
    // est la, en revanche, il y a un nom, un vehicule, un code de remise — et
    // la liste reprend tout son sens.
    final etat = async.valueOrNull?.status;
    final attente = etat == DeliveryStatus.searchingDriver ||
        etat == DeliveryStatus.noDriverFound;

    return Scaffold(
      // Pas de barre de titre pendant l'attente : la carte va jusqu'en haut,
      // et la navigation en arc reste accessible par-dessus.
      appBar: attente ? null : AppBar(title: const Text('Suivi')),
      body: async.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (error, _) => Center(
          child: Padding(
            padding: const EdgeInsets.all(HbaSpacing.gutter),
            child: Text(
              error is ApiException ? error.message : 'Suivi indisponible.',
              textAlign: TextAlign.center,
              style: theme.textTheme.bodyMedium,
            ),
          ),
        ),
        data: (delivery) => attente
            ? RechercheLivreurVue(
                delivery: delivery,
                onAnnuler: () => unawaited(annulerLaCourse(context, ref, delivery)),
              )
            : ListView(
          padding: const EdgeInsets.all(HbaSpacing.gutter),
          children: [
            // LE STATUT SUR SA PROPRE LIGNE, SOUS LA REFERENCE.
            //
            // Il etait a droite du titre, ou la barre de navigation flottante
            // venait le recouvrir, et ou un libelle long le comprimait a rien.
            // Sous le titre, il a toute la largeur et rien ne passe devant.
            Text(delivery.reference, style: theme.textTheme.headlineMedium),
            const SizedBox(height: HbaSpacing.sm),
            Row(
              children: [
                HbaChip(
                  label: delivery.status.label.toUpperCase(),
                  tone: switch (delivery.status) {
                    DeliveryStatus.delivered => HbaChipTone.success,
                    DeliveryStatus.cancelled ||
                    DeliveryStatus.failed ||
                    DeliveryStatus.noDriverFound ||
                    DeliveryStatus.paymentFailed =>
                      HbaChipTone.danger,
                    DeliveryStatus.unknown => HbaChipTone.neutral,
                    _ => HbaChipTone.primary,
                  },
                ),
              ],
            ),
            const SizedBox(height: HbaSpacing.xs),

            // CE QUE LE STATUT VEUT DIRE, EN UNE PHRASE. Un libelle d'etat
            // nomme la situation sans dire ce qu'elle implique : « Livreur sur
            // place » ne dit pas au client qu'on attend le colis. C'est cette
            // ligne-la qui evite l'appel au support.
            Text(
              switch (delivery.status) {
                DeliveryStatus.pendingPayment =>
                  'Terminez le paiement pour lancer la recherche. Sans '
                      'confirmation sous $delaiDePaiementMinutes minutes, la '
                      'commande est abandonnée.',
                DeliveryStatus.paymentFailed =>
                  "Le paiement n'a pas abouti. Aucun montant n'a été prélevé.",
                DeliveryStatus.paid =>
                  'Paiement confirmé. La recherche va commencer.',
                DeliveryStatus.searchingDriver =>
                  "Nous cherchons un livreur. Vous n'avez rien à faire.",
                DeliveryStatus.noDriverFound =>
                  // LE MEME TEXTE QUE L'ACCUEIL, MOT POUR MOT, et sans
                  // promesse de remboursement : FedaPay n'a pas d'API de
                  // remboursement (verifie le 30 septembre 2026, tableau de
                  // bord uniquement, MTN seulement). Promettre « vous serez
                  // rembourse » engageait un mecanisme qui n'existe pas.
                  //
                  // UNE FOIS LE REMBOURSEMENT CONSTATE, ON LE DIT. C'est le
                  // webhook du fournisseur qui l'apprend au service, apres que
                  // finance a rendu l'argent a la main.
                  delivery.estRemboursee
                      ? 'Aucun livreur disponible. Le montant vous a été rendu.'
                      : 'Aucun livreur disponible. HBA revient vers vous au '
                          'sujet du montant prélevé.',
                DeliveryStatus.driverAssigned =>
                  'Un livreur vient chercher le colis.',
                DeliveryStatus.driverAtPickup =>
                  'Le livreur est au point de collecte.',
                DeliveryStatus.pickedUp =>
                  'Le colis est en route vers le destinataire.',
                DeliveryStatus.delivered =>
                  'Le colis a été remis contre le code.',
                DeliveryStatus.cancelled => 'Cette course a été annulée.',
                DeliveryStatus.failed => "La course n'a pas pu aboutir.",
                DeliveryStatus.unknown =>
                  "État inconnu. Tirez vers le bas pour actualiser.",
              },
              style: theme.textTheme.bodyMedium
                  ?.copyWith(color: HbaColors.inkMuted),
            ),
            const SizedBox(height: HbaSpacing.lg),

            // PENDANT LA RECHERCHE, UN LIBELLE FIXE NE SUFFIT PLUS.
            //
            // Tant que la course etait diffusee a cinq livreurs a la fois,
            // elle partait en quelques dizaines de secondes et « Recherche
            // d'un livreur » tenait tout seul. Depuis le point 23, HBA
            // descend la liste un livreur a la fois : la recherche peut durer
            // plusieurs minutes. Un ecran qui cherche en silence pendant ce
            // temps se fait fermer avant la fin.

            if (delivery.showsOtp) ...[
              _OtpCard(code: delivery.deliveryOtp),
              const SizedBox(height: HbaSpacing.md),
            ],
            if (delivery.driver != null) ...[
              _DriverCard(driver: delivery.driver!),
              const SizedBox(height: HbaSpacing.md),
            ],
            HbaCard(
              padding: const EdgeInsets.all(HbaSpacing.lg),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  _Line(label: 'Collecte', value: delivery.pickupLandmark),
                  const SizedBox(height: HbaSpacing.md),
                  _Line(label: 'Livraison', value: delivery.dropoffLandmark),
                  const SizedBox(height: HbaSpacing.md),
                  _Line(
                    label: 'Destinataire',
                    value: '${delivery.recipientName} — ${delivery.recipientPhone}',
                  ),
                  const SizedBox(height: HbaSpacing.md),
                  _Line(label: 'Prix', value: Xof.format(delivery.totalXof)),
                ],
              ),
            ),
            // L'AIDE EST ICI, ET PAS SEULEMENT DANS L'ONGLET AIDE.
            //
            // Un client dont la course se passe mal ne va pas chercher un
            // autre onglet : il regarde l'ecran qui lui pose probleme. La
            // reference part dans l'objet du courriel — la recopier a la main
            // depuis le haut de l'ecran, il le ferait de travers une fois sur
            // cinq, et le support repondrait sur la mauvaise course.
            const SizedBox(height: HbaSpacing.md),
            AideSurLaCourse(reference: delivery.reference),

            if (delivery.status.canCancel) ...[
              const SizedBox(height: HbaSpacing.lg),
              HbaButton(
                label: 'Annuler la livraison',
                tone: HbaButtonTone.danger,
                onPressed: () => unawaited(annulerLaCourse(context, ref, delivery)),
              ),
            ],
            const SizedBox(height: HbaSpacing.lg),
          ],
        ),
      ),
    );
  }
}

/// Code de remise.
///
/// IL S'AFFICHE ICI, et seulement ici : c'est le client qui le transmet au
/// destinataire, par le moyen qu'il veut. Il disparait des que la course est
/// close.
class _OtpCard extends StatelessWidget {
  const _OtpCard({required this.code});

  final String code;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      highlighted: true,
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Code de remise', style: theme.textTheme.titleMedium),
          const SizedBox(height: HbaSpacing.xs),
          Text(
            'Communiquez-le au destinataire. Le livreur le lui demandera.',
            style: theme.textTheme.bodyMedium,
          ),
          const SizedBox(height: HbaSpacing.md),
          Row(
            children: [
              Expanded(
                child: Text(
                  code,
                  style: theme.textTheme.displaySmall?.copyWith(
                    color: HbaColors.primary,
                    letterSpacing: 8,
                  ),
                ),
              ),
              IconButton(
                onPressed: () async {
                  await Clipboard.setData(ClipboardData(text: code));
                  if (!context.mounted) return;
                  ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('Code copie.')),
                  );
                },
                icon: const Icon(Icons.copy_outlined),
                color: HbaColors.inkMuted,
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _DriverCard extends StatelessWidget {
  const _DriverCard({required this.driver});

  final AssignedDriver driver;

  /// Le pictogramme du vehicule qui vient vraiment.
  ///
  /// UNE MOTO ETAIT DESSINEE EN DUR, quel que soit le vehicule — et le type ne
  /// sortait meme pas de la passerelle, si bien que l'ecran n'avait aucun moyen
  /// de savoir. Depuis le 30 septembre 2026 le velo et le tricycle existent :
  /// un cycliste serait apparu en motard au client qui l'attend sur le pas de
  /// sa porte.
  ///
  /// LE REPLI EST LA MOTO, et c'est le bon : a Cotonou le zemidjan est le mode
  /// par defaut, et un vehicule non rendu par un service plus ancien y
  /// ressemble plus qu'a autre chose.
  static IconData _icone(String type) => switch (type) {
        'Car' => Icons.directions_car_outlined,
        'Van' => Icons.local_shipping_outlined,
        'Bicycle' => Icons.pedal_bike_outlined,
        'Tricycle' => Icons.electric_rickshaw_outlined,
        _ => Icons.two_wheeler_outlined,
      };

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Row(
        children: [
          Container(
            height: 44,
            width: 44,
            decoration: BoxDecoration(
              color: HbaColors.primarySoft,
              borderRadius: BorderRadius.circular(14),
            ),
            child: Icon(_icone(driver.vehicleType), color: HbaColors.primary),
          ),
          const SizedBox(width: HbaSpacing.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(driver.displayName, style: theme.textTheme.titleMedium),
                if (driver.vehiclePlate.isNotEmpty)
                  Text(driver.vehiclePlate, style: theme.textTheme.bodyMedium),
              ],
            ),
          ),
          if (driver.phone.isNotEmpty)
            Text(driver.phone, style: theme.textTheme.bodyMedium),
        ],
      ),
    );
  }
}

class _Line extends StatelessWidget {
  const _Line({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: theme.textTheme.bodySmall),
        const SizedBox(height: 2),
        Text(value.isEmpty ? '--' : value, style: theme.textTheme.titleMedium),
      ],
    );
  }
}

/// CE QUE LE CLIENT VOIT PENDANT QU'ON DESCEND LA LISTE DES LIVREURS.
///
/// PUBLIC, ET UNIQUEMENT POUR POUVOIR L'EPROUVER. Tous les autres blocs de cet
/// ecran sont prives, parce qu'ils n'ont qu'un seul appelant. Celui-ci porte
/// une minuterie et une regle de formatage a trois branches : deux choses qui
/// se cassent en silence et qu'aucun coup d'oeil a l'ecran ne rattrape.
///
/// POURQUOI CET ECRAN EXISTE. Le point 23 a remplace la diffusion simultanee
/// a cinq livreurs par un appel a la fois, du plus proche au plus eloigne.
/// C'est meilleur pour le client — il obtient le livreur le plus proche, pas
/// le plus rapide a appuyer — mais c'est plus long, et une attente longue
/// sans explication se lit comme une panne. Le chiffre qui avance est la
/// preuve que quelque chose se passe ; la phrase dit quoi.
///
/// L'HORODATAGE EST CELUI DE LA COMMANDE, PAS CELUI DE LA RECHERCHE, et le
/// libelle le dit. Le client ne recoit aujourd'hui que `createdAt` ; afficher
/// « Recherche depuis 2 min » alors qu'on mesure autre chose serait faux les
/// jours ou le paiement precede la recherche de quelques secondes. Un ecart
/// de quelques secondes n'a l'air de rien — jusqu'au jour ou il grandit et
/// ou le chiffre affiche ne correspond plus a rien de verifiable. Tant que le
/// service ne rend pas l'instant d'ouverture du dispatch, on annonce ce qu'on
/// mesure vraiment.
class RechercheEnCours extends StatefulWidget {
  const RechercheEnCours({required this.depuis, this.horloge, super.key});

  /// Instant de creation de la commande. NULLABLE : une reponse ancienne ou
  /// tronquee peut ne pas le porter. Dans ce cas la carte reste utile — elle
  /// perd le compteur, pas l'explication.
  final DateTime? depuis;

  /// D'OU VIENT « MAINTENANT ». Nul en production : c'est `DateTime.now`.
  ///
  /// CETTE COUTURE EXISTE PARCE QUE `tester.pump` NE FAIT PAS AVANCER L'HEURE.
  /// Il avance l'horloge FEINTE qui declenche les minuteries, pendant que
  /// `DateTime.now()` continue de rendre l'heure reelle. Un test qui avance
  /// d'une seconde verrait donc la minuterie battre et le compteur ne pas
  /// bouger — et conclurait a tort que le compteur est casse, ou pire,
  /// passerait pour la mauvaise raison. Sans cette couture, la seule chose que
  /// ce widget fait vraiment — avancer — est inverifiable.
  final DateTime Function()? horloge;

  @override
  State<RechercheEnCours> createState() => _RechercheEnCoursState();
}

class _RechercheEnCoursState extends State<RechercheEnCours> {
  /// UNE SECONDE, PARCE QUE C'EST CE QUI BOUGE. Un compteur qui saute de
  /// minute en minute passe le plus clair de son temps immobile, et un ecran
  /// immobile est un ecran qu'on soupconne d'etre bloque.
  static const _cadence = Duration(seconds: 1);

  Timer? _horloge;
  Duration _ecoule = Duration.zero;

  @override
  void initState() {
    super.initState();
    _recalculer();
    if (widget.depuis != null) {
      _horloge = Timer.periodic(_cadence, (_) => _recalculer());
    }
  }

  @override
  void didUpdateWidget(covariant RechercheEnCours ancien) {
    super.didUpdateWidget(ancien);
    if (ancien.depuis == widget.depuis) return;

    // La date a change (premiere reponse complete du service, par exemple) :
    // l'horloge doit suivre, et doit demarrer si elle n'existait pas encore.
    _horloge?.cancel();
    _horloge = null;
    _recalculer();
    if (widget.depuis != null) {
      _horloge = Timer.periodic(_cadence, (_) => _recalculer());
    }
  }

  @override
  void dispose() {
    _horloge?.cancel();
    super.dispose();
  }

  void _recalculer() {
    final debut = widget.depuis;
    if (debut == null) return;

    // LE SERVEUR RETARDE, LE TELEPHONE AUSSI. Si l'horloge du telephone est en
    // avance sur celle du service, l'ecart calcule est negatif ; on l'aplatit
    // a zero plutot que d'afficher un compteur qui recule.
    final maintenant = (widget.horloge ?? DateTime.now)();
    final ecart = maintenant.difference(debut.toLocal());
    final borne = ecart.isNegative ? Duration.zero : ecart;

    // On ne reconstruit que quand la SECONDE affichee change : le timer bat a
    // la seconde, mais un setState par tick sans changement visible est du
    // travail pur perdu sur un telephone d'entree de gamme.
    if (borne.inSeconds == _ecoule.inSeconds) return;
    if (!mounted) return;
    setState(() => _ecoule = borne);
  }

  /// « 45 s », « 2 min 05 », « 12 min ». Pas de « 00:02:05 » : un chronometre
  /// a deux-points evoque un compte a rebours, et rien ici ne se termine a
  /// zero.
  static String _duree(Duration d) {
    if (d.inMinutes < 1) return '${d.inSeconds} s';

    final minutes = d.inMinutes;
    final secondes = d.inSeconds % 60;
    if (minutes >= 10) return '$minutes min';
    return '$minutes min ${secondes.toString().padLeft(2, '0')}';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final compteur = widget.depuis == null ? null : _duree(_ecoule);

    // SUR LA CARTE, PLUS DANS UNE CARTE. Ce bloc etait une vignette dans une
    // liste ; il est desormais pose sur le plan, sous le halo. Les TEXTES
    // n'ont pas bouge d'une lettre : ils sont exacts — « un par un, du plus
    // proche au plus eloigne » decrit ce que fait le dispatch depuis le point
    // 23 — et sept tests les verifient. La maquette proposait « nous cherchons
    // le livreur le plus proche de vous » : plus court, moins vrai, et rien ne
    // l'aurait surveille.
    //
    // DU BLANC OMBRE, ET C'ETAIT UN PARI PERDU. La version precedente ecrivait
    // en blanc avec une ombre portee, au motif qu'un aplat semi-transparent
    // masquerait la carte. Le plan de cette application est le style CLAIR :
    // blanc sur blanc, l'ombre ne rattrape rien et le texte a disparu. Verifie
    // a l'ecran le 29 septembre 2026.
    //
    // UN PANNEAU OPAQUE, DONC, et de la couleur des autres cartes de
    // l'application. Il masque une bande de plan sous le halo — c'est le prix,
    // et il est sans commune mesure avec un ecran qu'on ne peut pas lire.
    return Container(
      margin: const EdgeInsets.symmetric(horizontal: HbaSpacing.sm),
      padding: const EdgeInsets.symmetric(
        horizontal: HbaSpacing.md,
        vertical: HbaSpacing.md,
      ),
      decoration: BoxDecoration(
        color: HbaColors.surface,
        borderRadius: BorderRadius.circular(HbaRadius.card),
        boxShadow: const [
          BoxShadow(color: Color(0x22000000), blurRadius: 16, offset: Offset(0, 4)),
        ],
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            'Recherche d\'un livreur',
            textAlign: TextAlign.center,
            style: theme.textTheme.titleLarge?.copyWith(
              fontWeight: FontWeight.w600,
            ),
          ),
          if (compteur != null) ...[
            const SizedBox(height: HbaSpacing.xs),
            // LE COMPTEUR EST ANNONCE POUR CE QU'IL EST. On mesure depuis la
            // commande, on l'ecrit.
            Text(
              'Commandée il y a $compteur',
              textAlign: TextAlign.center,
              style: theme.textTheme.bodyMedium
                  ?.copyWith(color: HbaColors.inkMuted),
            ),
          ],
          const SizedBox(height: HbaSpacing.sm),
          Text(
            'HBA contacte les livreurs un par un, du plus proche au plus '
            'éloigné. Chacun dispose de quelques secondes pour répondre.',
            textAlign: TextAlign.center,
            style: theme.textTheme.bodyMedium
                ?.copyWith(color: HbaColors.inkMuted),
          ),
        ],
      ),
    );
  }

}
