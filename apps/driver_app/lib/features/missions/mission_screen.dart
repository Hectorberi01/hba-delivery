import 'package:hba_core/hba_core.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_ui/hba_ui.dart';

import '../../core/navigation_externe.dart';
import '../../core/plantages.dart';
import '../profil/profil_repository.dart';
import 'carte_course.dart';
import 'mission_providers.dart';
import 'mission_repository.dart';
import 'models.dart';

/// Un point exploitable, ou null.
///
/// ZERO, ZERO EST UNE ADRESSE ABSENTE DE LA REPONSE, pas un point au large du
/// Benin : avant acceptation le BFF rend des champs vides, et protobuf rend
/// zero plutot que rien. Une carte centree la-dessus montrerait l'ocean.
LatLng? _pointDe(Place? lieu) =>
    lieu == null || (lieu.latitude == 0 && lieu.longitude == 0)
        ? null
        : LatLng(lieu.latitude, lieu.longitude);

/// Course en cours. Un seul bouton a la fois, celui de l'etape suivante.
class MissionScreen extends ConsumerStatefulWidget {
  const MissionScreen({required this.mission, required this.onClosed, super.key});

  final Mission mission;
  final VoidCallback onClosed;

  @override
  ConsumerState<MissionScreen> createState() => _MissionScreenState();
}

class _MissionScreenState extends ConsumerState<MissionScreen> {
  late Mission _mission = widget.mission;
  bool _busy = false;
  String? _error;

  Future<void> _advance() async {
    final repository = ref.read(missionRepositoryProvider);

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      final issue = switch (_mission.status) {
        DeliveryStatus.driverAssigned => await repository.markArrived(_mission.id),
        DeliveryStatus.driverAtPickup => await repository.markPickedUp(_mission.id),
        _ => const Issue(),
      };

      if (!mounted) return;

      if (issue.mission case final aJour?) {
        setState(() => _mission = aJour);
        return;
      }

      if (issue.miseEnFile) {
        // L'ETAPE AVANCE QUAND MEME, ET SEULEMENT POUR CES DEUX-LA. « Je suis
        // arrive » et « j'ai pris le colis » sont des constats : le serveur ne
        // peut pas les contredire, il ne fait que les enregistrer. Bloquer le
        // livreur sur un bouton jusqu'au retour du reseau l'empecherait de
        // finir sa course dans un parking souterrain.
        //
        // LA REMISE, ELLE, N'AVANCE PAS — voir _confirmDelivery. Le serveur y
        // valide un code, donc il PEUT contredire.
        setState(() {
          _mission = _mission.avecStatut(
            _mission.status == DeliveryStatus.driverAssigned
                ? DeliveryStatus.driverAtPickup
                : DeliveryStatus.pickedUp,
          );
          _error = 'Pas de réseau. L étape est enregistrée et repartira toute '
              'seule dès que la connexion revient.';
        });
      }
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _confirmDelivery() async {
    final otp = await showModalBottomSheet<String>(
      context: context,
      // MEME RAISON QUE LA FEUILLE DE RECAPITULATIF : les onglets ont chacun
      // leur Navigator, loge au-dessus de la barre du bas. Sans ceci la
      // feuille se dessine sous la barre et son bas devient inatteignable.
      useRootNavigator: true,
      isScrollControlled: true,
      // LA FEUILLE EST DE LA COULEUR DU FOND, pas blanche : les blocs
      // qu'elle contient sont en relief, et un relief ne se lit que
      // s'il sort de la meme matiere que le reste.
      backgroundColor: HbaColors.background,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(HbaRadius.card)),
      ),
      builder: (_) => const _OtpSheet(),
    );

    if (otp == null || !mounted) return;

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      final issue =
          await ref.read(missionRepositoryProvider).confirmDelivery(_mission.id, otp);

      if (!mounted) return;

      if (issue.miseEnFile) {
        // LA REMISE NE S'AFFICHE PAS COMME FAITE TANT QUE LE SERVEUR N'A PAS
        // VALIDE LE CODE. C'est la difference avec les deux etapes
        // precedentes : ici le serveur peut refuser — un code mal entendu, mal
        // recopie. Annoncer « course livree » et afficher la remuneration
        // serait promettre au livreur quelque chose qui n'est peut-etre pas
        // arrive.
        setState(() => _error = 'Pas de réseau. La remise est enregistrée et '
            'partira toute seule ; elle sera confirmee au retour de la '
            'connexion.');
        return;
      }

