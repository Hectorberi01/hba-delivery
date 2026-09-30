import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:geolocator/geolocator.dart';
import 'package:go_router/go_router.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_ui/hba_ui.dart';

import 'annulation.dart';
import 'courses_screen.dart';
import 'delivery_providers.dart';
import 'livreurs_proches.dart';
import 'models.dart';
import 'recherche_livreur_vue.dart';

/// Accueil : la carte, les livreurs alentour, et de quoi commander.
///
/// LA CARTE OCCUPE TOUT L'ECRAN, ET C'EST POUR CA QUE LA NAVIGATION FLOTTE.
/// Une barre en bas aurait coupe un bandeau a une carte qui n'en a pas de
/// trop ; l'arc se pose par-dessus.
///
/// CE QUE LES EPINGLES MONTRENT, ET CE QU'ELLES NE MONTRENT PAS. Depuis la
/// revision du point 24 le 30 septembre 2026, chaque livreur alentour est
/// dessine AVEC SON VEHICULE : une moto pour une moto, un velo pour un velo.
/// Rien d'autre ne passe — pas de nom, pas de plaque, pas d'identifiant, pas
/// meme un identifiant opaque. Le type de vehicule est une CATEGORIE, partagee
/// par des centaines de livreurs a Cotonou ; il ne designe personne, la ou une
/// plaque ou un identifiant stable permettraient de suivre un homme d'un
/// releve a l'autre. La ligne de la matrice de visibilite qui reserve
/// l'IDENTITE du livreur a partir de DRIVER_ASSIGNED reste donc entiere.
class HomeScreen extends ConsumerStatefulWidget {
  const HomeScreen({super.key});

