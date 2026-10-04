import 'dart:async';
import 'dart:math' as maths;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:geolocator/geolocator.dart';
import 'package:go_router/go_router.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import '../../core/paiement_en_attente.dart';
import '../../core/reprise_de_paiement.dart';
import '../avis/avis_models.dart';
import '../avis/avis_providers.dart';
import '../profil/adresses.dart';
import '../rides/carte_course_active.dart';
import '../rides/course_models.dart';
import '../rides/course_providers.dart';
import '../rides/suivi_course_screen.dart';
import 'annulation.dart';
import 'bandeau_livreur.dart';
import 'courses_screen.dart';
import 'delivery_providers.dart';
import 'livreurs_proches.dart';
import 'models.dart';
import 'paiement_webview.dart';
import 'point_field.dart';
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

  /// Les memes, en eteint, pour les livreurs qui ne peuvent pas prendre la
  /// mission.
  ///
  /// DEUX JEUX PLUTOT QU'UN DESSIN TEINTE A LA VOLEE : une BitmapDescriptor est
  /// une image deja rasterisee, elle ne se colore pas apres coup. Le socle sait
  /// dessiner les deux etats — c'est ce que fait deja la carte du livreur — et
  /// huit images de 160 px restent negligeables.
  final Map<String, BitmapDescriptor> _epinglesEteintes = {};

  /// L'epingle des vehicules sans image — camionnette, type non rendu.
  BitmapDescriptor? _epingleNue;

  /// La meme, en eteint.
  BitmapDescriptor? _epingleNueEteinte;

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

  /// Le meme, pour une course de personnes.
  bool _attendaitUneCourse = false;

  /// La feuille de commande est-elle deployee ?
  ///
  /// L'ETAT A QUITTE LA FEUILLE POUR MONTER ICI le 4 octobre 2026, et c'est le
  /// bouton flottant qui l'exige : il doit s'effacer quand la feuille est
  /// ouverte, donc quelqu'un doit savoir les deux. Tant que l'etat vivait dans
  /// la feuille, lui seul le savait.
  bool _feuilleOuverte = true;

  /// Ou le client a pose le bouton flottant. Nul tant qu'il ne l'a pas deplace.
  ///
  /// IL NE SURVIT PAS A LA FERMETURE DE L'APPLICATION, et c'est suffisant :
  /// on deplace ce bouton pour degager ce qu'il cache a l'instant — une rue,
  /// une epingle — pas pour se composer un ecran a demeure.
  Offset? _boutonPose;

  /// Une mission roulait-elle a la construction precedente ?
  ///
  /// IL SERT A RELIRE LES AVIS EN ATTENTE AU BON MOMENT, ET A UN SEUL. La liste
  /// des missions notables est demandee une fois puis gardee ; le seul instant
  /// ou elle change sous les yeux du passager est celui ou sa derniere mission
  /// s'acheve. C'est ce front descendant que ce champ detecte — sans lui, il
  /// faudrait soit relire la liste a chaque battement, soit attendre le
  /// prochain demarrage de l'application.
  bool _uneMissionRoulait = false;

  /// Faut-il relire les courses toutes les cinq secondes ?
  ///
  /// IL N'EXISTE QUE PENDANT LE RADAR. Hors de cette attente, l'accueil n'a
  /// aucune raison de redemander : la carte de course se relit deja toute
  /// seule, toutes les quinze secondes, et doubler la cadence ferait payer au
  /// passager des donnees pour un ecran qu'il ne regarde pas.
  bool _sondeLesCourses = false;

  /// La course pour laquelle on a deja ouvert le suivi. Sans elle, le retour
  /// en arriere rouvrirait le suivi en boucle.
  String? _bascule;

  /// La course dont on a deja ouvert le suivi, pour ne le faire qu'une fois.
  ///
  /// SEPAREE DE CELLE DES COLIS : un passager peut avoir les deux en meme temps,
  /// et un seul champ ferait que la seconde bascule n'aurait jamais lieu.
  String? _basculeCourse;

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

      // LE RADAR D'UNE COURSE A BESOIN DE LA MEME SONDE, ET JE L'AVAIS OUBLIEE
      // LE 3 OCTOBRE 2026. L'ecran s'affichait, le chauffeur acceptait, et le
      // passager continuait de lire « Recherche d'un livreur » : rien ne
      // relisait « coursesActivesProvider ». La carte de course, elle, porte sa
      // propre minuterie — mais l'accueil ne la construit pas tant que le radar
      // occupe l'ecran, donc elle ne tournait pas non plus.
      //
      // CINQ SECONDES, COMME POUR UN COLIS : on regarde l'ecran en attendant,
      // et c'est le seul moment ou un quart de minute de retard se verrait.
      if (_sondeLesCourses) ref.invalidate(coursesActivesProvider);
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
    // LES DEUX ETATS SONT DESSINES, DEPUIS LE 3 OCTOBRE 2026. Jusque-la cette
    // carte ne montrait que des livreurs disponibles — la route ecartait les
    // autres — et « enLigne: true » etait donc toujours vrai. Elle les montre
    // tous desormais (point 24, troisieme revision), et l'eteint sert enfin
    // ailleurs que sur la carte du livreur qui se regarde lui-meme.
    final dessins = <String, BitmapDescriptor>{};
    final eteints = <String, BitmapDescriptor>{};
    for (final type in const ['Motorcycle', 'Car', 'Bicycle', 'Tricycle']) {
      final chemin = Epingles.fichierDuVehicule(type);
      if (chemin == null) continue;

      final epingle = await Epingles.vehiculeEnRelief(
        chemin: chemin,
        ratio: ratio,
        enLigne: true,
      );

      final eteinte = await Epingles.vehiculeEnRelief(
        chemin: chemin,
        ratio: ratio,
        enLigne: false,
      );

      if (epingle != null) dessins[type] = epingle;
      if (eteinte != null) eteints[type] = eteinte;
    }

    final nue = await Epingles.disque(couleur: HbaColors.success, ratio: ratio);

    // GRIS, PAS VERT PALE : le disque nu ne porte aucun pictogramme, donc rien
    // d'autre que sa couleur ne peut dire « celui-la, non ». Un vert affadi se
    // lirait comme un vert sur un ecran en plein soleil.
    final nueEteinte = await Epingles.disque(couleur: HbaColors.inkMuted, ratio: ratio);

    // LA COMPARAISON APRES L'ATTENTE N'EST PAS DU ZELE : deux dessins peuvent
    // se croiser, et le plus lent ecraserait le plus recent.
    if (!mounted || ratio != _dessineeA) return;

    setState(() {
      _epingles
        ..clear()
        ..addAll(dessins);
      _epinglesEteintes
        ..clear()
        ..addAll(eteints);
      _epingleNue = nue;
      _epingleNueEteinte = nueEteinte;
    });
  }

  /// L'epingle d'un livreur, selon ce qu'il conduit.
  ///
  /// LE REPLI EST LE DISQUE, PAS UNE MOTO. Une camionnette n'a pas d'image, et
  /// un service plus ancien ne rend pas le type : dessiner une moto dans ces
  /// deux cas afficherait un vehicule que personne n'a declare.
  BitmapDescriptor? _epingleDe(String vehicule, {required bool disponible}) =>
      disponible
          ? _epingles[vehicule] ?? _epingleNue
          : _epinglesEteintes[vehicule] ?? _epingleNueEteinte;

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

    // LA PREMIERE LIVRAISON DONT LE LIVREUR EST CONNU, s'il y en a une.
    //
    // LA PREMIERE ET PAS TOUTES : plusieurs missions a la fois sont permises
    // (point 35), mais empiler trois bandeaux sous la marque reviendrait a
    // poser une liste par-dessus la carte. Celui-ci mene au suivi, qui les
    // porte toutes.
    //
    // PAS « firstOrNull » : il n'est pas dans dart:core, il vient de
    // package:collection — que cette application ne declare pas. Le piege
    // s'est deja referme dans ce depot.
    final affectees = enCours.where((c) => c.driver != null).toList();
    final enRoute = affectees.isEmpty ? null : affectees.first;

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

    // ── LA COURSE DE PERSONNES ───────────────────────────────────────────
    //
    // APRES LES COLIS, ET C'EST UN ORDRE, PAS UN HASARD. Un passager peut avoir
    // les deux en cours ; le colis garde la priorite parce que ce bloc-la
    // existait avant et que rien ne justifie de le lui retirer. Le jour ou il
    // faudra choisir vraiment, c'est une decision de produit.
    //
    // LE RADAR S'AFFICHE SUR L'ACCUEIL DEPUIS LE 3 OCTOBRE 2026. Avant, le
    // paiement renvoyait vers l'ecran de suivi, qui annonçait « Recherche d'un
    // chauffeur » sur un fond vide : le passager quittait l'accueil pour voir
    // moins de choses. Le colis, lui, montrait deja sa recherche ici.
    final course = _courseEnRecherche();

    // Ecrits pendant la construction, sans setState : ces deux champs ne
    // changent rien a ce qui est peint, ils ne servent qu'a la sonde et a la
    // construction SUIVANTE.
    _sondeLesCourses = course != null;

    if (course != null) {
      _attendaitUneCourse = true;

      return Scaffold(
        body: RechercheLivreurVue(
          mission: _missionDeLaCourse(course),
          onAnnuler: () =>
              unawaited(annulerLaDemandeDeCourse(context, ref, course)),
        ),
      );
    }

    _ouvrirLeSuiviSiUnChauffeurAAccepte();

    // CE QUI ROULE PASSE AVANT CE QUI EST FINI. Tant qu'une mission est en
    // cours, la notation de la precedente attend : poser une question sur hier
    // par-dessus la carte d'une course en route, c'est couvrir ce que le client
    // est venu regarder.
    _proposerUneNotation(
      roule: enCours.isNotEmpty ||
          (ref.watch(coursesActivesProvider).valueOrNull ?? const []).isNotEmpty,
    );

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
                        ?.tous.indexed ??
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
                    icon: _epingleDe(
                          livreur.vehicule,
                          disponible: livreur.disponible,
                        ) ??
                        BitmapDescriptor.defaultMarkerWithHue(
                          BitmapDescriptor.hueOrange,
                        ),
                  ),
              },
            ),
          ),

          // LA BARRE DU HAUT : LE MENU, LA MARQUE, LA CLOCHE.
          //
          // LE PORTRAIT A DISPARU D'ICI, ET C'EST LA BARRE D'ONGLETS QUI LE
          // REMPLACE. Il etait « la porte du reste de l'application » tant que
          // l'accueil etait seul ; depuis le 4 octobre 2026, le profil est un
          // onglet, et garder un second chemin vers lui sur la carte aurait
          // laisse deux portes pour une seule piece.
          //
          // POSEE SUR LA CARTE ET NON AU-DESSUS D'ELLE. Une vraie AppBar
          // aurait rogne la carte d'une bande blanche ; les maquettes montrent
          // la carte qui passe derriere, et les trois elements qui flottent.
          Positioned(
            top: 0,
            left: 0,
            right: 0,
            child: SafeArea(
              child: Padding(
                padding: const EdgeInsets.symmetric(
                  horizontal: HbaSpacing.gutter,
                  vertical: HbaSpacing.sm,
                ),
                child: Column(
                  children: [
                    Row(
                      children: [
                        // LE MENU OUVRE LE PROFIL, PAS UN TIROIR. Un tiroir
                        // aurait double la liste de destinations du profil, et
                        // deux listes finissent toujours par differer. « push »
                        // et non « go » : le profil s'empile, la carte reste
                        // vivante dessous, et la fleche de retour ramene
                        // vraiment ici.
                        _BoutonRond(
                          icone: Icons.menu,
                          etiquette: 'Mon profil',
                          onTap: () => context.push('/profil'),
                        ),
                        const Spacer(),
                        // L'EPINGLE NUE, SANS SON CARRE : voir
                        // ZedjoLogo.surPlaque. Sur la carte, la plaque faisait
                        // un troisieme bouton a cote du menu et de la cloche.
                        const ZedjoWordmark(
                          taille: 28,
                          avecAccroche: false,
                          surPlaque: false,
                        ),
                        const Spacer(),
                        _BoutonRond(
                          icone: Icons.notifications_none,
                          etiquette: 'Notifications',
                          onTap: () => context.push('/notifications'),
                        ),
                      ],
                    ),
                    const SizedBox(height: HbaSpacing.sm),

                    // LE LIVREUR AFFECTE PASSE DEVANT TOUT LE RESTE DU HAUT.
                    //
                    // Depuis les maquettes du 4 octobre 2026. Quand un livreur
                    // roule vers le client, c'est LA raison pour laquelle il
                    // ouvre l'application : le bandeau se met donc la ou l'oeil
                    // tombe, sous la marque, et pas au milieu des boutons de
                    // commande en bas de l'ecran.
                    //
                    // IL REMPLACE LE COMPTEUR, IL NE S'Y AJOUTE PAS. « Trois
                    // livreurs autour de vous » sert a decider de commander ;
                    // quelqu'un dont le colis est deja en route n'a rien a
                    // decider, et deux bandeaux empiles sous la marque
                    // mangeraient le quart de la carte.
                    if (enRoute case final Delivery course)
                      BandeauLivreur(course: course)
                    else
                      // LE COMPTEUR SOUS LA BARRE, CENTRE, EN PASTILLE. Il
                      // etait a gauche et pleine largeur ; sous une barre a
                      // trois elements, il se lisait comme un quatrieme.
                      _Compteur(livreurs: livreurs),
                  ],
                ),
              ),
            ),
          ),

          // LE BAS SE LIT EN DEUX ETAGES, ET LA SEPARATION EST VOULUE.
          //
          // LES CARTES D'ETAT RESTENT POSEES SUR LA CARTE GEOGRAPHIQUE, au-dessus
          // de la feuille. Elles sont blanches ; les mettre DANS une feuille
          // blanche les ferait disparaitre, alors qu'elles disent justement ce
          // qui ne va pas de soi — un paiement a reprendre, une livraison en
          // route. Au-dessus du blanc, chacune garde son filet et son ombre.
          //
          // LA FEUILLE, ELLE, TOUCHE LE BORD BAS DE L'ECRAN et n'arrondit que
          // ses coins hauts : c'est ce qui la fait lire comme un panneau qui
          // glisse, et non comme une carte oubliee au milieu.
          Positioned(
            left: 0,
            right: 0,
            bottom: 0,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Padding(
                  padding: const EdgeInsets.fromLTRB(
                    HbaSpacing.gutter,
                    0,
                    HbaSpacing.gutter,
                    HbaSpacing.sm,
                  ),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      // LA REPRISE DE PAIEMENT PASSE AVANT TOUT LE RESTE, et
                      // c'est le seul endroit ou elle peut apparaitre : une
                      // commande impayee est retiree des listes du client
                      // (point 30), donc aucun ecran de liste ne la montrera
                      // jamais. Sans cette carte, elle n'existe nulle part.
                      if (ref.watch(commandesAPayerProvider).valueOrNull
                          case final List<CommandeAPayer> aPayer
                          when aPayer.isNotEmpty) ...[
                        _CarteDeReprise(commande: aPayer.first),
                        const SizedBox(height: HbaSpacing.sm),
                      ],

                      // LA COURSE EN COURS PASSE AVANT LE BOUTON DE COMMANDE. Un
                      // client qui a une livraison en route ouvre l'application
                      // pour la SUIVRE, pas pour en commander une autre.
                      //
                      // SAUF QUAND LE BANDEAU DU HAUT PORTE DEJA CELLE-CI.
                      // Depuis le 4 octobre 2026, une livraison dont le livreur
                      // est connu s'annonce sous la marque ; la repeter en bas
                      // donnerait deux portes vers le meme suivi, a deux
                      // endroits de l'ecran, pour une seule course.
                      if (enCours.isNotEmpty &&
                          enCours.first.id != enRoute?.id) ...[
                        CarteCourse(course: enCours.first, enAvant: true),
                        const SizedBox(height: HbaSpacing.sm),
                      ],

                      // LA COURSE DE PERSONNES EN COURS, AVANT LES BOUTONS et
                      // pour la meme raison que la livraison juste au-dessus : on
                      // ouvre l'application pour SUIVRE ce qui roule. Cette carte
                      // est aussi le seul chemin de retour vers une course
                      // commencee — sans elle, un passager qui ferme
                      // l'application n'a plus aucune adresse a taper.
                      //
                      // ELLE NE PORTE PLUS D'ESPACEMENT APRES ELLE : quand aucune
                      // course ne roule, cette carte ne dessine rien, et l'ancien
                      // SizedBox laissait alors un trou de huit points que
                      // personne ne pouvait expliquer.
                      const CarteCourseActive(),
                    ],
                  ),
                ),
                // FERMEE, ELLE N'EXISTE PLUS : pas de bandeau blanc au bas de
                // l'ecran, pas de poignee qui depasse. C'est le bouton flottant
                // qui la rouvre, et lui seul — une feuille repliee qui laisse un
                // lisere blanc sur la carte fait croire a un ecran mal coupe.
                if (_feuilleOuverte)
                  _FeuilleDeCommande(
                    onFermer: () => setState(() => _feuilleOuverte = false),
                    onMePlacer: _seSituer,
                  ),
              ],
            ),
          ),

          // LE BOUTON NE S'AFFICHE QUE QUAND LA FEUILLE EST REPLIEE. Ouverte,
          // elle porte deja les deux metiers : un bouton qui ouvrirait ce qui
          // est ouvert n'a rien a dire, et il masquerait la carte pour rien.
          //
          // APRES LA FEUILLE DANS LA PILE, DONC AU-DESSUS. L'ordre compte : pose
          // avant, il passerait sous la feuille pendant l'animation de
          // fermeture et reapparaitrait par saccades.
          if (!_feuilleOuverte)
            _BoutonDeCommande(
              pose: _boutonPose,
              onDeplace: (position) => setState(() => _boutonPose = position),
              onTap: () => setState(() => _feuilleOuverte = true),
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

        _ => RechercheLivreurVue.pourLivraison(
            delivery: course,
            onAnnuler: () => unawaited(annulerLaCourse(context, ref, course)),
          ),
      };

  /// La course qui cherche encore un chauffeur, s'il y en a une.
  ///
  /// DEUX ETATS, ET PAS UN DE PLUS. « Demandee » est l'instant qui suit le
  /// paiement, avant que Ride n'ait lance la vague ; « recherche du chauffeur »
  /// est la vague elle-meme. Des qu'un chauffeur accepte, ce n'est plus une
  /// attente mais un suivi, et l'accueil rend la main.
  Course? _courseEnRecherche() {
    final courses = ref.watch(coursesActivesProvider).valueOrNull ?? const [];
    if (courses.isEmpty) return null;

    final attendue = ref.watch(courseEnAttenteDePaiementProvider);

    // CELLE QU'ON VIENT DE PAYER PASSE AVANT LA LISTE, comme pour les colis :
    // c'est la seule que le passager regarde a cet instant.
    final course = attendue == null
        ? courses.first
        : courses.firstWhere((c) => c.id == attendue, orElse: () => courses.first);

    return course.etat == EtatDeCourse.demandee ||
            course.etat == EtatDeCourse.rechercheDuChauffeur
        ? course
        : null;
  }

  /// Ouvre le suivi quand un chauffeur vient d'accepter.
  ///
  /// IL S'OUVRE TOUT SEUL, ET C'EST LE CHOIX FAIT LE 3 OCTOBRE 2026 : le radar
  /// laisse place au suivi sans geste du passager, comme le fait deja le colis
  /// quelques lignes plus haut. Le passager regardait l'ecran en attendant ;
  /// lui demander un geste de plus a l'instant ou la nouvelle arrive serait
  /// une friction placee au pire moment.
  ///
  /// UNE SEULE FOIS PAR COURSE. Sans « _basculeCourse », chaque reconstruction
  /// empilerait un ecran de suivi de plus.
  void _ouvrirLeSuiviSiUnChauffeurAAccepte() {
    if (!_attendaitUneCourse) return;

    // PAS « firstOrNull » : il n'est pas dans dart:core, il vient de
    // package:collection — que cette application ne declare pas. Le piege s'est
    // deja referme dans ce depot.
    //
    // L'ETAT PLUTOT QUE « chauffeur != null » : les deux disent la meme chose
    // aujourd'hui, mais l'etat est ce que le contrat garantit, et un chauffeur
    // absent de la reponse pour une autre raison n'ouvrirait pas le suivi.
    final courses = ref.watch(coursesActivesProvider).valueOrNull ?? const [];
    final affectees = courses
        .where((c) => const {
              EtatDeCourse.chauffeurAffecte,
              EtatDeCourse.chauffeurEnApproche,
              EtatDeCourse.chauffeurArrive,
              EtatDeCourse.enCours,
            }.contains(c.etat))
        .toList();

    if (affectees.isEmpty) return;

    final course = affectees.first;
    if (_basculeCourse == course.id) return;

    _attendaitUneCourse = false;
    _basculeCourse = course.id;

    final id = course.id;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;

      ref.read(courseEnAttenteDePaiementProvider.notifier).state = null;

      // PAS PAR-DESSUS UN AUTRE ECRAN, pour la meme raison que les colis : le
      // passager peut etre en train de remplir une commande posee sur cet
      // accueil, et lui jeter le suivi par-dessus lui ferait perdre sa saisie.
      if (!(ModalRoute.of(context)?.isCurrent ?? false)) return;

      context.push('/course/$id');
    });
  }

  /// Propose de noter une mission terminee, UNE FOIS, et jamais en travers.
  ///
  /// SEULEMENT DEPUIS CETTE BRANCHE DE L'ACCUEIL, celle ou rien n'attend. Les
  /// branches du dessus rendent le radar ou l'ecran de paiement : y poser une
  /// notation couvrirait une course qui cherche encore son chauffeur par une
  /// question sur celle d'avant.
  ///
  /// RETENUE AVANT LA NAVIGATION, PAS APRES. L'accueil se reconstruit plusieurs
  /// fois par seconde pendant que la carte s'installe ; marquer la mission apres
  /// le « push » laisserait deux constructions empiler deux fois le meme ecran.
  ///
  /// PASSER NE LA FERME PAS COTE SERVICE, et c'est voulu : une note se pose
  /// quand on veut. C'est donc une memoire LOCALE qui evite la boucle — elle ne
  /// survit pas a la fermeture de l'application, et le client reverra la
  /// proposition au prochain demarrage.
  ///
  /// RIEN PENDANT QU'UNE MISSION ROULE. « roule » le dit : une question sur la
  /// course d'hier posee par-dessus la carte d'une course en route couvrirait ce
  /// que le passager est venu regarder.
  void _proposerUneNotation({required bool roule}) {
    // ECOUTE EN PREMIER, ET SANS CONDITION, PARCE QUE L'ECOUTE EST CE QUI
    // RAMENE L'ECRAN ICI. Invalider un provider que personne ne regarde ne
    // reconstruit rien : en sortant avant ce « watch », le rappel du front
    // descendant partait dans le vide et la proposition n'arrivait qu'a la
    // prochaine reconstruction fortuite de l'accueil — c'est-a-dire, une fois
    // la derniere mission finie, plus du tout.
    final enAttente = ref.watch(avisEnAttenteProvider).valueOrNull;

    if (roule) {
      // Ecrit pendant la construction, sans setState : il ne change rien a ce
      // qui est peint, il sert a la construction SUIVANTE.
      _uneMissionRoulait = true;
      return;
    }

    // LA DERNIERE MISSION VIENT DE S'ACHEVER : LA LISTE EN CACHE NE LA CONNAIT
    // PAS. « avisEnAttenteProvider » n'est lu qu'une fois et garde sa reponse ;
    // sans ce rappel, le passager devrait fermer et rouvrir l'application pour
    // qu'on lui propose de noter la course dont il vient de descendre.
    if (_uneMissionRoulait) {
      _uneMissionRoulait = false;

      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) ref.invalidate(avisEnAttenteProvider);
      });

      // On ne propose rien ce tour-ci : la reponse arrive au suivant.
      return;
    }

    if (enAttente == null || enAttente.isEmpty) return;

    final deja = ref.read(avisDejaProposesProvider);

    // PAS « firstWhereOrNull » : il vient de package:collection, que cette
    // application ne declare pas. Meme piege que pour « firstOrNull ».
    AvisEnAttente? aProposer;
    for (final avis in enAttente) {
      // CE QUI N'EST NI COURSE NI LIVRAISON EST IGNORE. La nature vient du
      // service ; ne pas la reconnaitre veut dire que le contrat a bouge, et
      // supposer ferait noter un colis a la place d'une course.
      if (avis.notable && !deja.contains(avis.reference)) {
        aProposer = avis;
        break;
      }
    }

    if (aProposer == null) return;

    final avis = aProposer;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;

      // PAS PAR-DESSUS UN AUTRE ECRAN, pour la meme raison que les deux
      // bascules au-dessus : le passager peut etre en train de remplir une
      // commande posee sur cet accueil.
      if (!(ModalRoute.of(context)?.isCurrent ?? false)) return;

      ref.read(avisDejaProposesProvider.notifier).state = {
        ...ref.read(avisDejaProposesProvider),
        avis.reference,
      };

      context.push('/avis', extra: avis);
    });
  }

  /// Ce que le radar affiche d'une course.
  ///
  /// LES LIBELLES SONT DES COORDONNEES, FAUTE DE MIEUX. Une course de personnes
  /// ne porte pas de reperes saisis au contrat, contrairement a un colis : c'est
  /// deja ce que montre l'ecran de commande, et inventer « Depart » tout court
  /// en dirait moins.
  MissionEnRecherche _missionDeLaCourse(Course course) => MissionEnRecherche(
        depart: LatLng(course.departLatitude, course.departLongitude),
        arrivee: LatLng(course.destinationLatitude, course.destinationLongitude),
        departLibelle: _point(course.departLatitude, course.departLongitude),
        arriveeLibelle:
            _point(course.destinationLatitude, course.destinationLongitude),

        // LE VEHICULE A LA PLACE D'UNE REFERENCE : une course n'en porte pas, et
        // la pastille vide laisserait un trou dans l'en-tete du recapitulatif.
        reference: course.vehicule,
        prixXof: course.prixXof,
        depuis: course.demandeeLe,
        titre: 'Récapitulatif de la course',
      );

  static String _point(double latitude, double longitude) =>
      '${latitude.toStringAsFixed(5)}, ${longitude.toStringAsFixed(5)}';

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
  ///
  /// LE FACTEUR DE LONGITUDE SE CALCULE, IL N'EST PLUS ECRIT EN DUR. Il valait
  /// 0,62 : c'est le cosinus d'une latitude de 51,6 degres — quelque part entre
  /// Londres et Amsterdam. A Cotonou, ou le cosinus vaut 0,994, un ecart
  /// est-ouest etait compte pour 62 % de ce qu'il mesure, et le seuil de cinq
  /// cents metres ne se declenchait donc qu'a huit cents. L'essai francais
  /// prevu avant le deploiement n'aurait rien revele : a Paris, 0,62 tombe
  /// presque juste.
  static double _distanceApprox(LatLng a, LatLng b) {
    const metresParDegre = 111320.0;
    const versRadians = 3.1415926535897932 / 180;

    final dLat = (a.latitude - b.latitude).abs() * metresParDegre;
    final dLng = (a.longitude - b.longitude).abs() *
        metresParDegre *
        maths.cos((a.latitude + b.latitude) / 2 * versRadians).abs();

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

  final AsyncValue<LivreursAlentour> livreurs;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    // AUCUN LIVREUR LIBRE NE VEUT PAS DIRE AUCUN LIVREUR, et c'est tout l'objet
    // de ces deux premiers cas. Le secteur desert demande de renoncer ; le
    // secteur occupe demande d'attendre quelques minutes. Jusqu'au 3 octobre
    // 2026 les deux portaient la meme phrase — celle du desert — et elle
    // poussait le client a renoncer alors qu'un livreur terminait sa course a
    // deux kilometres.
    //
    // « PAS DISPONIBLE », JAMAIS « EN COURSE ». Le serveur rend une difference,
    // pas une raison : il y a la-dedans le livreur en mission, celui qui vient
    // de se deconnecter, celui dont le dossier n'est plus valide. Promettre
    // « il termine sa course » serait inventer ce que personne n'a verifie.
    // LA PHRASE COMPTE CE QUE LA CARTE MONTRE, ET DIT CE QU'ON PEUT EN AVOIR.
    //
    // Depuis le 3 octobre 2026 les epingles portent tout le monde, les eteintes
    // comprises : annoncer « 3 livreurs autour de vous » quand un seul peut
    // venir serait le mensonge inverse de celui qu'on vient de corriger. Le
    // nombre total situe le quartier, le second chiffre dit ce qui est
    // atteignable.
    //
    // « PAS DISPONIBLE », JAMAIS « EN COURSE ». Le serveur rend un booleen, pas
    // une raison : il y a dedans le livreur en mission, celui qui vient de se
    // deconnecter, celui dont le dossier n'est plus valide.
    final texte = switch (livreurs) {
      AsyncData(:final value) when value.desert =>
        'Aucun livreur dans ce secteur pour le moment.',
      AsyncData(:final value) when value.nombreDisponibles == 0 && value.tous.length == 1 =>
        "1 livreur dans le secteur, mais il n'est pas disponible.",
      AsyncData(:final value) when value.nombreDisponibles == 0 =>
        '${value.tous.length} livreurs dans le secteur, aucun disponible '
            'pour le moment.',
      AsyncData(:final value) when value.nombreDisponibles >= _plafondRoute =>
        'Plus de $_plafondRoute livreurs disponibles autour de vous.',
      AsyncData(:final value) when value.nombreDisponibles == value.tous.length =>
        value.tous.length == 1
            ? '1 livreur autour de vous.'
            : '${value.tous.length} livreurs autour de vous.',
      AsyncData(:final value) =>
        '${value.tous.length} livreurs autour de vous, '
            '${value.nombreDisponibles} disponible'
            '${value.nombreDisponibles > 1 ? 's' : ''}.',
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
              AsyncData(:final value) when value.desert =>
                Icons.location_off_outlined,

              // UNE HORLOGE, PAS UNE EPINGLE BARREE : ce qui manque ici n'est
              // pas un livreur, c'est le temps qu'il finisse.
              AsyncData(:final value) when value.nombreDisponibles == 0 =>
                Icons.schedule_outlined,
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
/// Propose de reprendre le paiement d'une commande que l'appareil se rappelle.
///
/// ELLE N'APPARAIT QUE PARCE QUE L'APPAREIL SE SOUVIENT. Le serveur sait reprendre
/// depuis le 29 septembre 2026 — rejouer la cle d'idempotence rend la meme
/// commande et la meme adresse de paiement — mais la cle vivait dans un champ de
/// l'ecran de commande : le systeme tuait l'application pendant la page de
/// paiement, et la cle partait avec. Au retour, plus rien a rejouer, et la
/// commande restait invisible jusqu'a son abandon au bout d'un quart d'heure.
///
/// ELLE DISPARAIT D'ELLE-MEME. Les entrees de plus de quinze minutes ne sont plus
/// rendues par la memoire locale — c'est le delai au-dela duquel le balayage a
/// ferme la commande et le devis a expire. Proposer une reprise apres cela
/// enverrait le client sur un refus.
class _CarteDeReprise extends ConsumerStatefulWidget {
  const _CarteDeReprise({required this.commande});

  final CommandeAPayer commande;

  @override
  ConsumerState<_CarteDeReprise> createState() => _EtatDeLaCarteDeReprise();
}

class _EtatDeLaCarteDeReprise extends ConsumerState<_CarteDeReprise> {
  bool _occupe = false;

  @override
  Widget build(BuildContext context) {
    final course = widget.commande.estUneCourse;

    return _Bandeau(
      icone: Icons.payments_outlined,
      titre: course ? 'Une course attend son paiement' : 'Un colis attend son paiement',
      texte: course
          ? "Votre course n'est pas partie : le paiement n'a pas été confirmé. "
              'Reprenez-le, ou laissez-la expirer.'
          : "Votre colis n'est pas parti : le paiement n'a pas été confirmé. "
              'Reprenez-le, ou laissez-le expirer.',
      action: (_occupe ? 'Reprise…' : 'Reprendre le paiement', _occupe ? () {} : _reprendre),
      secondaire: ('Abandonner', _occupe ? () {} : _abandonner),
    );
  }

  Future<void> _reprendre() async {
    // LE MESSAGER ET LE NAVIGATEUR SONT PRIS AVANT L'ATTENTE : apres elle, le
    // contexte peut avoir disparu, et le chercher alors est la faute que
    // l'analyseur signale sous « use_build_context_synchronously ».
    final messager = ScaffoldMessenger.of(context);
    final navigateur = Navigator.of(context);

    setState(() => _occupe = true);

    try {
      final issue = await reprendreLePaiement(ref, widget.commande);

      if (!mounted) return;

      switch (issue) {
        case ReprisePossible(:final url):
          // LA MEME PAGE QUE TOUT A L'HEURE, ET C'EST LE POINT : l'idempotence
          // du service rend la MEME intention, donc la MEME adresse. Le client
          // qui reprend ne paie pas une seconde fois.
          await navigateur.push<IssuePaiement>(
            MaterialPageRoute(builder: (_) => PaiementWebView(url: url)),
          );

          // ON REDEMANDE AU SERVICE PLUTOT QUE DE CROIRE L'ECRAN. Sortir de la
          // page ne prouve aucun paiement : seul le webhook signe fait foi
          // (ADR 0017). Sans cela, la carte de reprise restait a l'accueil apres
          // un paiement abouti, et proposait de repayer.
          await oublierSiReglee(
            ref,
            estUneCourse: widget.commande.estUneCourse,
            id: widget.commande.id,
          );

        case RepriseSansObjet(:final message):
          messager.showSnackBar(SnackBar(content: Text(message)));
      }
    } on OfflineException {
      messager.showSnackBar(
        const SnackBar(content: Text('Pas de réseau. Réessayez.')),
      );
    } on ApiException catch (erreur) {
      // LE REFUS EST SOUVENT DEFINITIF ICI — devis expire, commande abandonnee —
      // et garder l'entree reproposerait le meme mur a chaque demarrage.
      await ref.read(paiementsEnAttenteProvider).oublier(widget.commande.id);
      messager.showSnackBar(SnackBar(content: Text(erreur.message)));
    } finally {
      ref.invalidate(commandesAPayerProvider);
      if (mounted) setState(() => _occupe = false);
    }
  }

  Future<void> _abandonner() async {
    // ON N'ANNULE RIEN COTE SERVICE, ON OUBLIE SEULEMENT ICI. Le balayage fermera
    // la commande au bout du quart d'heure, et aucun montant n'a ete preleve :
    // appeler l'annulation demanderait un aller-retour pour le meme resultat, et
    // echouerait si la commande venait justement d'etre payee.
    await ref.read(paiementsEnAttenteProvider).oublier(widget.commande.id);
    ref.invalidate(commandesAPayerProvider);
  }
}

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

/// La feuille du bas : ou l'on va, et de quoi commander.
///
/// POURQUOI UNE FEUILLE PLUTOT QUE DES BOUTONS POSES SUR LA CARTE. Les deux
/// boutons flottaient seuls sur la carte geographique, et rien ne disait ou
/// s'arretait la carte et ou commencait l'application. La feuille blanche
/// tranche : au-dessus on regarde, en dessous on agit.
///
/// « OU ALLEZ-VOUS ? » MENE A LA COURSE, PAS AU COLIS. La question est celle
/// d'un passager. Elle ouvre le meme ecran que le bouton « Commander une
/// course » — c'est voulu : le champ est le geste rapide, le bouton est le
/// chemin nomme, et les deux arrivent au meme endroit plutot que de se
/// disputer un demi-ecran.
///
/// LES PASTILLES VIENNENT DES ADRESSES ENREGISTREES DU CLIENT, et de nulle part
/// ailleurs. Rien n'est devine ici : pas de « maison » presumee d'apres les
/// trajets, pas de lieu suggere. Tant que le client n'a rien enregistre, la
/// rangee n'existe pas — une rangee d'exemples lui ferait croire que
/// l'application sait ou il habite.
///
/// ELLE SE REPLIE, PARCE QU'ELLE MANGE LE TIERS BAS DE LA CARTE. Un client qui
/// regarde ou sont les livreurs, ou qui cherche une rue sous la feuille, doit
/// pouvoir la pousser hors du chemin. La poignee la ferme et la rouvre, au
/// doigt comme a l'appui.
///
/// REPLIEE, ELLE NE DISPARAIT PAS : il reste la bande de la poignee. Une
/// feuille qui s'efface entierement laisserait une carte sans aucun bouton, et
/// le geste pour la faire revenir ne s'inventerait pas. La bande est ce qui
/// rend le repli REVERSIBLE, et c'est pour cela qu'elle ne peut pas partir.
///
/// L'ETAT NE SURVIT PAS A LA FERMETURE DE L'APPLICATION, et c'est delibere.
/// Replier est un geste du moment — « laisse-moi voir cette rue » — pas un
/// reglage. Le retrouver ferme au lancement suivant ferait croire a une
/// application amputee de ses boutons.
class _FeuilleDeCommande extends ConsumerStatefulWidget {
  const _FeuilleDeCommande({
    required this.onFermer,
    required this.onMePlacer,
  });

  /// ELLE NE SAIT QUE SE FERMER. Elle n'est construite que lorsqu'elle est
  /// ouverte : un etat « ouverte » qu'elle porterait serait toujours vrai, et
  /// un rappel « bascule » lui ferait demander son propre retour alors qu'elle
  /// n'existera plus.
  final VoidCallback onFermer;

  /// Recentre la carte sur le client.
  ///
  /// C'EST L'ECRAN QUI SAIT LE FAIRE, PAS LA FEUILLE. La position, le
  /// controleur de carte et le centre de la requete vivent dans l'etat de
  /// l'ecran ; la feuille ne fait que transmettre l'appui.
  final VoidCallback onMePlacer;

  @override
  ConsumerState<_FeuilleDeCommande> createState() => _FeuilleDeCommandeState();
}

class _FeuilleDeCommandeState extends ConsumerState<_FeuilleDeCommande> {
  /// Trois pastilles au plus.
  ///
  /// LA RANGEE NE DOIT PAS DEFILER. Au-dela de trois, les libelles se coupent
  /// ou la rangee part en glissement horizontal, que personne ne devine sur un
  /// bandeau de cette hauteur. Le profil garde la liste complete.
  static const _maxPastilles = 3;

  /// Au-dela de cette vitesse, le sens du doigt decide seul.
  ///
  /// UN GLISSEMENT LENT NE DOIT PAS BASCULER LA FEUILLE. Le pouce frole la
  /// poignee en visant un bouton ; sans seuil, la feuille se fermerait sous le
  /// doigt de quelqu'un qui voulait commander.
  static const _vitesseDuGeste = 120.0;

  void _glissement(DragEndDetails details) {
    final vitesse = details.velocity.pixelsPerSecond.dy;
    if (vitesse.abs() < _vitesseDuGeste) return;

    // VERS LE BAS ON FERME, ET VERS LE HAUT ON NE FAIT RIEN : la feuille est
    // deja en haut. Le seuil de vitesse reste la pour qu'un pouce qui frole la
    // poignee en visant un bouton ne la referme pas sous lui.
    if (vitesse > 0) widget.onFermer();
  }

  void _versLaCourse(BuildContext context, {PickedPoint? destination}) =>
      context.push('/nouvelle-course', extra: destination);

  @override
  Widget build(BuildContext context) {
    final adresses =
        ref.watch(adressesProvider).valueOrNull ?? const <AdresseFavorite>[];
    final pastilles = adresses.take(_maxPastilles).toList();

    return DecoratedBox(
      decoration: const BoxDecoration(
        color: HbaColors.surface,

        // ARRONDIE EN HAUT SEULEMENT : c'est ce coin-la qui dit qu'elle glisse.
        // En bas, elle touche le bord de l'ecran, et un arrondi y laisserait
        // deux triangles de carte geographique sous le pouce.
        borderRadius: BorderRadius.vertical(
          top: Radius.circular(HbaRadius.feuille),
        ),
        boxShadow: [
          BoxShadow(
            color: HbaColors.ombre,
            blurRadius: 24,
            offset: Offset(0, -6),
          ),
        ],
      ),

      // « top: false » : la feuille n'est pas en haut de l'ecran, et reclamer
      // l'encoche y ajouterait une bande vide de quarante points.
      child: SafeArea(
        top: false,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            _Poignee(onTap: widget.onFermer, onGlissement: _glissement),
            _Contenu(
              pastilles: pastilles,
              versLaCourse: _versLaCourse,
              onMePlacer: widget.onMePlacer,
            ),
          ],
        ),
      ),
    );
  }
}