      final updated = issue.mission;
      if (updated == null) return;

      setState(() => _mission = updated);

      Plantages.trace('colis remis');

      if (updated.status == DeliveryStatus.delivered) {
        // LE RECAPITULATIF PASSE AVANT LA FERMETURE. Jusqu'ici l'ecran se
        // refermait sec : le livreur remettait un colis et se retrouvait sur
        // la carte, sans que rien ne lui dise ce que la course lui avait
        // rapporte. Il devait aller le chercher dans l'onglet Gains, ou ne
        // pas le savoir.
        await _montrerLeRecapitulatif(updated);
        widget.onClosed();

        // L'ECRAN RESTAIT OUVERT SUR UNE COURSE FINIE, ET C'EST LE DEFAUT
        // QU'ON CORRIGE. « onClosed » ne fait qu'effacer la course de
        // l'accueil — la route de cet ecran, elle, avait ete empilee dessus
        // et personne ne la depilait. Le livreur se retrouvait devant le
        // detail d'une course livree, sans bouton, sans rien a y faire, et
        // devait trouver la fleche de retour.
        if (mounted) Navigator.of(context).pop();
      }
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _montrerLeRecapitulatif(Mission mission) async {
    if (!mounted) return;

    await showModalBottomSheet<void>(
      context: context,

      // LA BARRE D'ONGLETS RECOUVRAIT LE BAS DE LA FEUILLE, BOUTON COMPRIS.
      //
      // L'application est batie sur un StatefulShellRoute : chaque onglet a
      // SON PROPRE Navigator, loge dans le corps du Scaffold, au-dessus de la
      // barre. Une feuille ouverte sur ce Navigator-la ne connait donc pas la
      // barre et se dessine dessous. Le livreur voyait « Terminer » depasser
      // derriere « Accueil », sans pouvoir l'atteindre.
      //
      // « useRootNavigator » remonte la feuille au Navigator de l'application
      // entiere : elle couvre alors tout l'ecran, barre comprise, ce qui est
      // de toute facon ce qu'on veut d'un ecran dont on ne peut pas sortir
      // sans lire.
      useRootNavigator: true,

      // SANS CECI LA FEUILLE EST PLAFONNEE A 9/16 DE L'ECRAN et son contenu
      // est coupe en silence sur un telephone court.
      isScrollControlled: true,

      // ON NE S'EN DEBARRASSE PAS D'UN GESTE DE COTE. C'est le seul moment ou
      // le livreur voit ce qu'il a gagne ; une feuille qu'on balaie par
      // reflexe en refermant la precedente la ferait disparaitre sans qu'il
      // l'ait lue.
      isDismissible: false,
      enableDrag: false,
      backgroundColor: HbaColors.background,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(HbaRadius.card)),
      ),
      builder: (_) => _Recapitulatif(mission: mission),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final mission = _mission;
    final action = mission.status.nextDriverAction;
    final atDelivery = mission.status == DeliveryStatus.pickedUp;