  @override
  ConsumerState<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends ConsumerState<HomeScreen> {
  /// LE CENTRE PAR DEFAUT EST COTONOU, PAS UN POINT NUL. Une carte qui s'ouvre
  /// au large du golfe de Guinee — latitude zero, longitude zero — pendant que
  /// la localisation se decide donne l'impression d'une application cassee.
  static const _cotonou = LatLng(6.3703, 2.3912);

  final _controleur = Completer<GoogleMapController>();

  /// Une epingle par type de vehicule rencontre.
  ///
  /// LE VEHICULE EST RENDU AU CLIENT DEPUIS LE 30 SEPTEMBRE 2026, par une
  /// revision du point 24 : la carte montre une moto, un velo, un tricycle ou
  /// une voiture, et non quatre pastilles identiques. Ce qui n'est toujours pas
  /// rendu — et qui borne tout le reste —, c'est l'identifiant : deux livreurs
  /// a moto restent indistinguables ici.
  ///
  /// UN DICTIONNAIRE, PAS UNE SEULE EPINGLE, parce que la carte en montre
  /// plusieurs a la fois et qu'elles n'ont pas le meme dessin. Le socle garde
  /// deja son propre cache par apparence ; celui-ci evite seulement de
  /// redemander pendant qu'un dessin est en cours.
  final Map<String, BitmapDescriptor> _epingles = {};

  /// L'epingle des vehicules sans image — camionnette, type non rendu.
  BitmapDescriptor? _epingleNue;

  /// La densite avec laquelle les epingles ont ete dessinees. Un telephone
  /// branche sur un ecran externe peut en changer en cours de route.
  double? _dessineeA;

  /// Sonde la course en cours tant que l'accueil la porte.
  ///
  /// CINQ SECONDES ICI, DIX DANS LE SUIVI, ET L'ECART EST VOULU. C'est le seul
  /// ecran que le client REGARDE sans rien faire : il vient de payer et il
  /// attend un livreur. Le suivi, lui, se consulte. Une fois le livreur
  /// attribue, plus rien n'est sonde ici.
  Timer? _sondeur;

  /// La course que la sonde interroge. Ecrite a chaque construction.
  String? _idSuivi;

  /// Vrai tant que l'accueil montre l'attente. C'est ce qui distingue « un
  /// livreur vient d'accepter sous ses yeux » — on ouvre le detail — de « il
  /// ouvre l'application sur une course deja attribuee », ou le jeter dans le
  /// suivi sans qu'il ait rien demande serait brutal.
  bool _attendait = false;

  /// La course pour laquelle on a deja ouvert le suivi. Sans elle, le retour
  /// en arriere rouvrirait le suivi en boucle.
  String? _bascule;

  /// Le dernier statut vu pour la course suivie.
  ///
  /// IL SERT A RAFRAICHIR « MES COURSES », QUI NE SE RAFRAICHISSAIT PAS. La
  /// liste est demandee une fois et gardee ; a la creation d'une course elle
  /// etait invalidee, mais la course n'etait alors PAS PAYEE — donc absente de
  /// la reponse. Le paiement confirme ensuite ne touchait rien, et le client
  /// devait tirer la liste vers le bas pour voir sa commande apparaitre.
  DeliveryStatus? _dernierStatut;

  LatLng _centre = _cotonou;
  LatLng? _moi;

  /// Le point que la camera vise pendant qu'on la deplace. Il ne devient le
  /// centre de la requete qu'au repos — voir [_recentrer].
  LatLng? _vise;

  /// Depuis quand cette course attend sa confirmation de paiement.
  ///
  /// SERT A OUVRIR UNE SORTIE, PAS A MESURER QUOI QUE CE SOIT. Les premieres
  /// secondes, l'attente est normale et proposer d'abandonner serait alarmant ;
  /// passe trois quarts de minute, elle ne l'est plus, et un ecran sans issue
  /// devient une impasse dont on ne sort qu'en tuant l'application.
  DateTime? _paiementDepuis;

  /// Au-dela de ce delai, l'attente offre une sortie.
  ///
  /// QUARANTE-CINQ SECONDES, ET LE CHIFFRE VIENT DU CHEMIN NORMAL : le webhook
  /// de l'operateur arrive en quelques secondes quand tout va bien. Ce qui se
  /// passe au-dela n'est plus de la lenteur, c'est une panne — tunnel ferme,
  /// webhook rejete, operateur muet — et aucune d'elles ne se resout en
  /// attendant devant l'ecran.
  static const _avantLaSortie = Duration(seconds: 45);

  @override
  void initState() {
    super.initState();
    unawaited(_seSituer());

    _sondeur = Timer.periodic(const Duration(seconds: 5), (_) {
      final id = _idSuivi;
      if (id != null) ref.invalidate(deliveryProvider(id));
    });
  }

  /// LA DENSITE NE SE LIT PAS DANS initState : MediaQuery n'y est pas encore
  /// disponible. Et elle peut CHANGER en cours de route — un telephone branche
  /// sur un ecran externe —, donc on compare au lieu de charger une seule fois.
  @override
  void didChangeDependencies() {
    super.didChangeDependencies();

    final ratio = MediaQuery.devicePixelRatioOf(context);
    if (ratio == _dessineeA) return;

    _dessineeA = ratio;
    unawaited(_dessinerLesEpingles(ratio));
  }

  Future<void> _dessinerLesEpingles(double ratio) async {
    _epingles.clear();

    // LES QUATRE IMAGES SONT DESSINEES D'AVANCE, ET NON A LA DEMANDE. Les
    // dessiner quand un livreur apparait ferait clignoter la carte a chaque
    // rafraichissement ; quatre images de 160 px, c'est negligeable.
    //
    // LA CAMIONNETTE MANQUE A LA LISTE PARCE QU'ELLE MANQUE AUX ASSETS. Elle
    // retombe sur le disque par _epingleDe, sans cas particulier a ecrire ici.
    //
    // « enLigne: true » TOUJOURS : ces livreurs-la sont disponibles, sinon la
    // recherche ne les rendrait pas. L'etat eteint n'a de sens que sur la carte
    // du livreur, qui se regarde lui-meme.
    final dessins = <String, BitmapDescriptor>{};
    for (final type in const ['Motorcycle', 'Car', 'Bicycle', 'Tricycle']) {
      final chemin = Epingles.fichierDuVehicule(type);
      if (chemin == null) continue;

      final epingle = await Epingles.vehiculeEnRelief(
        chemin: chemin,
        ratio: ratio,
        enLigne: true,
      );

      if (epingle != null) dessins[type] = epingle;
    }

    final nue = await Epingles.disque(couleur: HbaColors.success, ratio: ratio);

    // LA COMPARAISON APRES L'ATTENTE N'EST PAS DU ZELE : deux dessins peuvent
    // se croiser, et le plus lent ecraserait le plus recent.
    if (!mounted || ratio != _dessineeA) return;

    setState(() {
      _epingles
        ..clear()
        ..addAll(dessins);
      _epingleNue = nue;
    });
  }

  /// L'epingle d'un livreur, selon ce qu'il conduit.
  ///
  /// LE REPLI EST LE DISQUE, PAS UNE MOTO. Une camionnette n'a pas d'image, et
  /// un service plus ancien ne rend pas le type : dessiner une moto dans ces
  /// deux cas afficherait un vehicule que personne n'a declare.
  BitmapDescriptor? _epingleDe(String vehicule) =>
      _epingles[vehicule] ?? _epingleNue;

  @override
  void dispose() {
    _sondeur?.cancel();
    super.dispose();
  }

  Future<void> _seSituer() async {
    try {
      if (!await Geolocator.isLocationServiceEnabled()) return;

      var permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied) {
        permission = await Geolocator.requestPermission();
      }
      if (permission == LocationPermission.denied ||
          permission == LocationPermission.deniedForever) {
        return;
      }

      final position = await Geolocator.getCurrentPosition();
      if (!mounted) return;

      final ici = LatLng(position.latitude, position.longitude);
      setState(() {
        _moi = ici;
        _centre = ici;
      });

      final carte = await _controleur.future;
      await carte.animateCamera(CameraUpdate.newLatLngZoom(ici, 14));
    } on Object {
      // POSITION REFUSEE OU INDISPONIBLE : LA CARTE RESTE SUR COTONOU. Ce
      // n'est pas une panne — un client peut commander sans partager sa
      // position, il pose ses points a la main sur la carte.
    }
  }

