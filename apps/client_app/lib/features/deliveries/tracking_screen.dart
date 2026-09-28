import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import 'delivery_providers.dart';
import 'models.dart';

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

  Future<void> _cancel(Delivery delivery) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Annuler la livraison ?'),
        // Aucun montant n'est annonce ici : la politique d'annulation n'est pas
        // tranchee, et promettre un remboursement que le service ne fera pas
        // serait pire que de ne rien dire.
        content: const Text(
          "Le livreur ne viendra pas. Les conditions de remboursement vous "
          "seront confirmees par le service.",
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(false),
            child: const Text('Garder'),
          ),
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(true),
            child: const Text('Annuler la course'),
          ),
        ],
      ),
    );

    if (confirmed != true || !mounted) return;

    try {
      await ref
          .read(deliveryRepositoryProvider)
          .cancel(delivery.id, 'Annulee par le client');
      ref.invalidate(deliveryProvider(widget.deliveryId));
    } on ApiException catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(error.message)));
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final async = ref.watch(deliveryProvider(widget.deliveryId));

    return Scaffold(
      appBar: AppBar(title: const Text('Suivi')),
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
        data: (delivery) => ListView(
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
                  'Terminez le paiement pour lancer la recherche.',
                DeliveryStatus.paymentFailed =>
                  "Le paiement n'a pas abouti. Aucun montant n'a ete preleve.",
                DeliveryStatus.paid =>
                  'Paiement confirme. La recherche va commencer.',
                DeliveryStatus.searchingDriver =>
                  "Nous cherchons un livreur. Vous n'avez rien a faire.",
                DeliveryStatus.noDriverFound =>
                  'Aucun livreur disponible. Vous serez rembourse.',
                DeliveryStatus.driverAssigned =>
                  'Un livreur vient chercher le colis.',
                DeliveryStatus.driverAtPickup =>
                  'Le livreur est au point de collecte.',
                DeliveryStatus.pickedUp =>
                  'Le colis est en route vers le destinataire.',
                DeliveryStatus.delivered =>
                  'Le colis a ete remis contre le code.',
                DeliveryStatus.cancelled => 'Cette course a ete annulee.',
                DeliveryStatus.failed => "La course n'a pas pu aboutir.",
                DeliveryStatus.unknown =>
                  "Etat inconnu. Tirez vers le bas pour actualiser.",
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
            if (delivery.status == DeliveryStatus.searchingDriver) ...[
              RechercheEnCours(depuis: delivery.createdAt),
              const SizedBox(height: HbaSpacing.md),
            ],

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
            if (delivery.status.canCancel) ...[
              const SizedBox(height: HbaSpacing.lg),
              HbaButton(
                label: 'Annuler la livraison',
                tone: HbaButtonTone.danger,
                onPressed: () => _cancel(delivery),
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
            child: const Icon(Icons.two_wheeler_outlined, color: HbaColors.primary),
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

    return HbaCard(
      highlighted: true,
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const SizedBox(
                height: 18,
                width: 18,
                child: CircularProgressIndicator(
                  strokeWidth: 2,
                  color: HbaColors.primary,
                ),
              ),
              const SizedBox(width: HbaSpacing.sm),
              Expanded(
                child: Text(
                  'Recherche d\'un livreur',
                  style: theme.textTheme.titleMedium,
                ),
              ),
            ],
          ),
          if (compteur != null) ...[
            const SizedBox(height: HbaSpacing.sm),
            // LE COMPTEUR EST ANNONCE POUR CE QU'IL EST. Voir plus haut : on
            // mesure depuis la commande, on l'ecrit.
            Text(
              'Commandee il y a $compteur',
              style: theme.textTheme.bodyMedium?.copyWith(
                color: HbaColors.inkMuted,
              ),
            ),
          ],
          const SizedBox(height: HbaSpacing.sm),
          Text(
            'HBA contacte les livreurs un par un, du plus proche au plus '
            'eloigne. Chacun dispose de quelques secondes pour repondre.',
            style: theme.textTheme.bodyMedium,
          ),
        ],
      ),
    );
  }
}