    return Scaffold(
      appBar: AppBar(title: Text(mission.reference)),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.symmetric(horizontal: HbaSpacing.gutter),
          children: [
            HbaCard(
              padding: const EdgeInsets.all(HbaSpacing.lg),
              child: Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('Votre rémunération',
                            style: theme.textTheme.bodyMedium),
                        Text(
                          Xof.format(mission.driverEarningXof),
                          style: theme.textTheme.headlineMedium
                              ?.copyWith(color: HbaColors.primaryInk),
                        ),
                      ],
                    ),
                  ),
                  HbaChip(label: _statusLabel(mission.status)),
                ],
              ),
            ),
            const SizedBox(height: HbaSpacing.md),

            // SITUER LA COURSE SANS QUITTER L'APPLICATION. Le guidage reste
            // l'affaire de Waze ou de Plans — ils connaissent le trafic et
            // parlent, ce qu'une carte maison ne fera pas de sitot. Ce qui
            // manquait, c'etait de voir d'un coup d'oeil ou va la course
            // avant de lancer la navigation.
            if (_pointDe(mission.pickup) case final collecte?) ...[
              CarteCourse(
                collecte: collecte,
                depot: _pointDe(mission.dropoff),

                // Le profil est deja en memoire a ce stade — la course est
                // acceptee, l'application tourne depuis un moment. Rien a
                // attendre, et rien a casser s'il manque : l'epingle reste
                // alors un disque nu.
                vehicule: ref.watch(profilProvider).valueOrNull?.vehicule?.type,
              ),
              const SizedBox(height: HbaSpacing.xs),
              Text(
                _pointDe(mission.dropoff) == null
                    ? "Le point de livraison s'affiche une fois la course acceptée."
                    : "Le trait est à vol d'oiseau, ce n'est pas l'itinéraire : « Y aller » ouvre votre application de navigation.",
                style: theme.textTheme.bodySmall,
              ),
              const SizedBox(height: HbaSpacing.md),
            ],

            _PlaceCard(
              title: 'Collecte',
              place: mission.pickup,
              icon: Icons.storefront_outlined,
            ),
            const SizedBox(height: HbaSpacing.md),
            _PlaceCard(
              title: 'Livraison',
              place: mission.dropoff,
              icon: Icons.flag_outlined,
              // Avant acceptation le BFF renvoie des champs vides : l'ecran
              // doit le vivre sans supposer leur presence.
              fallback: "Adresse communiquée après acceptation.",
            ),
            const SizedBox(height: HbaSpacing.md),

            // LA CARTE NE S'AFFICHE QUE SI ELLE DIT QUELQUE CHOSE DE NEUF.
            //
            // « Destinataire » et le contact de la carte « Livraison » sont
            // DEUX CHAMPS DISTINCTS DU CONTRAT, et ce n'est pas une subtilite
            // d'implementation : « Location.contact_name » designe la personne
            // PRESENTE sur le lieu — un gardien, une secretaire, le comptoir
            // d'une boutique —, « recipient_name » celle a qui le colis
            // revient. Les fondre en un seul champ se decide dans le contrat,
            // pas dans cet ecran.
            //
            // MAIS QUAND LES DEUX PORTENT LA MEME VALEUR, les afficher deux
            // fois est du bruit — et c'est le cas de la plupart des courses,
            // ou le client saisit la meme personne aux deux endroits. La carte
            // ne garde alors que ce que l'autre ne dit pas : le colis.
            if (!_memeQueLeContact(mission) ||
                mission.packageDescription.isNotEmpty) ...[
              HbaCard(
                padding: const EdgeInsets.all(HbaSpacing.lg),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (!_memeQueLeContact(mission)) ...[
                      Text('Destinataire', style: theme.textTheme.bodySmall),
                      Text(
                        mission.recipientName.isEmpty
                            ? '--'
                            : mission.recipientName,
                        style: theme.textTheme.titleMedium,
                      ),
                      if (mission.recipientPhone.isNotEmpty) ...[
                        Text(mission.recipientPhone,
                            style: theme.textTheme.bodyMedium),
                        const SizedBox(height: HbaSpacing.md),
                        _BoutonAppel(numero: mission.recipientPhone),
                      ],
                      if (mission.packageDescription.isNotEmpty)
                        const SizedBox(height: HbaSpacing.md),
                    ],
                    if (mission.packageDescription.isNotEmpty) ...[
                      Text('Colis', style: theme.textTheme.bodySmall),
                      Text(mission.packageDescription,
                          style: theme.textTheme.bodyMedium),
                    ],
                  ],
                ),
              ),
            ],
            if (_error != null) ...[
              const SizedBox(height: HbaSpacing.md),
              Text(
                _error!,
                style: theme.textTheme.bodyMedium
                    ?.copyWith(color: HbaColors.danger),
              ),
            ],
            const SizedBox(height: HbaSpacing.lg),
            if (action != null)
              // CHAQUE ETAPE SE GLISSE. « Je suis au point de collecte »
              // envoye par erreur fausse l'horodatage de la course ; « J'ai
              // recupere le colis » declare un colis en main qu'on n'a pas
              // encore. Ces trois gestes se font en roulant ou en tenant un
              // carton : le glissement est le seul qui ne parte pas tout
              // seul.
              HbaGlissiere(
                libelle: action,
                libelleConfirme: 'Envoi...',
                busy: _busy,
                onConfirme: atDelivery ? _confirmDelivery : _advance,
              ),
            const SizedBox(height: HbaSpacing.lg),
          ],
        ),
      ),
    );
  }

  /// Le destinataire porte-t-il exactement le contact du lieu de livraison.
  ///
  /// COMPARAISON EXACTE, SANS NORMALISATION, ET C'EST DELIBERE. Rapprocher
  /// « +229 01 97 … » de « 0197… » demanderait de decider ce qu'est le meme
  /// numero au Benin — indicatif implicite, ancien format a huit chiffres,
  /// espaces. Se tromper la-dessus ferait disparaitre de l'ecran un
  /// destinataire QUI N'EST PAS le contact sur place. En cas de doute on
  /// affiche les deux : du bruit se supporte, une personne cachee non.
  static bool _memeQueLeContact(Mission mission) {
    final lieu = mission.dropoff;

    return mission.recipientName == (lieu?.contactName ?? '') &&
        mission.recipientPhone == (lieu?.phone ?? '');
  }

  static String _statusLabel(DeliveryStatus status) => switch (status) {
        DeliveryStatus.driverAssigned => 'AFFECTEE',
        DeliveryStatus.driverAtPickup => 'SUR PLACE',
        DeliveryStatus.pickedUp => 'EN COURS',
        DeliveryStatus.delivered => 'REMISE',
        _ => '--',
      };
}

