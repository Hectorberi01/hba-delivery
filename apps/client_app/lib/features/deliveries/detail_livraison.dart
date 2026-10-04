import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import '../commande/feuille_modale.dart';
import '../profil/support.dart';
import 'annulation.dart';
import 'carte_du_livreur.dart';
import 'delivery_providers.dart';
import 'etapes_envoi.dart';
import 'models.dart';
import 'recu.dart';
import 'suivi_temps_reel.dart';

/// Tout ce qu'il y a a savoir sur une livraison : la carte, l'etat, le livreur,
/// le code de remise, les points, le prix, l'annulation.
///
/// POURQUOI CE BLOC A ETE SORTI DE L'ECRAN DE SUIVI, le 4 octobre 2026. Il
/// vivait dans le corps de TrackingScreen, donc il n'existait qu'en PAGE
/// PLEINE. Depuis que l'accueil porte un bandeau « votre livreur arrive », le
/// geste naturel est d'appuyer dessus et de voir le detail LA, sans quitter la
/// carte : une feuille qui monte par-dessus plutot qu'une page qui la remplace.
/// Les deux presentations montrent desormais le meme bloc, et il n'y a rien a
/// tenir en double.
///
/// IL NE VA PAS CHERCHER LA LIVRAISON LUI-MEME. L'appelant la lui donne, parce
/// que l'un la tient d'un fournisseur par identifiant et l'autre d'une liste
/// deja chargee ; aller la relire ici ferait un deuxieme appel pour une donnee
/// qu'on a sous la main.
class DetailDeLaLivraison extends ConsumerWidget {
  const DetailDeLaLivraison({
    required this.delivery,
    this.avecCarte = true,
    super.key,
  });

  final Delivery delivery;

  /// La carte du livreur, en haut du bloc.
  ///
  /// FAUX EN FEUILLE, ET CE N'EST PAS UN CHOIX DE GOUT. La feuille se pose sur
  /// la carte d'accueil, qui est VIVANTE dessous — meme controleur, memes
  /// livreurs (voir PageEnFeuille). Une seconde carte dans la feuille afficherait
  /// le meme quartier a dix centimetres de la premiere, pour le prix d'une
  /// deuxieme vue native et de ses tuiles.
  final bool avecCarte;

  /// Ou en est la livraison sur le fil des trois temps, ou nul avant qu'un
  /// livreur soit affecte.
  ///
  /// « Livree » VAUT TROIS, HORS DES BORNES DU FIL, ET C'EST VOULU : toutes les
  /// pastilles portent alors leur coche, ce qui est exactement ce qu'on veut
  /// montrer d'une course terminee.
  int? get _jalon => switch (delivery.status) {
        DeliveryStatus.driverAssigned => 0,
        DeliveryStatus.driverAtPickup => 1,
        DeliveryStatus.pickedUp => 2,
        DeliveryStatus.delivered => 3,
        _ => null,
      };

  /// « dans 4 min », « dans moins d'une minute ».
  static String _duree(Duration duree) {
    final minutes = (duree.inSeconds / 60).round();
    return minutes < 1 ? 'dans moins d’une minute' : 'dans $minutes min';
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);