  @override
  Widget build(BuildContext context) {
    final livreurs = ref.watch(livreursProchesProvider(_centre));
    final courses = ref.watch(deliveriesProvider).valueOrNull ?? const [];
    final enCours = courses.where((c) => !c.status.isClosed).toList();

    // LA COURSE QUE L'ON VIENT DE PAYER PASSE AVANT LA LISTE, et elle ne peut
    // pas en venir : tant qu'elle n'est pas payee, le service ne la rend plus
    // au client. Voir courseEnAttenteProvider.
    final attendue = ref.watch(courseEnAttenteProvider);
    final idActif = attendue ?? (enCours.isEmpty ? null : enCours.first.id);

    final active =
        idActif == null ? null : ref.watch(deliveryProvider(idActif)).valueOrNull;

    // ON NE SONDE QUE PENDANT L'ATTENTE. Une fois le livreur attribue, c'est le
    // suivi qui interroge le service ; laisser cette sonde tourner toutes les
    // cinq secondes pendant que le client est sur un autre onglet lui couterait
    // des donnees qu'il paie a la recharge, pour un ecran que personne ne
    // regarde.
    _idSuivi = active == null || active.status.suiviParAccueil ? idActif : null;

    // TOUT CHANGEMENT DE STATUT PERIME LA LISTE. Le passage decisif est
    // PENDING_PAYMENT vers PAID — la course entre alors dans ce que le service
    // rend au client — mais les suivants comptent aussi : « en cours » devient
    // « livree », et l'onglet doit le montrer sans qu'on le lui demande.
    if (active != null && active.status != _dernierStatut) {
      _dernierStatut = active.status;

      // LE COMPTEUR EST ARME ICI, PAS DANS _attente. C'est le seul endroit qui
      // voit un CHANGEMENT de statut ; _attente, lui, est rappele a chaque
      // battement de la sonde et remettrait le compteur a zero sans fin.
      _paiementDepuis =
          active.status == DeliveryStatus.pendingPayment ? DateTime.now() : null;
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) ref.invalidate(deliveriesProvider);
      });
    }

    if (active != null && active.status.suiviParAccueil) {
      // Ecrit pendant la construction, sans setState : ce champ ne change rien
      // a ce qui est peint, il ne sert qu'a la construction SUIVANTE.
      _attendait = true;
      return Scaffold(body: _attente(active));
    }

    // LE PAIEMENT VIENT D'ETRE CONFIRME MAIS LA FICHE N'EST PAS ENCORE LA. Sans
    // ce cas, on peindrait la carte une fraction de seconde avant de la
    // remplacer par l'attente, et le client verrait son ecran sauter.
    if (attendue != null && active == null) {
      return const Scaffold(body: Center(child: CircularProgressIndicator()));
    }

    // LA COURSE EST TERMINEE : LE MARQUEUR N'A PLUS DE RAISON D'ETRE. Sans ce
    // menage, il continuerait de designer une course livree, et une NOUVELLE
    // course en cours serait ignoree au profit de l'ancienne.
    if (attendue != null && active != null && active.status.isClosed) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) ref.read(courseEnAttenteProvider.notifier).state = null;
      });
    }

    // UN LIVREUR A ACCEPTE PENDANT QU'IL REGARDAIT : on ouvre le detail, ou se
    // trouvent son nom, son vehicule et le code de remise.
    if (active != null &&
        _attendait &&
        !active.status.isClosed &&
        _bascule != active.id) {
      _attendait = false;
      _bascule = active.id;

      final id = active.id;
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted) return;

        ref.read(courseEnAttenteProvider.notifier).state = null;

        // PAS PAR-DESSUS UN AUTRE ECRAN. Le client peut etre en train de
        // remplir une nouvelle commande, posee sur cet accueil : lui jeter le
        // suivi par-dessus lui ferait perdre sa saisie. Il retrouvera la course
        // en revenant, et la carte du bas l'y mene.
        if (!(ModalRoute.of(context)?.isCurrent ?? false)) return;

        context.push('/suivi/$id');
      });
    }

    return Scaffold(
      body: Stack(
        children: [
          Positioned.fill(
            child: GoogleMap(
              style: HbaCarte.jour,
              initialCameraPosition:
                  CameraPosition(target: _centre, zoom: _moi == null ? 12 : 14),
              onMapCreated: (carte) {
                if (!_controleur.isCompleted) _controleur.complete(carte);
              },
              myLocationEnabled: _moi != null,
              myLocationButtonEnabled: false,
              zoomControlsEnabled: false,
              mapToolbarEnabled: false,

              // LA CARTE NE RECENTRE PAS LA REQUETE A CHAQUE PIXEL. On attend
              // que le doigt se leve : « onCameraIdle » ne part qu'une fois le
              // geste fini, la ou « onCameraMove » enverrait des dizaines de
              // requetes pour un seul deplacement.
              onCameraIdle: _recentrer,
              onCameraMove: (position) => _vise = position.target,

              markers: {
                for (final (i, livreur) in livreurs.valueOrNull
                        ?.indexed ??
                    const <(int, LivreurProche)>[])
                  Marker(
                    // L'INDEX, PAS UN IDENTIFIANT DE LIVREUR — le service n'en
                    // rend pas, et c'est voulu. Voir livreurs_proches.dart.
                    markerId: MarkerId('livreur-$i'),
                    position: livreur.position,
                    anchor: const Offset(0.5, 0.5),

                    // LE BALLON DE GOOGLE RESTE LE SECOURS tant que le dessin
                    // n'est pas pret, ou s'il a echoue : une carte sans
                    // epingles serait pire qu'une carte mal habillee.
                    icon: _epingleDe(livreur.vehicule) ??
                        BitmapDescriptor.defaultMarkerWithHue(
                          BitmapDescriptor.hueOrange,
                        ),
                  ),
              },
            ),
          ),

          // LE BANDEAU DU HAUT RESTE A GAUCHE, loin de l'arc : les deux se
          // recouvriraient sur un petit ecran.
          Positioned(
            top: 0,
            left: 0,
            right: 96,
            child: SafeArea(
              child: Padding(
                padding: const EdgeInsets.all(HbaSpacing.gutter),
                child: _Compteur(livreurs: livreurs),
              ),
            ),
          ),

          Positioned(
            left: 0,
            right: 0,
            bottom: 0,
            child: SafeArea(
              child: Padding(
                padding: const EdgeInsets.all(HbaSpacing.gutter),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    // LA COURSE EN COURS PASSE AVANT LE BOUTON DE COMMANDE. Un
                    // client qui a une livraison en route ouvre l'application
                    // pour la SUIVRE, pas pour en commander une autre.
                    if (enCours.isNotEmpty) ...[
                      CarteCourse(course: enCours.first, enAvant: true),
                      const SizedBox(height: HbaSpacing.sm),
                    ],
                    HbaButton(
                      label: 'Envoyer un colis',
                      icon: Icons.add,
                      onPressed: () => context.push('/nouvelle'),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }

  /// L'attente du paiement, avec une issue quand elle dure.
  ///
  /// POURQUOI CET ECRAN AVAIT BESOIN D'ETRE REPRIS. Il n'offrait rien : ni
  /// bouton, ni retour, ni onglet — l'accueil le remplace entierement. Quand la
  /// confirmation n'arrivait pas, et elle n'arrive pas des que le webhook de
  /// l'operateur echoue, le client restait devant un sablier jusqu'a fermer
  /// l'application de force. C'est le meme defaut que « aucun livreur trouve »
  /// corrige la veille : un etat sans sortie.
  ///
  /// LES DEUX GESTES NE FONT PAS LA MEME CHOSE, ET AUCUN N'ANNULE LA COURSE.
  ///
  /// « Vérifier » relit la fiche. La sonde le fait deja toutes les cinq
  /// secondes, donc ce bouton ne debloque rien que le temps ne debloquerait :
  /// il rend la main. Attendre sans pouvoir rien tenter est ce qui rend une
  /// attente insupportable, et un client qui a appuye sait qu'il a fait ce
  /// qu'il pouvait.
  ///
  /// « Continuer sans attendre » efface le marqueur et rend l'accueil, SANS
  /// TOUCHER A LA COURSE. C'est deliberé : si le paiement se confirme plus
  /// tard, la course devient payée, entre dans ce que le service rend au client
  /// et reparait dans sa liste — rien n'est perdu. S'il ne se confirme jamais,
  /// le balayage des impayées l'abandonne de lui-meme. Annuler ici, au
  /// contraire, fermerait une course peut-etre payée une seconde plus tard.
  ///
  /// CE QUE CE BOUTON NE PROMET PAS : que rien n'a ete preleve. On ne le sait
  /// pas — c'est precisement l'information qui manque — et le dire serait une
  /// promesse que ce code ne peut pas tenir. Voir le texte du remboursement
  /// plus bas, corrige le 30 septembre 2026 pour la meme raison.
  Widget _attentePaiement(Delivery course) {
    final depuis = _paiementDepuis;
    final trop = depuis != null &&
        DateTime.now().difference(depuis) >= _avantLaSortie;

    if (!trop) {
      return const _Bandeau(
        icone: Icons.hourglass_top,
        titre: 'Paiement en cours de confirmation',
        texte: "Nous attendons la confirmation de votre opérateur. La "
            "recherche d'un livreur commencera dès qu'elle arrive.",
      );
    }

    return _Bandeau(
      icone: Icons.hourglass_bottom,
      titre: 'La confirmation tarde',
      texte: "Votre opérateur n'a pas encore confirmé. Vous pouvez vérifier à "
          'nouveau, ou revenir plus tard : si le paiement aboutit, la course '
          'reprendra toute seule et vous la retrouverez dans vos commandes.',
      action: ('Vérifier', () => ref.invalidate(deliveryProvider(course.id))),
      secondaire: (
        'Continuer sans attendre',
        () => ref.read(courseEnAttenteProvider.notifier).state = null,
      ),
    );
  }

  /// Ce que l'accueil montre a la place de la carte pendant l'attente.
  Widget _attente(Delivery course) => switch (course.status) {
        DeliveryStatus.pendingPayment => _attentePaiement(course),

        // LE MEME TEXTE QUE DANS LE SUIVI, MOT POUR MOT. Deux formulations du
        // meme fait — surtout quand ce fait est « personne ne vous a preleve »
        // — sont deux promesses differentes.
        DeliveryStatus.paymentFailed => _Bandeau(
            icone: Icons.error_outline,
            titre: "Le paiement n'a pas abouti",
            texte: "Aucun montant n'a été prélevé. La commande a été "
                'abandonnée ; vous pouvez en relancer une.',
            action: ('Compris', () {
              ref.read(courseEnAttenteProvider.notifier).state = null;
            }),
          ),

        // AUCUN LIVREUR : C'EST UN ETAT TERMINAL, IL LUI FAUT DONC UNE SORTIE.
        //
        // Ce cas tombait dans la branche par defaut et affichait la recherche de
        // livreur, indefiniment. Le seul geste offert etait « Annuler la
        // demande », qui appelle CancelDelivery sur un etat terminal : le
        // service repondait 409 et le client lisait « Transition interdite sur
        // Delivery : NoDriverFound -> Cancelled par Customer » dans un
        // bandeau. Sur l'onglet Accueil il n'y a pas de fleche de retour :
        // l'ecran ne se debloquait qu'en tuant l'application.
        //
        // C'est le meme traitement que le paiement echoue, qui avait bien recu
        // son bouton : effacer le marqueur rend l'accueil a la carte.
        //
        // AUCUNE PROMESSE DE REMBOURSEMENT AUTOMATIQUE, ET C'EST UNE CORRECTION
        // DU 30 SEPTEMBRE 2026. Ce texte annoncait « le remboursement a ete
        // demande automatiquement ». VERIFIE CE JOUR-LA DANS LA DOCUMENTATION DU
        // FOURNISSEUR : FedaPay n'a PAS d'API de remboursement. Les
        // remboursements se font depuis le tableau de bord du compte marchand,
        // et uniquement sur MTN Mobile Money. Aucun code ne pouvait donc tenir
        // cette promesse, et la tenir a la main est une decision d'exploitation,
        // pas un effet de bord d'un ecran. Voir points-a-trancher, point 3.
        DeliveryStatus.noDriverFound => _Bandeau(
            icone: Icons.person_search_outlined,
            titre: "Aucun livreur n'a pu être trouvé",
            texte: course.estRemboursee
                ? "Personne n'était disponible autour du point de collecte. "
                    'Le montant vous a été rendu.'
                : "Personne n'était disponible autour du point de collecte. "
                    'HBA revient vers vous au sujet du montant prélevé.',
            action: ('Compris', () {
              ref.read(courseEnAttenteProvider.notifier).state = null;
            }),
          ),

        _ => RechercheLivreurVue(
            delivery: course,
            onAnnuler: () => unawaited(annulerLaCourse(context, ref, course)),
          ),
      };

  void _recentrer() {
    final cible = _vise;
    if (cible == null || !mounted) return;

    // ON NE REDEMANDE PAS POUR TROIS METRES. Sous cinq cents metres, la reponse
    // serait la meme a quelques pastilles pres, et chaque requete coute au
    // client des donnees qu'il paie.
    if (_distanceApprox(cible, _centre) < 500) return;

    setState(() => _centre = cible);
  }

  /// Distance approchee en metres, suffisante pour decider d'un rafraichissement.
  static double _distanceApprox(LatLng a, LatLng b) {
    const metresParDegre = 111320.0;
    final dLat = (a.latitude - b.latitude).abs() * metresParDegre;
    final dLng = (a.longitude - b.longitude).abs() * metresParDegre * 0.62;
    return dLat + dLng;
  }
}

/// Combien de livreurs sont autour du client.
///
/// LE NOMBRE EXACT A ETE DEMANDE, ET IL EST AFFICHE. La version precedente
/// disait « des livreurs sont disponibles » sans chiffre, pour une raison qui
/// reste vraie : un nombre releve d'heure en heure renseigne sur l'activite
/// reelle d'un quartier, ce qui n'est ni au client ni a un concurrent. Le
/// choix inverse a ete fait sciemment — un chiffre convainc la ou une formule
/// vague laisse douter.
///
/// LE PLAFOND DE LA ROUTE BORNE CE QUI EST DIT. Le service ne rend jamais plus
/// de quinze positions ; « 15 » ne veut donc pas dire quinze, mais « au moins
/// quinze ». L'ecran l'ecrit ainsi plutot que d'affirmer un nombre faux.
class _Compteur extends StatelessWidget {
  const _Compteur({required this.livreurs});

  /// Doit valoir le plafond de /api/client/v1/drivers/nearby. Les deux se
  /// desynchroniseraient sans bruit ; c'est pour ca que la phrase change de
  /// forme au lieu de mentir sur un nombre.
  static const _plafondRoute = 15;

  final AsyncValue<List<LivreurProche>> livreurs;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    final texte = switch (livreurs) {
      AsyncData(:final value) when value.isEmpty =>
        'Aucun livreur dans ce secteur pour le moment.',
      AsyncData(:final value) when value.length >= _plafondRoute =>
        'Plus de $_plafondRoute livreurs autour de vous.',
      AsyncData(:final value) when value.length == 1 =>
        '1 livreur autour de vous.',
      AsyncData(:final value) => '${value.length} livreurs autour de vous.',
      AsyncError() => 'Livreurs alentour indisponibles.',
      _ => 'Recherche des livreurs alentour…',
    };

    return HbaCard(
      padding: const EdgeInsets.symmetric(
        horizontal: HbaSpacing.md,
        vertical: HbaSpacing.sm,
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(
            switch (livreurs) {
              AsyncData(:final value) when value.isEmpty =>
                Icons.location_off_outlined,
              AsyncData() => Icons.two_wheeler_outlined,
              AsyncError() => Icons.cloud_off_outlined,
              _ => Icons.more_horiz,
            },
            size: 18,
            color: HbaColors.primary,
          ),
          const SizedBox(width: HbaSpacing.sm),
          Flexible(child: Text(texte, style: theme.textTheme.bodyMedium)),
        ],
      ),
    );
  }
}

/// Un fait, une phrase, et rien d'autre a l'ecran.
///
/// PAS DE CARTE DERRIERE. Ces deux moments — « on attend votre operateur », « le
/// paiement n'a pas abouti » — ne se lisent pas sur une carte : il n'y a ni
/// livreur a montrer, ni trajet a suivre. Une carte sous le texte donnerait a
/// croire qu'il se passe quelque chose dehors.
class _Bandeau extends StatelessWidget {
  const _Bandeau({
    required this.icone,
    required this.titre,
    required this.texte,
    this.action,
    this.secondaire,
  });

  final IconData icone;
  final String titre;
  final String texte;

  /// Libelle et geste du bouton, quand il y en a un.
  final (String, VoidCallback)? action;

  /// La seconde issue, quand un etat en a deux.
  ///
  /// EN NEUTRE ET SOUS LA PREMIERE, PAS A COTE. Deux boutons de meme poids
  /// obligent a choisir ; ici l'un est ce qu'on espere — reverifier — et
  /// l'autre ce qu'on concede. La hierarchie visuelle dit lequel est lequel
  /// sans qu'un mot ait a l'expliquer.
  final (String, VoidCallback)? secondaire;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Center(
      child: Padding(
        padding: const EdgeInsets.all(HbaSpacing.gutter),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icone, size: 48, color: theme.colorScheme.primary),
            const SizedBox(height: HbaSpacing.md),
            Text(
              titre,
              textAlign: TextAlign.center,
              style: theme.textTheme.titleLarge,
            ),
            const SizedBox(height: HbaSpacing.sm),
            Text(
              texte,
              textAlign: TextAlign.center,
              style: theme.textTheme.bodyMedium
                  ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
            ),
            if (action case (final libelle, final geste)) ...[
              const SizedBox(height: HbaSpacing.lg),
              HbaButton(label: libelle, onPressed: geste),
            ],
            if (secondaire case (final libelle, final geste)) ...[
              const SizedBox(height: HbaSpacing.sm),
              HbaButton(
                label: libelle,
                tone: HbaButtonTone.neutral,
                onPressed: geste,
              ),
            ],
          ],
        ),
      ),
    );
  }
}