class _PlaceCard extends StatelessWidget {
  const _PlaceCard({
    required this.title,
    required this.place,
    required this.icon,
    this.fallback = '--',
  });

  final String title;
  final Place? place;
  final IconData icon;
  final String fallback;

  /// « Nom — numero », ou l'un des deux seul, ou rien.
  static String _contact(Place lieu) {
    if (lieu.contactName.isEmpty) return lieu.phone;
    if (lieu.phone.isEmpty) return lieu.contactName;

    return '${lieu.contactName} — ${lieu.phone}';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final value = place;

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, color: HbaColors.primaryInk),
          const SizedBox(width: HbaSpacing.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title, style: theme.textTheme.bodySmall),
                const SizedBox(height: HbaSpacing.xs),
                Text(
                  value == null || value.landmark.isEmpty
                      ? fallback
                      : value.landmark,
                  style: theme.textTheme.titleMedium,
                ),
                // LE CONTACT SUR PLACE EST CELUI QU'ON APPELLE EN PREMIER.
                // Au retrait c'est le commercant derriere son comptoir, a la
                // livraison la personne qui doit descendre ouvrir.
                //
                // LE NUMERO EST ECRIT ICI, PAS DANS LE BOUTON. Il y etait, et
                // les deux pastilles n'entraient plus cote a cote sur un
                // telephone de 360 dp : celle qui portait quatorze chiffres
                // faisait deux fois la largeur de « Y aller ». Il se lit donc
                // au-dessus, ou il sert aussi a qui veut le recopier.
                if (value != null && _contact(value).isNotEmpty) ...[
                  const SizedBox(height: HbaSpacing.xs),
                  Text(_contact(value), style: theme.textTheme.bodyMedium),
                ],
                if (value != null && value.notes.isNotEmpty) ...[
                  const SizedBox(height: HbaSpacing.xs),
                  Text(value.notes, style: theme.textTheme.bodyMedium),
                ],

