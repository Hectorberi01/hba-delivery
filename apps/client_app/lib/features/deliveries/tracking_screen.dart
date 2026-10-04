import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import 'annulation.dart';
import 'delivery_providers.dart';
import 'detail_livraison.dart';
import 'etapes_envoi.dart';
import 'models.dart';
import 'recherche_livreur_vue.dart';
import 'suivi_temps_reel.dart';

/// Suivi d'une livraison.
class TrackingScreen extends ConsumerStatefulWidget {
  const TrackingScreen({required this.deliveryId, super.key});

  final String deliveryId;

  @override
  ConsumerState<TrackingScreen> createState() => _TrackingScreenState();
}

class _TrackingScreenState extends ConsumerState<TrackingScreen> {
  /// Cadence du sondage DEPUIS QUE LE TEMPS REEL EXISTE.
  ///
  /// ELLE EST PASSEE DE DIX A TRENTE SECONDES, ET LE SONDAGE N'A PAS DISPARU. Le
  /// service de diffusion consomme Kafka au PLUS RECENT et ne rejoue rien : un
  /// changement d'etat emis pendant une coupure — la sienne, ou celle du telephone
  /// — est perdu pour toujours. Le temps reel est donc un CONFORT, et ce sondage
  /// est la VERITE. Le supprimer parce que le direct marche reviendrait a echanger
  /// un ecran lent contre un ecran faux, sur un reseau ou la coupure est la norme.
  ///
  /// TRENTE SECONDES ET NON SOIXANTE : c'est aussi ce qui fait vieillir l'age de la
  /// position affiche sous la carte, et une minute de retard sur « le flux s'est
  /// tu » serait trop long pour etre utile.
  static const _cadenceDeSondage = Duration(seconds: 30);

  Timer? _poller;

  @override
  void initState() {
    super.initState();

    _poller = Timer.periodic(
      _cadenceDeSondage,
      (_) => ref.invalidate(livraisonAvecItineraireProvider(widget.deliveryId)),
    );

    // LE SIGNAL DU SERVEUR RELIT LA MISSION, IL NE LA REMPLACE PAS.
    //
    // La trame « etat » dit « ceci a change » et ne porte ni le nom du livreur, ni
    // son telephone, ni le code de remise : la matrice de visibilite est appliquee
    // dans les services et a la passerelle, pas dans une trame. On relit donc par
    // la route REST, exactement comme le sondage — simplement a la seconde ou le
    // changement arrive, au lieu de l'attendre jusqu'a trente.
    //
    // APRES LA CONSTRUCTION, PAS PENDANT : lire un notificateur dans initState
    // declencherait une modification d'etat au premier cadre.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;

      ref.read(suiviLivraisonProvider(widget.deliveryId).notifier).surChangementDEtat =
          () {
        if (mounted) {
          ref.invalidate(livraisonAvecItineraireProvider(widget.deliveryId));
        }
      };
    });
  }

  @override
  void dispose() {
    _poller?.cancel();
    super.dispose();
  }


  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    // L'ECRAN PLEIN DESSINE LA ROUTE, DONC IL DEMANDE LA GEOMETRIE. La feuille
    // de suivi, elle, n'affiche pas de carte — la carte d'accueil vit derriere —
    // et garde deliveryProvider, qui ne transporte aucun trace.
    final async = ref.watch(livraisonAvecItineraireProvider(widget.deliveryId));

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
            ? RechercheLivreurVue.pourLivraison(
                delivery: delivery,
                onAnnuler: () => unawaited(annulerLaCourse(context, ref, delivery)),
              )
            : DetailDeLaLivraison(delivery: delivery),
      ),
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
  const RechercheEnCours({
    required this.depuis,
    this.horloge,
    this.vignette,
    super.key,
  });

  /// Le halo anime, pose a gauche du titre.
  ///
  /// L'APPELANT LE FOURNIT, LA CARTE NE LE CONNAIT PAS. Il bat a soixante
  /// images par seconde et vit dans l'ecran de recherche ; l'importer ici
  /// ferait dependre une carte de texte — testee par sept tests qui ne
  /// pompent aucune animation — d'un controleur d'animation.
  ///
  /// NUL, LE BLOC RESTE CENTRE comme avant. C'est ainsi que les tests le
  /// construisent, et c'est encore la bonne mise en page quand rien ne vient
  /// occuper la gauche.
  final Widget? vignette;

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
    final vignette = widget.vignette;
    final centre = vignette == null;

    final titres = Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment:
          centre ? CrossAxisAlignment.center : CrossAxisAlignment.start,
      children: [
        Text(
          'Recherche d\'un livreur',
          textAlign: centre ? TextAlign.center : TextAlign.start,
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
            textAlign: centre ? TextAlign.center : TextAlign.start,
            style: theme.textTheme.bodyMedium
                ?.copyWith(color: HbaColors.inkMuted),
          ),
        ],
      ],
    );

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
          if (vignette == null)
            titres
          else
            Row(
              children: [
                vignette,
                const SizedBox(width: HbaSpacing.sm),
                Expanded(child: titres),
              ],
            ),
          const SizedBox(height: HbaSpacing.md),

          // LES TROIS TEMPS DE L'ATTENTE, ET LE PREMIER EST TOUJOURS CELUI-CI.
          //
          // CETTE CARTE N'EXISTE QUE PENDANT LA RECHERCHE : des qu'un livreur
          // accepte, l'ecran change. L'etape courante est donc constante, et
          // ce n'est pas une barre de progression — c'est la REPONSE a « et
          // apres ? », posee avant que le client ne se demande si l'ecran est
          // bloque.
          BandeauDEtapes(
            etapes: const [
              EtapeDEnvoi(titre: 'Recherche', sousTitre: 'd’un livreur'),
              EtapeDEnvoi(titre: 'Un livreur', sousTitre: 'accepte'),
              EtapeDEnvoi(titre: 'En préparation', sousTitre: 'de l’enlèvement'),
            ],
            courante: 0,

            // RIEN OU REVENIR : ce ne sont pas des etapes qu'on remplit, ce
            // sont des moments qu'on attend.
            onRevenir: (_) {},
          ),
          const SizedBox(height: HbaSpacing.md),
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