/// La barre qui ferme et rouvre la feuille.
///
/// ELLE REPOND AU DOIGT ET A L'APPUI, parce que les deux se tentent. Le trait
/// gris est le signe universel d'une feuille qu'on tire ; mais beaucoup de gens
/// tapent dessus, et une poignee qui ne repond qu'au glissement passe pour
/// decorative.
///
/// LA ZONE SENSIBLE EST LARGE, LE TRAIT EST FIN. Quatre points de haut ne se
/// visent pas au pouce : la rangee entiere capte le geste, et « opaque » lui
/// fait recevoir aussi les marges, qui sont l'essentiel de sa surface.
class _Poignee extends StatelessWidget {
  const _Poignee({
    required this.onTap,
    required this.onGlissement,
  });

  final VoidCallback onTap;
  final void Function(DragEndDetails) onGlissement;

  @override
  Widget build(BuildContext context) => Semantics(
        button: true,
        label: 'Masquer les actions',
        child: GestureDetector(
          behavior: HitTestBehavior.opaque,
          onTap: onTap,
          onVerticalDragEnd: onGlissement,
          child: Container(
            width: double.infinity,
            padding: const EdgeInsets.symmetric(vertical: HbaSpacing.md),
            alignment: Alignment.center,
            child: Container(
              height: 4,
              width: 44,
              decoration: BoxDecoration(
                color: HbaColors.border,
                borderRadius: BorderRadius.circular(2),
              ),
            ),
          ),
        ),
      );
}