                // LES DEUX GESTES DU LIEU, SUR UNE SEULE LIGNE ET DE MEME
                // LARGEUR. Ils repondent a la meme question — comment
                // j'atteins cet endroit —, l'un par la route, l'autre par le
                // telephone. Empiles l'un sous l'autre, ils se lisaient comme
                // deux etapes successives.
                //
                // UN POINT A ZERO N'EST PAS UN POINT. Une Location absente de
                // la reponse arrive en 0,0 — au large du golfe de Guinee, a
                // six cents kilometres de Cotonou. Y envoyer un livreur
                // serait pire que ne rien proposer : « Y aller » disparait
                // alors, et « Appeler » occupe la ligne seul.
                if (value != null &&
                    (value.phone.isNotEmpty ||
                        value.latitude != 0 ||
                        value.longitude != 0)) ...[
                  const SizedBox(height: HbaSpacing.md),
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      if (value.phone.isNotEmpty)
                        Expanded(child: _BoutonAppel(numero: value.phone)),
                      if (value.phone.isNotEmpty &&
                          (value.latitude != 0 || value.longitude != 0))
                        const SizedBox(width: HbaSpacing.sm),
                      if (value.latitude != 0 || value.longitude != 0)
                        Expanded(
                          child: _BoutonItineraire(
                            place: value,
                            etiquette: title,
                          ),
                        ),
                    ],
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// « Appeler » : passe la main au composeur du telephone.
///
/// AUCUNE DONNEE NOUVELLE N'EST EXPOSEE ICI. Le numero du destinataire et
/// celui du contact sur place etaient deja affiches en clair ; la matrice de
/// visibilite les accorde au livreur affecte. Ce bloc n'ajoute qu'un geste :
/// un appui la ou il fallait auparavant memoriser dix chiffres, sortir de
/// l'application et les recomposer.
///
/// LE NUMERO RESTE VISIBLE SOUS LE LIBELLE, et c'est voulu. Un livreur dont
/// le telephone n'a pas d'application d'appel, ou qui veut envoyer un message
/// depuis un autre appareil, doit pouvoir le lire sans appuyer.
class _BoutonAppel extends StatefulWidget {
  const _BoutonAppel({required this.numero});

  final String numero;

  @override
  State<_BoutonAppel> createState() => _BoutonAppelState();
}

class _BoutonAppelState extends State<_BoutonAppel> {
  bool _echec = false;

  Future<void> _appeler() async {
    final ok = await appeler(widget.numero);
    if (mounted) setState(() => _echec = !ok);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        HbaPressable(
          onTap: _appeler,
          elevation: HbaElevation.douce,
          radius: HbaRadius.button,
          // L'ALIGNEMENT ETIRE LE BLOC SUR TOUTE LA LARGEUR DISPONIBLE, et
          // c'est ce qu'on veut ici : le bloc est dans un Expanded, il doit
          // remplir sa moitie de ligne pour que les deux pastilles fassent la
          // meme largeur. La marge verticale porte la hauteur a 48 dp.
          alignment: Alignment.center,
          padding: const EdgeInsets.symmetric(
            horizontal: HbaSpacing.sm,
            vertical: 14,
          ),
          semantique: 'Appeler le ${widget.numero}',
          child: const Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.call_outlined, size: 18, color: HbaColors.primaryInk),
              SizedBox(width: HbaSpacing.sm),
              Text(
                'Appeler',
                style: TextStyle(
                  color: HbaColors.primaryInk,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ],
          ),
        ),
        if (_echec) ...[
          const SizedBox(height: HbaSpacing.xs),
          Text(
            "Ce téléphone n'a pas d'application d'appel. Composez le "
            '${widget.numero}.',
            style: theme.textTheme.bodySmall
                ?.copyWith(color: theme.colorScheme.error),
          ),
        ],
      ],
    );
  }
}

/// « Y aller » : passe la main a l'application de navigation du telephone.
class _BoutonItineraire extends StatefulWidget {
  const _BoutonItineraire({required this.place, required this.etiquette});

  final Place place;
  final String etiquette;

  @override
  State<_BoutonItineraire> createState() => _BoutonItineraireState();
}

class _BoutonItineraireState extends State<_BoutonItineraire> {
  bool _echec = false;