    return ListView(
      padding: const EdgeInsets.all(HbaSpacing.gutter),

      // EN FEUILLE, LA LISTE EPOUSE SON CONTENU. Sans cela elle prend tout ce
      // que la feuille lui accorde — quatre-vingts pour cent de l'ecran — meme
      // pour une livraison qui tient en six lignes.
      shrinkWrap: !avecCarte,
      children: [
        // LA CARTE EN PREMIER, ET SEULEMENT S'IL Y A UN POINT A MONTRER.
        //
        // « Ou en est-il » est la question qui fait ouvrir cet ecran ; le code de
        // remise et le telephone viennent apres, quand le livreur est la. La carte
        // s'efface d'elle-meme tant qu'aucun battement n'est arrive — et a la
        // cloture, puisque le serveur cesse alors d'en envoyer (point 37).
        if (avecCarte)
          CarteDuLivreur(
            depart: switch (delivery.pickupPoint) {
              final point? => LatLng(point.latitude, point.longitude),
              _ => null,
            },
            arrivee: switch (delivery.dropoffPoint) {
              final point? => LatLng(point.latitude, point.longitude),
              _ => null,
            },
            vehicule: delivery.driver?.vehicleType,
            suivi: ref.watch(suiviLivraisonProvider(delivery.id)),

            // VIDE TANT QUE LA LECTURE N'A PAS DEMANDE LA GEOMETRIE : la carte
            // retombe alors sur ses deux epingles, sans trait.
            trace: delivery.itineraire?.trace ?? '',
          ),

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

        // L'ARRIVEE ESTIMEE, SOUS LA PHRASE D'ETAT ET AU-DESSUS DU FIL.
        //
        // ELLE NE PARAIT QUE SI ELLE EST MESUREE. Delivery.arriveeDans rend nul
        // quand la duree vient du repli a vol d'oiseau : une estimation
        // geometrique sous-estime d'environ un tiers en ville, et le client
        // juge le service a la minute pres sur ce chiffre-la.
        if (delivery.arriveeDans case final duree?) ...[
          const SizedBox(height: HbaSpacing.sm),
          Row(
            children: [
              const Icon(
                Icons.schedule,
                size: 18,
                color: HbaColors.primary,
              ),
              const SizedBox(width: HbaSpacing.xs),
              Text(
                'Arrivée estimée ${_duree(duree)}',
                style: theme.textTheme.bodyMedium?.copyWith(
                  fontWeight: FontWeight.w600,
                  color: HbaColors.primary,
                ),
              ),
            ],
          ),
        ],
        const SizedBox(height: HbaSpacing.lg),

        // LES TROIS TEMPS DE LA LIVRAISON, DES QU'UN LIVREUR EST LA.
        //
        // AVANT L'AFFECTATION, IL N'Y A RIEN A JALONNER : la recherche a son
        // propre ecran, et un fil dont aucune etape n'est atteinte dirait
        // seulement que rien n'a commence — ce que la phrase d'etat dit deja.
        //
        // IL REPREND LE BANDEAU DU FORMULAIRE D'ENVOI. Le client a vu ces
        // pastilles en commandant ; les redessiner autrement ici lui ferait
        // apprendre deux fois la meme grammaire.
        if (_jalon != null) ...[
          BandeauDEtapes(
            etapes: const [
              EtapeDEnvoi(titre: 'En route', sousTitre: 'vers la collecte'),
              EtapeDEnvoi(titre: 'Collecte', sousTitre: 'du colis'),
              EtapeDEnvoi(titre: 'Livraison', sousTitre: 'au destinataire'),
            ],
            courante: _jalon!,
            onRevenir: (_) {},
          ),
          const SizedBox(height: HbaSpacing.lg),
        ],

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

        // LE RECU N'APPARAIT QU'UNE FOIS LA COURSE PAYEE ET CLOSE. Avant,
        // il n'y a rien a justifier ; une commande abandonnee avant
        // paiement afficherait un papier vide.
        if (delivery.aUnRecu) ...[
          const SizedBox(height: HbaSpacing.lg),
          HbaButton(
            label: 'Voir le reçu',
            tone: HbaButtonTone.neutral,
            icon: Icons.receipt_long_outlined,
            onPressed: () => unawaited(RecuDeCourse.ouvrir(context, delivery)),
          ),
        ],

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

/// Le detail, pose en feuille sur la carte d'accueil.
///
/// POURQUOI UNE ROUTE ET NON showModalBottomSheet. C'est la doctrine de
/// PageEnFeuille, et elle vaut ici comme ailleurs : un ecran sorti du routeur
/// n'existe plus pour la reprise de paiement, pour un lien profond, ni pour le
/// retour materiel d'Android. La feuille est une PRESENTATION, pas une
/// exception.
///
/// L'ECRAN PLEIN SURVIT, ET IL LE FAUT. « /suivi/:id » reste la cible des
/// notifications, de la liste des courses et de l'attente — qui, elle, se joue
/// plein cadre avec sa propre carte. Une feuille de quatre-vingts pour cent ne
/// sait pas porter une carte plein ecran.
class FeuilleDuSuivi extends ConsumerStatefulWidget {
  const FeuilleDuSuivi({required this.deliveryId, super.key});

  final String deliveryId;

  @override
  ConsumerState<FeuilleDuSuivi> createState() => _FeuilleDuSuiviState();
}

class _FeuilleDuSuiviState extends ConsumerState<FeuilleDuSuivi> {
  /// LA FEUILLE SE RELIT TOUTE SEULE, COMME L'ECRAN PLEIN.
  ///
  /// Sans cela, un client qui laisse la feuille ouverte verrait l'etat fige au
  /// moment ou il l'a ouverte : le livreur arriverait au point de collecte sans
  /// que rien ne bouge sous ses yeux.
  ///
  /// ELLE NE TOUCHE PAS A « surChangementDEtat ». Ce rappel n'a qu'une place
  /// dans le notificateur, et c'est l'ecran plein qui la tient : la lui prendre
  /// au passage le laisserait sourd apres la fermeture de la feuille. Trente
  /// secondes suffisent ici — la position, elle, continue d'arriver en direct.
  static const _cadence = Duration(seconds: 30);

  Timer? _poller;

  @override
  void initState() {
    super.initState();
    _poller = Timer.periodic(
      _cadence,
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
    final async = ref.watch(deliveryProvider(widget.deliveryId));

    return FeuilleModale(
      titre: 'Suivi de livraison',
      icone: Icons.local_shipping_outlined,
      enfant: async.when(
        loading: () => const Padding(
          padding: EdgeInsets.all(HbaSpacing.xl),
          child: Center(child: CircularProgressIndicator()),
        ),
        error: (error, _) => Padding(
          padding: const EdgeInsets.all(HbaSpacing.gutter),
          child: Text(
            error is ApiException ? error.message : 'Suivi indisponible.',
            textAlign: TextAlign.center,
            style: Theme.of(context).textTheme.bodyMedium,
          ),
        ),

        // SANS LA CARTE : celle de l'accueil est vivante derriere la feuille.
        data: (delivery) =>
            DetailDeLaLivraison(delivery: delivery, avecCarte: false),
      ),
    );
  }
}