/// Ce que la feuille porte.
///
/// SEPARE DE LA POIGNEE, et il le reste. Le repli anime a disparu le 4 octobre
/// 2026 — la feuille se demonte entierement — mais garder les deux distincts
/// laisse la poignee lisible : elle ferme, et rien d'autre.
class _Contenu extends StatelessWidget {
  const _Contenu({
    required this.pastilles,
    required this.versLaCourse,
    required this.onMePlacer,
  });

  final List<AdresseFavorite> pastilles;
  final void Function(BuildContext, {PickedPoint? destination}) versLaCourse;
  final VoidCallback onMePlacer;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.fromLTRB(
        HbaSpacing.gutter,
        0,
        HbaSpacing.gutter,
        HbaSpacing.md,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          // LE BOUTON DE POSITION EST COLLE AU CHAMP, PAS POSE SUR LA CARTE.
          //
          // Depuis les maquettes du 4 octobre 2026. Sur la carte, il aurait
          // recouvert une rue de plus et il se serait confondu avec le bouton
          // flottant de commande, qui est rond et vert lui aussi. A droite de
          // « Où allez-vous ? », il se lit comme ce qu'il est : remplir le
          // depart avec l'endroit ou le client se trouve.
          Row(
            children: [
              Expanded(
                child: HbaPressable(
                  radius: HbaRadius.field,
                  color: HbaColors.surfaceSunken,
                  padding: const EdgeInsets.symmetric(
                    horizontal: HbaSpacing.md,
                    vertical: HbaSpacing.sm,
                  ),
                  alignment: Alignment.centerLeft,
                  semantique: 'Où allez-vous ?',
                  onTap: () => versLaCourse(context),
                  child: Row(
                    children: [
                      const Icon(
                        Icons.search,
                        size: 20,
                        color: HbaColors.inkMuted,
                      ),
                      const SizedBox(width: HbaSpacing.sm),
                      Expanded(
                        child: Text(
                          'Où allez-vous ?',
                          style: theme.textTheme.bodyLarge
                              ?.copyWith(color: HbaColors.inkMuted),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
              const SizedBox(width: HbaSpacing.sm),
              _BoutonMePlacer(onTap: onMePlacer),
            ],
          ),
          const SizedBox(height: HbaSpacing.sm),

          // SANS ADRESSE ENREGISTREE, LA RANGEE INVITE AU LIEU DE DISPARAITRE.
          //
          // Les maquettes montrent deux raccourcis « Maison » et « Bureau »
          // qui proposent « Ajouter votre adresse » tant que rien n'est
          // enregistre. UN SEUL CARTON EST DESSINE ICI, ET SANS NOM IMPOSE :
          // une adresse favorite ne porte qu'un libelle libre dans ce projet
          // — aucun type « maison » ou « bureau » n'existe cote service —
          // donc deux cartons nommes d'avance promettraient deux
          // emplacements que rien ne sait remplir.
          if (pastilles.isEmpty)
            _InvitationAdresse(onTap: () => context.push('/profil'))

          // INTRINSICHEIGHT : un raccourci sans repere n'a qu'une ligne, son
          // voisin en a deux, et sans mesure commune la rangee montre deux
          // cartons de hauteurs differentes. Le cout est nul pour trois
          // cartons.
          else
            IntrinsicHeight(
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  // L'INDEX SERT A L'ESPACEMENT, PAS A L'IDENTITE. Comparer
                  // l'adresse a la derniere de la liste reposerait sur
                  // l'egalite d'objets, qu'AdresseFavorite ne definit pas :
                  // deux lieux au meme nom seraient alors separes ou collés
                  // au hasard des instances.
                  for (final (rang, adresse) in pastilles.indexed) ...[
                    if (rang > 0) const SizedBox(width: HbaSpacing.sm),
                    Expanded(
                      child: _Pastille(
                        adresse: adresse,
                        onTap: () => versLaCourse(
                          context,
                          destination: PickedPoint(
                            latitude: adresse.latitude,
                            longitude: adresse.longitude,
                          ),
                        ),
                      ),
                    ),
                  ],
                ],
              ),
            ),
          const SizedBox(height: HbaSpacing.md),

          // DEUX METIERS, DEUX TUILES, ET LE COLIS RESTE EN PREMIER.
          //
          // C'est le metier d'origine et le plus demande a Cotonou ; le
          // transport de personnes s'ajoute a cote, sans deplacer le geste que
          // les clients connaissent deja.
          //
          // DES TUILES ET NON DEUX BOUTONS, depuis les maquettes du 4 octobre
          // 2026. Deux boutons empiles se lisent comme une action principale et
          // un repli ; ce sont deux metiers de rang egal, et la ligne
          // d'explication sous chaque titre est ce qui permet a quelqu'un qui
          // ouvre l'application pour la premiere fois de choisir sans se
          // tromper.
          //
          // COTE A COTE ET NON L'UN SOUS L'AUTRE, depuis le 4 octobre 2026.
          // Empilees, les deux tuiles prenaient la moitie de la feuille et
          // repoussaient la carte hors de l'ecran ; a hauteur egale, elles se
          // comparent d'un regard — ce qui est exactement le geste demande.
          //
          // INTRINSICHEIGHT POUR QUE LES DEUX TUILES AIENT LA MEME HAUTEUR.
          // Celle qui est mise en avant porte un filet plus epais, donc elle
          // depasse l'autre de deux points — assez pour que l'oeil voie un
          // defaut d'alignement. Le cout d'une passe de mesure supplementaire
          // est nul pour deux cartes.
          IntrinsicHeight(
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
              Expanded(
                child: _TuileService(
                  image: 'assets/services/colis.png',
                  titre: 'Livraison\nde colis',
                  sousTitre: 'Envoyez vos colis en toute sécurité',
                  enAvant: true,
                  onTap: () => context.push('/nouvelle'),
                ),
              ),
              const SizedBox(width: HbaSpacing.sm),
              Expanded(
                child: _TuileService(
                  image: 'assets/services/voiture.png',
                  titre: 'Transport\nde personnes',
                  sousTitre: 'Réservez une course en quelques secondes',
                  onTap: () => versLaCourse(context),
                ),
              ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// Un lieu enregistre, en raccourci.
///
/// LE REPERE EST REVENU SOUS LE LIBELLE, le 4 octobre 2026. Il en avait ete
/// retire comme « utile au livreur et inutile ici » ; les maquettes le
/// redemandent, et avec raison : « Maison » ne distingue pas deux maisons, et
/// un client qui en a enregistre plusieurs ne sait pas laquelle il presse.
/// Une ligne, coupee, suffit a trancher — le profil le montre en entier.
///
/// UN CARTON ET NON UNE GELULE. Deux lignes de texte dans un rayon de 999
/// points laissent deux croissants vides a gauche et a droite.
class _Pastille extends StatelessWidget {
  const _Pastille({required this.adresse, required this.onTap});

  final AdresseFavorite adresse;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final repere = adresse.repere.trim();

    return HbaPressable(
      radius: HbaRadius.field,
      color: HbaColors.surfaceSunken,
      elevation: HbaElevation.douce,
      padding: const EdgeInsets.symmetric(
        horizontal: HbaSpacing.sm,
        vertical: HbaSpacing.sm,
      ),
      alignment: Alignment.centerLeft,
      semantique: 'Aller à ${adresse.libelle}',
      onTap: onTap,
      child: Row(
        children: [
          Icon(
            adresse.parDefaut ? Icons.star_rounded : Icons.place_outlined,
            size: 18,
            color: HbaColors.primary,
          ),
          const SizedBox(width: HbaSpacing.xs),
          Expanded(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  adresse.libelle,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodyMedium
                      ?.copyWith(fontWeight: FontWeight.w600),
                ),
                if (repere.isNotEmpty)
                  Text(
                    repere,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: theme.textTheme.bodySmall
                        ?.copyWith(color: HbaColors.inkMuted),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// Le carton qui remplace les raccourcis quand le client n'a rien enregistre.
///
/// IL MENE AU PROFIL, PAS A UN FORMULAIRE. Le formulaire d'adresse vit dans le
/// bloc « Mes adresses » du profil ; le dupliquer ici ferait deux endroits ou
/// l'on ajoute la meme chose, qui divergeraient a la premiere correction.
///
/// IL NE NOMME PAS L'ADRESSE A L'AVANCE. Une adresse favorite ne porte qu'un
/// libelle libre dans ce projet : annoncer « Maison » puis laisser le client
/// ecrire « Chez ma mere » ferait mentir le carton.
class _InvitationAdresse extends StatelessWidget {
  const _InvitationAdresse({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaPressable(
      radius: HbaRadius.field,
      color: HbaColors.surfaceSunken,
      elevation: HbaElevation.douce,
      padding: const EdgeInsets.symmetric(
        horizontal: HbaSpacing.sm,
        vertical: HbaSpacing.sm,
      ),
      alignment: Alignment.centerLeft,
      semantique: 'Ajouter une adresse favorite',
      onTap: onTap,
      child: Row(
        children: [
          const Icon(
            Icons.add_location_alt_outlined,
            size: 18,
            color: HbaColors.primary,
          ),
          const SizedBox(width: HbaSpacing.xs),
          Expanded(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Ajouter votre adresse',
                  style: theme.textTheme.bodyMedium
                      ?.copyWith(fontWeight: FontWeight.w600),
                ),
                Text(
                  'Maison, bureau : un appui pour commander',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodySmall
                      ?.copyWith(color: HbaColors.inkMuted),
                ),
              ],
            ),
          ),
          const Icon(
            Icons.chevron_right,
            size: 20,
            color: HbaColors.inkMuted,
          ),
        ],
      ),
    );
  }
}

/// Le rond vert qui recentre la carte sur le client.
///
/// VERT PLEIN, A LA DIFFERENCE DES RONDS BLANCS DU HAUT. Ceux-la sont poses
/// sur la carte et doivent s'en detacher sans la commenter ; celui-ci est dans
/// la feuille, a cote du champ de depart, et il agit sur ce champ — la couleur
/// de l'application le dit.
///
/// IL NE PROMET PAS DE REUSSIR. La position peut etre refusee ou indisponible :
/// l'ecran garde alors Cotonou, sans message d'erreur, parce qu'un client peut
/// commander en posant ses points a la main.
class _BoutonMePlacer extends StatelessWidget {
  const _BoutonMePlacer({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
        button: true,
        label: 'Me localiser',
        child: Material(
          color: HbaColors.primary,
          shape: const CircleBorder(),
          elevation: 1.5,
          shadowColor: HbaColors.ombre,
          child: InkWell(
            customBorder: const CircleBorder(),
            onTap: onTap,
            child: const SizedBox(
              height: HbaSpacing.cible,
              width: HbaSpacing.cible,
              child: Icon(
                Icons.my_location,
                size: 22,
                color: HbaColors.lumiere,
              ),
            ),
          ),
        ),
      );
}

/// Un rond blanc sur la carte : le menu, la cloche.
///
/// LE FOND EST PLEIN, PAS TRANSPARENT. Une icone posee nue sur une carte
/// devient illisible des qu'elle passe sur un batiment sombre ou une route ;
/// le disque blanc lui donne son contraste quel que soit ce qu'il y a dessous.
class _BoutonRond extends StatelessWidget {
  const _BoutonRond({
    required this.icone,
    required this.etiquette,
    required this.onTap,
  });

  final IconData icone;
  final String etiquette;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
        button: true,
        label: etiquette,
        child: Material(
          color: HbaColors.surface,
          shape: const CircleBorder(),
          elevation: 1.5,
          shadowColor: HbaColors.ombre,
          child: InkWell(
            customBorder: const CircleBorder(),
            onTap: onTap,
            child: SizedBox(
              height: HbaSpacing.cible,
              width: HbaSpacing.cible,
              child: Icon(icone, size: 22, color: HbaColors.ink),
            ),
          ),
        ),
      );
}

/// Un metier, en tuile : son image, et son nom.
///
/// UNE IMAGE ET NON UNE ICONE DE MATERIAL. Un carton dessine en trois
/// dimensions et une voiture se reconnaissent sans etre lus — ce qui compte
/// pour quelqu'un qui ouvre l'application en marchant, et pour quelqu'un qui
/// lit mal. Deux pictogrammes au trait se ressemblaient trop a cette taille.
///
/// LE SOUS-TITRE EST REVENU LE 4 OCTOBRE 2026, demande par les maquettes. Il
/// avait ete retire a la mise cote a cote parce qu'il prenait trois lignes dans
/// une tuile deux fois moins large — ce qui reste vrai, et c'est le prix
/// accepte : la feuille monte d'une cinquantaine de points. Ce qu'il apporte
/// vaut cela pour quelqu'un qui ouvre l'application la premiere fois et doit
/// choisir entre deux metiers qu'il ne connait pas encore.
///
/// LE CHEVRON NE SE PRESSE PAS TOUT SEUL. C'est la tuile entiere qui est le
/// bouton ; le rond vert dit seulement « ceci ouvre quelque chose ». Il est
/// retire de l'arbre d'accessibilite pour ne pas annoncer une deuxieme cible
/// a un lecteur d'ecran, qui n'en trouverait pas.
class _TuileService extends StatelessWidget {
  const _TuileService({
    required this.image,
    required this.titre,
    required this.sousTitre,
    required this.onTap,
    this.enAvant = false,
  });

  final String image;

  /// Le nom du metier, coupe a la main par un saut de ligne.
  ///
  /// LA COUPURE EST CHOISIE, PAS SUBIE. « Transport de personnes » casse apres
  /// « Transport » ; laisse au moteur de rendu, il casserait apres « de » sur
  /// un ecran etroit, et les deux tuiles n'auraient plus la meme hauteur.
  final String titre;

  /// La ligne qui dit a quoi sert le metier.
  final String sousTitre;

  final VoidCallback onTap;

  /// Met la tuile en avant : filet vert et fond teinte. Le colis le porte,
  /// parce qu'il est le metier d'origine et le plus demande.
  final bool enAvant;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      onTap: onTap,
      highlighted: enAvant,
      padding: const EdgeInsets.symmetric(
        horizontal: HbaSpacing.sm,
        vertical: HbaSpacing.md,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          // L'IMAGE ET LE CHEVRON PARTAGENT LA MEME BANDE. Le rond se pose au
          // coin bas-droit de l'illustration, comme sur les maquettes ; mis
          // sous le texte, il aurait rajoute une rangee a une tuile qui en a
          // deja trois.
          SizedBox(
            height: 76,
            width: double.infinity,
            child: Stack(
              fit: StackFit.passthrough,
              children: [
                Center(
                  child: Image.asset(
                    image,
                    height: 76,
                    fit: BoxFit.contain,

                    // SI L'IMAGE MANQUE, LA TUILE RESTE UTILISABLE. Flutter
                    // affiche sinon un rectangle gris barre, et le client ne
                    // sait plus sur quoi il appuie — alors que le titre, lui,
                    // est toujours la.
                    errorBuilder: (_, __, ___) => const SizedBox.shrink(),
                  ),
                ),
                const Positioned(
                  right: 0,
                  bottom: 0,
                  child: ExcludeSemantics(
                    child: DecoratedBox(
                      decoration: BoxDecoration(
                        color: HbaColors.primary,
                        shape: BoxShape.circle,
                      ),
                      child: Padding(
                        padding: EdgeInsets.all(5),
                        child: Icon(
                          Icons.chevron_right,
                          size: 16,
                          color: HbaColors.lumiere,
                        ),
                      ),
                    ),
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: HbaSpacing.sm),
          Text(
            titre,
            textAlign: TextAlign.center,
            style: theme.textTheme.bodyMedium?.copyWith(
              fontWeight: FontWeight.w600,
              height: 1.25,
            ),
          ),
          const SizedBox(height: HbaSpacing.xs),
          Text(
            sousTitre,
            textAlign: TextAlign.center,

            // TROIS LIGNES AU PLUS, PUIS ON COUPE. Une quatrieme ligne
            // repousserait la carte geographique hors de l'ecran sur un
            // telephone de cinq pouces.
            maxLines: 3,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.bodySmall?.copyWith(
              color: HbaColors.inkMuted,
              height: 1.25,
            ),
          ),
        ],
      ),
    );
  }
}

/// Le bouton qui rouvre la feuille de commande, et qui se deplace.
///
/// POURQUOI IL EXISTE. Repliee, la feuille ne se rouvrait que par sa poignee —
/// un trait gris de quatre points de haut, en bas de l'ecran. C'est le signe
/// universel d'une feuille qu'on tire, mais il ne dit pas CE QU'ELLE CONTIENT :
/// quelqu'un qui vient commander ne devine pas que la commande est derriere ce
/// trait. Un bouton rond avec un plus le dit.
///
/// POURQUOI IL SE DEPLACE. C'est le seul element de cet ecran qui recouvre la
/// carte sans avoir ete demande, et la carte est ce que le client regarde : il
/// cache forcement quelque chose — une rue, une epingle, le nom d'un quartier.
/// Plutot que de choisir a sa place le coin qui gene le moins, on le laisse
/// pousser le bouton ailleurs. Le geste est connu : c'est celui de la bulle de
/// discussion des applications de messagerie.
///
/// POURQUOI IL DISPARAIT. Feuille ouverte, les deux metiers sont a l'ecran :
/// un bouton qui ouvre ce qui est deja ouvert ne dit rien et masque la carte
/// pour rien.
class _BoutonDeCommande extends StatelessWidget {
  const _BoutonDeCommande({
    required this.pose,
    required this.onDeplace,
    required this.onTap,
  });

  /// La position choisie par le client, ou nul tant qu'il n'a rien deplace.
  final Offset? pose;

  final ValueChanged<Offset> onDeplace;
  final VoidCallback onTap;

  static const _cote = 56.0;

  /// La marge que le bouton ne franchit pas.
  ///
  /// IL NE DOIT PAS POUVOIR SORTIR DE L'ECRAN, ni se glisser sous la barre du
  /// haut ou sous la feuille : un bouton a moitie dehors ne se rattrape plus,
  /// et il faudrait fermer l'application pour le retrouver.
  static const _marge = HbaSpacing.gutter;

  @override
  Widget build(BuildContext context) {
    final taille = MediaQuery.sizeOf(context);
    final encoches = MediaQuery.paddingOf(context);

    // LES BORNES SE CALCULENT A CHAQUE CONSTRUCTION, ET NON UNE FOIS. Un
    // telephone pivote, un clavier monte, une barre systeme apparait : des
    // bornes figees laisseraient le bouton dehors apres le premier changement.
    // LA BORNE GAUCHE, ELLE, NE DEPEND DE RIEN : c'est la marge, et elle est
    // connue a la compilation. Les trois autres se calculent.
    const gaucheMin = _marge;
    final gaucheMax = taille.width - _cote - _marge;

    // EN HAUT : sous la barre ZEDJO et sa pastille de comptage, qui tiennent
    // environ cent vingt points sous l'encoche.
    final hautMin = encoches.top + 120;

    // EN BAS : au-dessus de la poignee de la feuille repliee, encoche du bas
    // comprise.
    final hautMax = taille.height - _cote - encoches.bottom - 96;

    // SUR UN TRES PETIT ECRAN, LES DEUX BORNES PEUVENT SE CROISER — et
    // « clamp » leve quand la borne basse depasse la haute. Mieux vaut un
    // bouton un peu haut qu'une exception au premier affichage.
    final basse = hautMax < hautMin ? hautMin : hautMax;

    final defaut = Offset(gaucheMax, basse);
    final voulue = pose ?? defaut;

    final position = Offset(
      voulue.dx.clamp(gaucheMin, gaucheMax < gaucheMin ? gaucheMin : gaucheMax),
      voulue.dy.clamp(hautMin, basse),
    );

    return Positioned(
      left: position.dx,
      top: position.dy,
      child: GestureDetector(
        // LE DEPLACEMENT SE SUIT AU DOIGT, PAS A LA FIN DU GESTE. Un bouton qui
        // saute a sa place une fois le doigt leve ne se place pas, il se devine.
        onPanUpdate: (details) => onDeplace(position + details.delta),
        child: Semantics(
          button: true,
          label: 'Commander',
          hint: 'Appuyez pour commander, faites glisser pour déplacer le bouton',
          child: Material(
            color: HbaColors.primary,
            shape: const CircleBorder(),
            elevation: 4,
            shadowColor: HbaColors.ombre,
            child: InkWell(
              customBorder: const CircleBorder(),
              onTap: onTap,
              child: const SizedBox(
                height: _cote,
                width: _cote,
                child: Icon(Icons.add, color: HbaColors.surface, size: 28),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