  Future<void> _ouvrir() async {
    final ok = await ouvrirItineraire(
      latitude: widget.place.latitude,
      longitude: widget.place.longitude,
      etiquette: widget.place.landmark.isEmpty
          ? widget.etiquette
          : widget.place.landmark,
    );

    if (mounted) setState(() => _echec = !ok);
  }

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // UN BLOC EN RELIEF PLUTOT QU'UN BOUTON CONTOURE.
          //
          // DEUX RAISONS, ET LA PREMIERE EST UN DEFAUT CORRIGE : le libelle
          // etait en #F2690F, soit 2,6:1 sur cette matiere — sous le seuil de
          // 3:1 que Material demande pour un simple element d'interface, et
          // tres loin des 4,5:1 d'un texte. Il passe en #A6480A.
          //
          // La seconde est de style : un contour d'un pixel est le seul objet
          // de l'ecran qui ne serait ni en relief ni creux. Ici il s'enfonce
          // comme tout le reste.
          HbaPressable(
            onTap: _ouvrir,
            elevation: HbaElevation.douce,
            radius: HbaRadius.button,
            // MEME LARGEUR QUE « APPELER », a cote duquel il vit desormais.
            // La marge verticale porte la hauteur a 48 dp, la cible tactile
            // de Material.
            alignment: Alignment.center,
            padding: const EdgeInsets.symmetric(
              horizontal: HbaSpacing.sm,
              vertical: 14,
            ),
            semantique: 'Y aller',
            child: const Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(Icons.navigation_outlined, size: 18, color: HbaColors.primaryInk),
                SizedBox(width: HbaSpacing.sm),
                Text(
                  'Y aller',
                  style: TextStyle(
                    color: HbaColors.primaryInk,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ],
            ),
          ),

          // UN BOUTON QUI NE FAIT RIEN EST PIRE QU'UN BOUTON ABSENT : le
          // livreur reste a l'arret a appuyer dessus. S'il n'y a aucune
          // application de navigation et aucun navigateur, on donne les
          // coordonnees a recopier.
          if (_echec) ...[
            const SizedBox(height: HbaSpacing.xs),
            Text(
              'Aucune application de navigation. Coordonnées : '
              '${widget.place.latitude.toStringAsFixed(5)}, '
              '${widget.place.longitude.toStringAsFixed(5)}',
              style: Theme.of(context)
                  .textTheme
                  .bodySmall
                  ?.copyWith(color: Theme.of(context).colorScheme.error),
            ),
          ],
        ],
      );
}

/// Ce que la course a rapporte, dit une fois et clairement.
///
/// LE MONTANT EST CELUI DE L'OFFRE, PAS UN CALCUL D'ICI. C'est la part figee
/// a la confirmation de la course, celle que le livreur avait sous les yeux en
/// acceptant, et celle que le grand livre creditera. Trois chiffres qui
/// doivent etre le meme ; en recalculer un serait s'offrir une chance de les
/// faire diverger.
///
/// ON NE DIT PAS « AJOUTE A VOS GAINS », ET C'EST UNE PRECISION QUI COMPTE. Le
/// credit n'est pas ecrit par cet appel : il arrive par un evenement, quelques
/// instants plus tard. Un livreur qui ouvrirait ses gains dans la seconde
/// pourrait ne rien y voir encore, et conclure a une perte. La phrase dit donc
/// ce qui est vrai — le montant que cette course rapporte — et annonce le
/// delai au lieu de le cacher.
class _Recapitulatif extends StatelessWidget {
  const _Recapitulatif({required this.mission});

  final Mission mission;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    // SAFEAREA POUR LA BARRE DE GESTES, DEFILEMENT POUR LES PETITS ECRANS.
    // Le premier tient le bouton au-dessus du trait de navigation d'Android ;
    // le second fait qu'un telephone court fait defiler au lieu de couper.
    return SafeArea(
      top: false,
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(
          HbaSpacing.gutter,
          HbaSpacing.lg,
          HbaSpacing.gutter,
          HbaSpacing.lg,
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Center(
              child: Container(
                height: 64,
                width: 64,
                decoration: const BoxDecoration(
                  color: HbaColors.successSoft,
                  shape: BoxShape.circle,
                ),
                child: const Icon(
                  Icons.check_rounded,
                  size: 36,
                  color: HbaColors.success,
                ),
              ),
            ),
            const SizedBox(height: HbaSpacing.md),

            Text(
              'Course livrée',
              textAlign: TextAlign.center,
              style: theme.textTheme.headlineMedium,
            ),

            if (mission.reference.isNotEmpty) ...[
              const SizedBox(height: HbaSpacing.xs),
              Text(
                mission.reference,
                textAlign: TextAlign.center,
                style: theme.textTheme.bodySmall,
              ),
            ],

            const SizedBox(height: HbaSpacing.lg),

            // LE MONTANT EN GRAND, SUR LE DEGRADE DE LA CARTE DE SOLDE : c'est
            // le meme objet que l'ecran Gains, et le livreur fait le lien sans
            // qu'on ait a l'ecrire.
            Container(
              padding: const EdgeInsets.symmetric(
                horizontal: HbaSpacing.lg,
                vertical: HbaSpacing.md,
              ),
              decoration: BoxDecoration(
                gradient: const LinearGradient(
                  colors: HbaColors.balanceGradient,
                  begin: Alignment.topLeft,
                  end: Alignment.bottomRight,
                ),
                borderRadius: BorderRadius.circular(HbaRadius.card),
              ),
              child: Column(
                children: [
                  const Text(
                    'Cette course vous rapporte',
                    style: TextStyle(color: Colors.white70, fontSize: 13),
                  ),
                  const SizedBox(height: HbaSpacing.xs),
                  Text(
                    Xof.format(mission.driverEarningXof),
                    style: const TextStyle(
                      color: Colors.white,
                      fontSize: 34,
                      fontWeight: FontWeight.w800,
                      letterSpacing: -0.5,
                    ),
                  ),
                ],
              ),
            ),

            const SizedBox(height: HbaSpacing.md),
            Text(
              'Le montant apparaît dans vos gains dans quelques instants, puis '
              'vous pourrez en demander le versement.',
              textAlign: TextAlign.center,
              style: theme.textTheme.bodySmall,
            ),

            const SizedBox(height: HbaSpacing.lg),
            HbaButton(
              label: 'Terminer',
              onPressed: () => Navigator.of(context).pop(),
            ),
          ],
        ),
      ),
    );
  }
}

/// Saisie du code de remise.
///
/// LE CODE N'EST JAMAIS AFFICHE NI PRE-REMPLI. Le livreur ne l'a pas recu : le
/// destinataire le lui dicte. Rien ici ne le conserve ni ne le journalise.
class _OtpSheet extends StatefulWidget {
  const _OtpSheet();

  @override
  State<_OtpSheet> createState() => _OtpSheetState();
}

class _OtpSheetState extends State<_OtpSheet> {
  final _controller = TextEditingController();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: EdgeInsets.only(
        left: HbaSpacing.gutter,
        right: HbaSpacing.gutter,
        top: HbaSpacing.lg,
        // TROIS TERMES, ET AUCUN N'EST DE TROP. « viewInsets » est le clavier ;
        // « padding » est ce qui reste de la barre de gestes UNE FOIS le
        // clavier deduit, donc les deux ne se cumulent jamais a tort. Depuis
        // que la feuille monte au Navigator racine, elle descend jusqu'au bas
        // reel de l'ecran : sans ce second terme, son bouton se retrouverait
        // sous le trait de navigation d'Android.
        bottom: MediaQuery.viewInsetsOf(context).bottom +
            MediaQuery.paddingOf(context).bottom +
            HbaSpacing.lg,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Code de remise', style: theme.textTheme.headlineMedium),
          const SizedBox(height: HbaSpacing.sm),
          Text(
            'Demandez le code au destinataire et saisissez-le.',
            style: theme.textTheme.bodyMedium,
          ),
          const SizedBox(height: HbaSpacing.lg),
          TextField(
            controller: _controller,
            autofocus: true,
            keyboardType: TextInputType.number,
            textAlign: TextAlign.center,
            inputFormatters: [
              FilteringTextInputFormatter.digitsOnly,
              LengthLimitingTextInputFormatter(6),
            ],
            style: const TextStyle(
              fontSize: 30,
              fontWeight: FontWeight.w800,
              letterSpacing: 12,
            ),
            decoration: const InputDecoration(hintText: '000000'),
          ),
          const SizedBox(height: HbaSpacing.lg),
          HbaButton(
            label: 'Confirmer la remise',
            onPressed: () => Navigator.of(context).pop(_controller.text),
          ),
        ],
      ),
    );
  }
}
