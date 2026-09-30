import 'package:hba_core/hba_core.dart';
import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:geolocator/geolocator.dart';
import 'package:go_router/go_router.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_ui/hba_ui.dart';

import '../../core/plantages.dart';
import '../../core/preferences.dart';
import '../../core/service_en_ligne.dart';
import '../../core/signal.dart';
import '../auth/session_controller.dart';
import '../profil/profil_repository.dart';
import '../missions/mission_providers.dart';
import '../missions/mission_repository.dart';
import '../missions/mission_screen.dart';
import '../missions/models.dart';
import '../missions/offer_sheet.dart';
import 'bascule_flottante.dart';
import 'carte_livreur.dart';

/// Accueil du livreur : en ligne ou hors ligne, la course en cours s'il y en a
/// une, et l'offre qui arrive.
class HomeScreen extends ConsumerStatefulWidget {
  const HomeScreen({super.key});

  @override
  ConsumerState<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends ConsumerState<HomeScreen>
    with WidgetsBindingObserver {
  /// Intervalle de sondage des offres. Une notification poussee prendra le
  /// relais ; tant qu'elle n'existe pas, sonder reste le moyen le plus simple
  /// de ne pas rater une vague.
  static const _pollInterval = Duration(seconds: 5);

  /// Intervalle d'envoi de la position.
  ///
  /// CE NOMBRE EST LIE A UN AUTRE, DANS UN AUTRE DEPOT. Le service Driver
  /// ecarte des recherches toute position plus vieille que
  /// `DriverLocations:FreshnessSeconds` — 120 s par defaut. Vingt secondes
  /// laissent donc passer cinq echecs consecutifs avant que le livreur ne
  /// disparaisse des vagues. Allonger l'un sans regarder l'autre fait
  /// s'evanouir des livreurs en ligne, sans erreur nulle part.
  static const _positionInterval = Duration(seconds: 20);

  /// Cadence de relecture du dossier tant qu'il n'est pas valide.
  static const _cadenceDossier = Duration(seconds: 30);

  /// Age au-dela duquel le serveur cesse de compter ce livreur.
  ///
  /// CE NOMBRE EST LA COPIE D'UN AUTRE, DANS UN AUTRE DEPOT :
  /// `DriverLocations:FreshnessSeconds`, 120 s par defaut. Le recopier ici est
  /// un compromis assume — aucune route ne le publie, et sans lui l'ecran ne
  /// peut pas savoir a partir de quand il ment. S'ils divergent, c'est
  /// l'ecran qui aura tort, et il le fera en affichant du vert.
  static const _fenetreDeFraicheur = Duration(seconds: 120);

  bool _online = false;

  /// Instant du dernier envoi de position ACCEPTE par le serveur.
  ///
  /// C'EST LUI, ET PAS LA BASCULE, QUI DIT SI LE LIVREUR EXISTE POUR LE
  /// DISPATCH. Les deux se confondaient : l'interrupteur etait sur « en
  /// ligne », donc l'ecran affichait du vert — y compris quand plus aucune
  /// position ne partait depuis dix minutes, et que le serveur avait donc
  /// cesse de proposer la moindre course. Le livreur attendait en croyant
  /// travailler.
  DateTime? _positionAcceptee;

  /// Instant ou l'application est passee en arriere-plan.
  DateTime? _endormieDepuis;

  /// Actions faites par le livreur qui ne sont pas encore parties.
  int _enAttente = 0;
  bool _busy = false;
  String? _notice;
  Mission? _mission;

  /// Vrai entre l'acceptation d'une offre et le moment ou le serveur rend la
  /// course lisible. Voir [_attendreLaCourse].
  bool _attendLaCourse = false;

  Timer? _poller;
  Timer? _heartbeat;

  /// Veille sur le dossier, tant qu'il n'est pas valide. Voir
  /// [_relireLeDossier].
  Timer? _veille;
  bool _sheetOpen = false;

  /// Le livreur s'est mis hors ligne, mais le SERVEUR ne le sait pas encore.
  ///
  /// SANS CE DRAPEAU, LES DEUX CORRECTIONS DU 30 SEPTEMBRE 2026 SE
  /// CONTREDISENT. Le passage hors ligne qui echoue coupe tout localement
  /// (S3) ; la resynchronisation au retour, elle, lit l'etat du serveur et
  /// remet en ligne ce qu'il tient pour disponible (S5). Sans memoire de
  /// l'intention, la seconde defait la premiere : le livreur se met hors
  /// ligne, revient dans l'application, et se retrouve en ligne sans avoir
  /// rien demande.
  bool _horsLigneAConfirmer = false;

  /// Derniere position connue, pour la carte. Elle n'est pas relue pour
  /// l'affichage : le battement en produit deja une toutes les vingt
  /// secondes, et un second lecteur doublerait la consommation du GPS.
  LatLng? _ici;

  @override
  void initState() {
    super.initState();
    // LE TIRER-POUR-RAFRAICHIR N'EXISTE PLUS : on ne tire pas une carte vers
    // le bas, on la fait glisser. La relecture se fait donc au retour de
    // l'application — c'est le moment ou le livreur revient de son appli de
    // navigation, et ou le dossier a pu etre valide entre-temps.
    WidgetsBinding.instance.addObserver(this);
    Future.microtask(_refreshMission);
    Future.microtask(_positionInitiale);

    // AU DEMARRAGE AUSSI : une action mise en file hier soir doit partir des
    // la premiere ouverture, sans attendre que le livreur se remette en ligne.
    Future.microtask(_viderLaFile);

    // C'EST LE SERVEUR QUI SAIT SI LE LIVREUR EST EN LIGNE, PAS CET ECRAN.
    Future.microtask(_reprendreLEtatDuServeur);

    // TANT QUE LE DOSSIER N'EST PAS VALIDE, ON LE GUETTE. La veille se coupe
    // toute seule au premier battement qui le trouve valide.
    _veille = Timer.periodic(_cadenceDossier, (_) => _relireLeDossier());

    // ON REVEILLE LE REGLAGE DU SON MAINTENANT, PAS A LA PREMIERE OFFRE. Sa
    // lecture dans le coffre est asynchrone et rend « actif » en attendant :
    // demandee au moment de l'offre, elle ferait sonner une fois le telephone
    // d'un livreur qui avait coupe le son. Demandee ici, elle a tout le temps
    // d'aboutir.
    ref.read(sonDOffreProvider);
  }

  /// Le livreur est-il reellement joignable par le dispatch ?
  ///
  /// « EN LIGNE » EST UNE INTENTION, « A PORTEE » EST UN FAIT. La bascule dit
  /// ce que le livreur veut ; ce booleen dit ce que le serveur sait de lui. Un
  /// telephone en poche, un sous-sol, un forfait epuise : dans les trois cas
  /// l'intention reste et le fait disparait.
  bool get _aPortee {
    final derniere = _positionAcceptee;
    if (derniere == null) return false;

    return DateTime.now().difference(derniere) < _fenetreDeFraicheur;
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState etat) {
    switch (etat) {
      case AppLifecycleState.resumed:
        unawaited(_reprendre());

      case AppLifecycleState.paused:
      case AppLifecycleState.hidden:
        _endormieDepuis ??= DateTime.now();

      case AppLifecycleState.inactive:
      case AppLifecycleState.detached:
        // « inactive » arrive pour un appel entrant ou le centre de controle,
        // et repasse a « resumed » aussitot : le compter comme un sommeil
        // afficherait une alerte a chaque notification recue.
        break;
    }
  }

  /// Retour de l'application au premier plan.
  ///
  /// LES MINUTERIES NE SONT PAS ARRETEES QUAND ON PART, ET C'EST DELIBERE.
  /// Android les etrangle de lui-meme en arriere-plan, mais il en laisse
  /// parfois passer ; les annuler transformerait une panne PROBABLE en panne
  /// CERTAINE. Surtout, le cas le plus frequent d'arriere-plan est le livreur
  /// qui ouvre son application de navigation AU MILIEU D'UNE COURSE — le
  /// client suit sa position pendant ce temps-la, et la couper serait un
  /// recul.
  ///
  /// CE QU'ON FAIT AU RETOUR, EN REVANCHE : une position tout de suite, sans
  /// attendre le prochain battement, et un sondage d'offre dans la foulee.
  Future<void> _reprendre() async {
    final endormie = _endormieDepuis;
    _endormieDepuis = null;

    await _refreshMission();
    unawaited(_viderLaFile());

    // L'ETAT OPERATIONNEL SE RELIT ICI, ET C'EST LA CORRECTION S5. Il ne se
    // lisait qu'au demarrage de l'ecran ; or « _refreshMission » vient
    // d'invalider le profil et de l'attendre, donc la fiche lue ci-dessous est
    // fraiche et ne coute aucun appel de plus.
    await _reprendreLEtatDuServeur();

    if (!mounted || !_online) return;

    // LE SILENCE SE MESURE SUR LA DERNIERE POSITION ACCEPTEE, pas sur la duree
    // du sommeil : une mise en veille de trois minutes pendant laquelle deux
    // battements sont quand meme passes n'a rien coupe du tout.
    final coupe = !_aPortee && endormie != null;

    await _pushPosition();
    unawaited(_pollOffer());

    if (!mounted || !coupe) return;

    final minutes = DateTime.now().difference(endormie).inMinutes;

    setState(() => _notice = minutes < 1
        ? 'Vous etiez hors de portée. Votre position vient d\'être renvoyée.'
        : 'Vous etiez hors de portée pendant $minutes min : aucune course ne '
            'pouvait vous être proposée. Votre position vient d\'être renvoyée.');
  }

  /// Une position au demarrage, POUR LA CARTE SEULEMENT.
  ///
  /// ELLE NE DEMANDE RIEN. Reclamer l'acces a la position a la seconde ou
  /// l'application s'ouvre, avant que le livreur n'ait touche a quoi que ce
  /// soit, est le meilleur moyen de se faire refuser : il ne voit pas encore
  /// a quoi cela sert. La demande se fait au passage en ligne, ou la raison
  /// est evidente. Ici, on se contente de ce qui est deja accorde.
  Future<void> _positionInitiale() async {
    try {
      final permission = await Geolocator.checkPermission();
      final accordee = permission == LocationPermission.always ||
          permission == LocationPermission.whileInUse;
      if (!accordee || !await Geolocator.isLocationServiceEnabled()) return;

      final position = await Geolocator.getCurrentPosition();
      if (mounted) {
        setState(() => _ici = LatLng(position.latitude, position.longitude));
      }
    } on Object {
      // La carte reste centree sur Cotonou : ce n'est pas une panne.
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _poller?.cancel();
    _heartbeat?.cancel();
    _veille?.cancel();
    super.dispose();
  }

  /// Tirer-pour-rafraichir : la course en cours ET le statut du dossier.
  ///
  /// SANS LE SECOND, un livreur dont ops vient de valider les pieces resterait
  /// bloque jusqu'a ce qu'il pense a redemarrer l'application.
  ///
  /// LE PROFIL EST INVALIDE AUSSI, ET CETTE LIGNE MANQUAIT. Le commentaire
  /// ci-dessus promettait « le statut du dossier » alors que seule la session
  /// etait relue — or c'est le profil qui fait foi depuis la correction du
  /// 28 septembre. La promesse etait tenue par le mauvais objet.
  Future<void> _refreshMission() async {
    ref.invalidate(profilProvider);

    await Future.wait([
      ref.read(sessionProvider.notifier).refresh(),
      _loadMission(),
    ]);
  }

  /// Relit le dossier tant qu'il n'est pas valide.
  ///
  /// UN LIVREUR QUI ATTEND SA VALIDATION N'AVAIT AUCUN MOYEN DE L'APPRENDRE.
  /// Il ne peut pas passer en ligne, donc le sondage d'offres ne tourne pas ;
  /// l'accueil n'a pas de tirer-pour-rafraichir, parce qu'on ne tire pas une
  /// carte vers le bas. Il ne lui restait qu'a tuer l'application et la
  /// relancer — ce qu'aucun ecran ne lui disait.
  ///
  /// TRENTE SECONDES, ET SEULEMENT DANS CET ETAT. La veille s'arrete d'
  /// elle-meme des que le dossier est valide : un livreur qui travaille ne
  /// paie rien pour elle. Un livreur qui attend paie deux requetes par minute,
  /// pendant qu'il ne peut de toute facon rien faire d'autre.
  Future<void> _relireLeDossier() async {
    if (ref.read(profilProvider).valueOrNull?.dossier == EtatDossier.valide) {
      _veille?.cancel();
      _veille = null;
      return;
    }

    ref.invalidate(profilProvider);
    await ref.read(sessionProvider.notifier).refresh();
  }

  /// Reprend l'etat operationnel que le SERVEUR tient pour vrai.
  ///
  /// « _online » ETAIT UN BOOLEEN PUREMENT LOCAL, ET IL MENTAIT A CHAQUE
  /// REDEMARRAGE. Il n'etait ecrit que par la bascule : au lancement de
  /// l'application il valait false, l'accueil affichait « hors ligne », et il
  /// coupait le battement de position comme le sondage d'offres — pendant que
  /// le serveur, lui, tenait toujours le livreur DISPONIBLE. Aucune mise hors
  /// ligne automatique n'existe cote service (point 21).
  ///
  /// DEUX CONSEQUENCES, ET LA SECONDE COUTE UNE COURSE AU CLIENT. L'onglet
  /// Profil affichait « DISPONIBLE » a cote d'une bascule eteinte — il lit le
  /// meme /me, lui. Et pendant les cent vingt secondes ou sa derniere position
  /// reste fraiche, le livreur pouvait recevoir une offre qu'il ne verrait
  /// pas : avec un seul livreur sollicite par vague, c'est trente secondes
  /// pleines perdues pour le client.
  ///
  /// ON NE REMET PAS LE LIVREUR EN LIGNE, ON CONSTATE QU'IL L'EST. Aucun appel
  /// reseau mutant ici : le serveur n'a pas change d'avis, c'est l'ecran qui
  /// rattrape son retard. Le battement reprend donc, et la position repart.
  /// LA SYNCHRONISATION VA DANS LES DEUX SENS DEPUIS LE 30 SEPTEMBRE 2026, et
  /// elle a lieu aussi au retour dans l'application (correction S5).
  ///
  /// Elle ne savait que RATTRAPER UN RETARD : serveur en ligne, ecran hors
  /// ligne. L'inverse n'etait jamais regarde, et il arrive — un livreur
  /// suspendu par le support, une mise hors ligne forcee, une bascule depuis un
  /// autre telephone. L'ecran continuait alors d'afficher du vert, de battre et
  /// de sonder, pour un compte que le serveur ne proposait plus.
  ///
  /// ELLE NE S'EXECUTAIT QU'UNE FOIS, au demarrage. Or l'arriere-plan est l'etat
  /// normal de cette application : le livreur ouvre sa navigation, revient. Tout
  /// ce qui change pendant ce temps-la n'etait jamais constate.
  Future<void> _reprendreLEtatDuServeur() async {
    try {
      // UNE MISE HORS LIGNE EN ATTENTE PASSE AVANT TOUT, sans quoi on
      // remettrait en ligne quelqu'un qui a demande le contraire (voir
      // [_horsLigneAConfirmer]).
      if (_horsLigneAConfirmer) {
        await _confirmerHorsLigne();
        return;
      }

      final profil = await ref.read(profilProvider.future);

      if (!mounted) return;

      final enLigne = profil.operationnel == EtatOperationnel.disponible ||
          profil.operationnel == EtatOperationnel.offreEnCours ||
          profil.operationnel == EtatOperationnel.enMission;

      // LE SENS DESCENDANT : le serveur ne le tient plus pour disponible.
      //
      // SEUL « horsLigne » DECLENCHE, JAMAIS « inconnu ». Un statut que
      // l'application ne sait pas lire est un defaut d'analyse, pas une mise
      // hors ligne : s'en servir pour couper le travail du livreur ferait payer
      // une faute de version a celui qui roule.
      if (_online && profil.operationnel == EtatOperationnel.horsLigne) {
        _poller?.cancel();
        _heartbeat?.cancel();
        _positionAcceptee = null;

        try {
          await ServiceEnLigne.arreter();
        } on Object {
          // Une notification de trop ne justifie pas d'abandonner la reprise.
        }

        if (!mounted) return;

        setState(() {
          _online = false;
          _notice = 'Vous avez été mis hors ligne. Reprenez avec '
              'l\'interrupteur si vous êtes disponible.';
        });

        return;
      }

      if (!enLigne || _online) return;

      setState(() => _online = true);

      _startPolling();
      _startHeartbeat();

      // UN BATTEMENT TOUT DE SUITE, SANS ATTENDRE LA MINUTERIE. Sans lui,
      // l'ecran afficherait « hors de portee » pendant vingt secondes alors que
      // le serveur tient le livreur pour disponible : le contraire du defaut
      // qu'on corrige.
      await _pushPosition();

      // LA NOTIFICATION SUIT L'ETAT. Le livreur est en ligne pour le serveur ;
      // ne pas redemarrer le service de premier plan le laisserait en ligne
      // sans que rien ne le lui dise, et sans position des que l'ecran passe en
      // arriere-plan.
      await ServiceEnLigne.autorisationNotification();
      await ServiceEnLigne.demarrer();
    } on Object {
      // Silencieux, comme la relecture de course : l'accueil ne crie pas parce
      // que le reseau a hoquete au demarrage. La bascule reste utilisable.
    }
  }

  /// Redit au serveur ce qu'il n'a pas pu entendre : ce livreur est hors ligne.
  ///
  /// LE GESTE A DEJA EU LIEU POUR LE LIVREUR, il ne reste qu'a le rendre vrai
  /// pour le reste du systeme. Tant que ce n'est pas fait, sa fiche affiche
  /// « Disponible » dans l'onglet Profil et le prochain demarrage le remettrait
  /// en ligne. On reessaie donc a chaque retour dans l'application, sans rien
  /// dire : il n'y a aucune nouvelle a annoncer a quelqu'un qui se croit — a
  /// juste titre — hors ligne.
  Future<void> _confirmerHorsLigne() async {
    if (!_horsLigneAConfirmer) return;

    try {
      await ref.read(missionRepositoryProvider).goOffline();
      _horsLigneAConfirmer = false;
    } on Object {
      // Toujours pas de reseau : on retentera au prochain retour.
    }
  }

  Future<void> _loadMission() async {
    try {
      final mission = await ref.read(missionRepositoryProvider).currentMission();
      if (mounted) setState(() => _mission = mission);
    } on Object {
      // Silencieux : l'accueil ne doit pas crier parce que le reseau a hoquete.
    }
  }

  Future<void> _toggle(bool value) async {
    setState(() {
      _busy = true;
      _notice = null;
    });

    final repository = ref.read(missionRepositoryProvider);

    try {
      if (value) {
        final (etat, position) = await _position();
        if (position == null) {
          if (mounted) setState(() => _notice = etat.explication);
          return;
        }

        if (mounted) {
          setState(() => _ici = LatLng(position.latitude, position.longitude));
        }

        await repository.goOnline(
          latitude: position.latitude,
          longitude: position.longitude,
        );

        // LE PASSAGE EN LIGNE COMPTE COMME UNE POSITION ACCEPTEE : c'est le
        // meme appel qui porte les coordonnees. Sans cette ligne, l'ecran
        // afficherait « hors de portee » pendant les vingt secondes qui
        // separent la bascule du premier battement.
        _positionAcceptee = DateTime.now();

        // ET IL ANNULE UNE MISE HORS LIGNE RESTEE EN ATTENTE. Sans cette
        // ligne, le drapeau survivait au retour en ligne : la premiere
        // resynchronisation venue aurait envoye le « goOffline » differe, et
        // le serveur aurait mis hors ligne un livreur dont l'ecran affichait
        // du vert et dont le battement tournait. Exactement la panne que ces
        // corrections referment, par une porte qu'elles auraient ouverte.
        _horsLigneAConfirmer = false;

        _startPolling();
        _startHeartbeat();

        // LE SERVICE DEMARRE APRES LE PASSAGE EN LIGNE, PAS AVANT. S'il
        // partait d'abord, un refus du serveur — dossier suspendu, reseau
        // coupe — laisserait une notification « vous etes en ligne » sur un
        // livreur qui ne l'est pas. La notification suit l'etat, elle ne
        // l'annonce pas.
        //
        // L'AUTORISATION EST DEMANDEE ICI ET SON REFUS NE BLOQUE RIEN : le
        // livreur retombe sur le fonctionnement d'avant, en ligne tant que
        // l'ecran reste ouvert. Le faire echouer a travailler pour un reglage
        // systeme serait hors de proportion.
        await ServiceEnLigne.autorisationNotification();
        await ServiceEnLigne.demarrer();
      } else {
        _poller?.cancel();
        _heartbeat?.cancel();
        _positionAcceptee = null;

        // LE SERVICE S'ARRETE AVANT L'APPEL RESEAU. Si « goOffline » echoue —
        // c'est-a-dire precisement quand il n'y a plus de reseau —, le livreur
        // doit au moins ne plus voir une notification qui le dit en ligne.
        //
        // ET SON PROPRE ECHEC NE DOIT RIEN EMPORTER : c'est un appel au
        // systeme, pas au reseau, et une notification recalcitrante ne peut pas
        // avoir pour consequence de laisser l'interrupteur sur EN LIGNE.
        try {
          await ServiceEnLigne.arreter();
        } on Object {
          // Au pire il reste une notification de trop ; le battement, lui, est
          // deja coupe.
        }

        // L'ECHEC DE CET APPEL N'EST PLUS UN ECHEC DE LA BASCULE, et c'est la
        // correction S3.
        //
        // Il remontait au « catch » commun, qui n'atteignait jamais la ligne
        // « _online = value » : l'interrupteur restait sur EN LIGNE alors que
        // les minuteries etaient coupees, le service arrete et la position
        // effacee. Le livreur voyait du vert et rien ne tournait ; le serveur,
        // lui, le proposait encore le temps de la fenetre de fraicheur.
        //
        // COUPER LE BATTEMENT SUFFIT A NE PLUS RECEVOIR DE COURSES : Driver
        // ecarte des recherches toute position perimee. Le livreur EST donc
        // hors ligne dans les faits, a deux minutes pres. On honore son geste,
        // on le lui dit dans ces termes, et on retient de prevenir le serveur.
        try {
          await repository.goOffline();
          _horsLigneAConfirmer = false;
        } on Object {
          _horsLigneAConfirmer = true;

          if (mounted) {
            setState(() => _notice =
                'Hors ligne sur ce téléphone. Le serveur n\'a pas pu être '
                'prévenu : vous pouvez encore recevoir une course pendant '
                '${_fenetreDeFraicheur.inMinutes} min.');
          }
        }
      }

      // MIETTE DE CHEMIN. Les dernieres etapes avant un plantage disent ce que
      // le livreur faisait ; sans elles, une pile d'appels ne dit que l'endroit
      // ou le code a casse, jamais pourquoi on y etait.
      Plantages.trace(value ? 'passe en ligne' : 'passe hors ligne');

      if (mounted) setState(() => _online = value);
    } on OfflineException {
      if (mounted) setState(() => _notice = 'Pas de réseau. Réessayez.');
    } on ApiException catch (error) {
      if (mounted) setState(() => _notice = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _startPolling() {
    _poller?.cancel();
    _poller = Timer.periodic(_pollInterval, (_) => _pollOffer());
  }

  /// Battement de position.
  ///
  /// SANS LUI, UN LIVREUR EN LIGNE DISPARAIT AU BOUT DE DEUX MINUTES. La
  /// position n'etait envoyee qu'au passage en ligne ; Driver ecarte ensuite
  /// des recherches toute position perimee, et le livreur cessait d'exister
  /// pour le dispatch sans qu'aucun ecran ni aucun journal ne le signale.
  ///
  /// UNE MINUTERIE, PAS UN FLUX DE POSITIONS. Le flux de geolocator ne se
  /// declenche qu'au mouvement : un livreur arrete au feu, ou qui attend
  /// devant un restaurant, cesserait d'emettre — c'est-a-dire exactement
  /// quelqu'un de parfaitement disponible qu'on rendrait invisible.
  void _startHeartbeat() {
    _heartbeat?.cancel();
    _heartbeat = Timer.periodic(_positionInterval, (_) => _pushPosition());
  }

  Future<void> _pushPosition() async {
    try {
      final (_, position) = await _position();
      if (position == null) return;

      if (mounted) {
        setState(() => _ici = LatLng(position.latitude, position.longitude));
      }

      await ref.read(missionRepositoryProvider).pushPosition(
            latitude: position.latitude,
            longitude: position.longitude,
            capturedAt: DateTime.now(),
          );

      _positionAcceptee = DateTime.now();

      // LE BATTEMENT EST LA MEILLEURE PREUVE QUE LE RESEAU EST REVENU, et il
      // arrive toutes les vingt secondes sans rien couter de plus. Y accrocher
      // le rejeu de la file evite une minuterie de plus, et fait repartir les
      // actions en attente dans la demi-minute qui suit le retour du reseau.
      unawaited(_viderLaFile());

      // ECRAN ETEINT, LA NOTIFICATION EST LE SEUL ECRAN QUI RESTE. Elle doit
      // donc porter la meme verite que la pastille verte de l'accueil, sans
      // quoi on aurait deplace le mensonge du point 1.1 au lieu de le
      // corriger.
      unawaited(ServiceEnLigne.dire('HBA peut vous proposer des courses.'));
    } on Object {
      // Un battement rate ne se montre pas TOUT DE SUITE : le suivant arrive
      // dans vingt secondes, et il en faut six d'affilee pour sortir des
      // recherches. C'est au bout de ces six-la que l'ecran cesse d'afficher
      // du vert, et il le fait tout seul — « _positionAcceptee » ne bouge pas.
      //
      // LA NOTIFICATION SUIT LA MEME REGLE, PAS UNE AUTRE : elle ne s'alarme
      // qu'une fois la fenetre de fraicheur passee, quand le livreur est
      // reellement sorti des recherches.
      if (!_aPortee) {
        unawaited(
          ServiceEnLigne.dire("Position non transmise — vous ne recevez plus d'offres."),
        );
      }
    } finally {
      // LA RECONSTRUCTION A LIEU DANS TOUS LES CAS, succes comme echec. Sans
      // elle, un battement qui echoue ne declenche aucun setState : l'ecran
      // garderait son point vert indefiniment, precisement dans la situation
      // ou il devrait virer a l'orange.
      if (mounted) setState(() {});
    }
  }

  /// Rejoue ce qui attend, et met a jour le compteur affiche.
  Future<void> _viderLaFile() async {
    final repository = ref.read(missionRepositoryProvider);

    final bilan = await repository.viderLaFile();
    final reste = await repository.actionsEnAttente;

    if (!mounted) return;

    // UN REFUS DU SERVEUR SE DIT AU LIVREUR, ET IL NE SE DISAIT A PERSONNE.
    //
    // La file abandonnait l'entree et la consignait dans le rapport de
    // plantage. L'ecran, lui, avait promis que la remise « sera confirmee au
    // retour de la connexion » : un code mal recopie laissait donc la course en
    // « colis recupere », une tentative brulee sur les cinq, et aucun message.
    //
    // ON N'EN MONTRE QU'UN. Deux refus a la suite sont presque toujours la meme
    // cause, et un bandeau ne se lit que s'il tient en une phrase.
    if (bilan.aDesRefus) {
      setState(() => _notice = bilan.refus.first);
    }

    // LA COURSE EST RELUE QUAND QUELQUE CHOSE A BOUGE. Les etapes avancees
    // en optimiste doivent etre remplacees par ce que le serveur en dit :
    // c'est lui la source de verite, l'avance locale n'etait qu'un
    // depannage. UN REFUS COMPTE AUSSI — c'est meme le cas ou l'ecran est le
    // plus faux, puisqu'il affiche une etape que le serveur n'a pas retenue.
    if (bilan.parties > 0 || bilan.aDesRefus) unawaited(_loadMission());

    if (_enAttente != reste) setState(() => _enAttente = reste);
  }

  /// Relit la course apres une acceptation que le serveur n'a pas encore
  /// rendue lisible.
  ///
  /// POURQUOI CE DECALAGE EXISTE. L'acceptation est ecrite puis publiee ; la
  /// route qui rend la course en cours lit une projection qui peut avoir une
  /// seconde ou deux de retard. Ce n'est pas une panne, c'est le prix d'une
  /// ecriture qui ne bloque pas — mais le livreur, lui, n'a pas a le savoir.
  ///
  /// VINGT SECONDES, PUIS ON LE DIT. Au-dela, quelque chose ne va pas et le
  /// silence deviendrait un mensonge : la phrase finale envoie le livreur vers
  /// l'onglet Courses plutot que de le laisser devant une carte muette.
  Future<void> _attendreLaCourse() async {
    setState(() => _attendLaCourse = true);

    try {
      for (var essai = 0; essai < 10; essai++) {
        await Future<void>.delayed(const Duration(seconds: 2));
        if (!mounted) return;

        try {
          final course =
              await ref.read(missionRepositoryProvider).currentMission();

          if (course != null) {
            if (!mounted) return;
            setState(() {
              _mission = course;
              _notice = null;
            });
            return;
          }
        } on Object {
          // Reseau qui hoquette : l'essai suivant reprendra. On ne remonte
          // rien, le livreur a deja une phrase a l'ecran.
        }
      }

      if (!mounted) return;
      setState(() => _notice =
          'Course acceptée, mais elle n\'apparaît pas encore. '
          'Regardez dans Courses.');
    } finally {
      if (mounted) setState(() => _attendLaCourse = false);
    }
  }

  Future<void> _pollOffer() async {
    // Un livreur ne porte qu'une offre a la fois, et n'en recoit pas pendant
    // une course — NI PENDANT QU'ON ATTEND CELLE QU'IL VIENT D'ACCEPTER.
    if (_sheetOpen || _mission != null || _attendLaCourse) return;

    try {
      final offer = await ref.read(missionRepositoryProvider).currentOffer();
      if (offer == null || !mounted) return;

      // LE VERROU SE RELIT APRES L'ATTENTE, PAS SEULEMENT AVANT.
      //
      // La garde du haut est franchie avant un aller-retour reseau : deux
      // sondages partis en meme temps — la minuterie de cinq secondes et celui
      // du retour au premier plan — la passaient tous les deux, puis ouvraient
      // DEUX feuilles pour la meme offre. La premiere fermee remettait
      // « _sheetOpen » a faux alors que l'autre etait encore a l'ecran.
      if (_sheetOpen || _mission != null || _attendLaCourse) return;

      Plantages.trace('offre recue');

      // LE SIGNAL PART AVANT LA FEUILLE, ET NON APRES. Ouvrir d'abord fait
      // perdre les deux ou trois dixiemes de seconde de l'animation — or ce
      // sont precisement ceux pendant lesquels le livreur leve les yeux.
      unawaited(Signal.offre(avecSon: ref.read(sonDOffreProvider)));

      await _showOffer(offer);
    } on Object {
      // Un sondage rate n'a pas a etre montre : le suivant reessaiera.
    }
  }

  Future<void> _showOffer(Offer offer) async {
    _sheetOpen = true;
    final repository = ref.read(missionRepositoryProvider);

    // DANS UN « TRY », POUR QUE LE VERROU SE RENDE QUOI QU'IL ARRIVE. Il etait
    // remis a faux sur la ligne qui suit l'attente : une exception pendant
    // l'affichage le laissait a vrai POUR TOUJOURS, et le sondage d'offres ne
    // repartait plus de la session. Meme panne que l'offre expiree qui ne se
    // fermait pas, par une autre porte.
    try {
      await showModalBottomSheet<void>(
        context: context,
        // MEME RAISON QUE LA FEUILLE DE RECAPITULATIF : les onglets ont chacun
        // leur Navigator, loge au-dessus de la barre du bas. Sans ceci la
        // feuille se dessine sous la barre et son bas devient inatteignable.
        useRootNavigator: true,
        isScrollControlled: true,
        isDismissible: false,
        enableDrag: false,
        // LA FEUILLE EST DE LA COULEUR DU FOND, pas blanche : les blocs
        // qu'elle contient sont en relief, et un relief ne se lit que
        // s'il sort de la meme matiere que le reste.
        backgroundColor: HbaColors.background,
        shape: const RoundedRectangleBorder(
          borderRadius: BorderRadius.vertical(top: Radius.circular(HbaRadius.card)),
        ),
        builder: (sheetContext) => OfferSheet(
          offer: offer,
          position: _ici,
          onAccept: () async {
            Plantages.trace('offre acceptee');

            // LA FEUILLE SE FIGEAIT QUAND LE RESEAU TOMBAIT, ET IL N'Y AVAIT PAS
            // DE SORTIE.
            //
            // Cet appel partait sans aucun rattrapage : ni OfflineException, ni
            // ApiException. Les deux gestes de la feuille sont des VoidCallback,
            // donc le Future etait abandonne et l'exception partait dans la zone
            // d'erreur de Flutter. Or la feuille est ouverte en
            // « isDismissible: false, enableDrag: false » et ne porte aucun
            // bouton de fermeture : le livreur glissait pour accepter, rien ne
            // se passait, aucun message, et il ne lui restait que le bouton
            // retour d'Android. Pendant ce temps le sondage etait suspendu.
            //
            // L'ACCEPTATION NE SE MET PAS EN FILE, ET C'EST DELIBERE — le depot
            // le dit : une course acceptee dix minutes plus tard a ete prise par
            // quelqu'un d'autre. On ferme donc, et on le dit.
            Acceptation resultat;

            try {
              resultat = await repository.acceptOffer(offer.id);
            } on Object catch (erreur) {
              if (sheetContext.mounted) Navigator.of(sheetContext).pop();
              if (!mounted) return;

              setState(() => _notice = erreur is OfflineException
                  ? "Pas de réseau : la course n'a pas pu être acceptée."
                  : "La course n'a pas pu être acceptée. Réessayez.");
              return;
            }

            if (!sheetContext.mounted) return;
            Navigator.of(sheetContext).pop();
            if (!mounted) return;

            if (resultat.perdue) {
              // Un autre livreur a ete plus rapide. Fonctionnement normal d'une
              // vague, annonce sans alarme.
              setState(() => _notice = 'Course prise par un autre livreur.');
              return;
            }

            setState(() {
              _mission = resultat.mission;

              // LA COURSE EST GAGNEE MEME QUAND ELLE N'EST PAS ENCORE LISIBLE.
              // On le dit tel quel plutot que de laisser un ecran vide, qui se
              // lirait comme un echec.
              _notice = resultat.mission == null
                  ? "Course acceptée. L'écran s'ouvre dès que le serveur l'a "
                      'enregistree.'
                  : null;
            });

            // LE MESSAGE NE SUFFISAIT PAS, ET C'EST LE DEFAUT QU'ON CORRIGE.
            // Jusqu'ici l'acceptation sans course lisible laissait le livreur
            // sur la carte avec une phrase, et RIEN n'allait rechercher la
            // course : elle n'apparaissait qu'au prochain retour dans
            // l'application. Pire, le sondage continuait pendant ce temps et
            // pouvait lui proposer une SECONDE offre alors qu'il en avait deja
            // une sur les bras.
            if (resultat.mission == null) unawaited(_attendreLaCourse());
          },
          onDecline: () async {
            // MEME RAISON QU'AU-DESSUS, ET LE REFUS EST PIRE : un livreur qui
            // refuse veut se debarrasser de la feuille. Elle se ferme donc dans
            // tous les cas — le serveur finira par expirer l'offre tout seul au
            // bout de trente secondes.
            try {
              await repository.declineOffer(offer.id);
            } on Object {
              // Sans consequence : l'offre expire d'elle-meme.
            }

            if (sheetContext.mounted) Navigator.of(sheetContext).pop();
          },
        ),
      );
    } finally {
      _sheetOpen = false;
    }
  }

  /// Demande la position, et dit pourquoi quand elle n'arrive pas.
  ///
  /// UN SEUL « null » NE SUFFISAIT PAS. « Activez la localisation » ne sert a
  /// rien a un livreur qui a deja active le GPS mais refuse l'autorisation,
  /// et encore moins a celui qui l'a refusee definitivement : lui doit passer
  /// par les reglages du telephone. Chaque cas a sa phrase.
  ///
  /// ET SURTOUT, PLUS RIEN NE S'ECHAPPE D'ICI. checkPermission LEVE quand le
  /// manifeste ne declare aucune permission de position ; l'exception
  /// remontait jusqu'a la console et le bouton restait muet. Une couche qui
  /// interroge le systeme rend un resultat, elle ne fait pas tomber l'ecran.
  Future<(_Localisation, Position?)> _position() async {
    try {
      if (!await Geolocator.isLocationServiceEnabled()) {
        return (_Localisation.serviceEteint, null);
      }

      var permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied) {
        permission = await Geolocator.requestPermission();
      }

      if (permission == LocationPermission.deniedForever) {
        return (_Localisation.refuseeDefinitivement, null);
      }

      if (permission == LocationPermission.denied) {
        return (_Localisation.refusee, null);
      }

      return (_Localisation.obtenue, await Geolocator.getCurrentPosition());
    } on Object {
      return (_Localisation.impossible, null);
    }
  }

  @override
  Widget build(BuildContext context) {
    final session = ref.watch(sessionProvider);

    // DEUX COPIES DU MEME FAIT, ET C'EST CE QUI A MENTI A L'ECRAN.
    //
    // « /me » rend l'etat du dossier une seule fois, mais l'application en
    // gardait DEUX exemplaires : « kycApproved » dans l'etat de session, et
    // « dossier » dans le profil. Les deux viennent de la meme route et n'ont
    // pas la meme duree de vie — la session ne se relit qu'au demarrage et au
    // retour au premier plan, le profil se relit a chaque tirer-pour-
    // rafraichir de l'ecran Profil. Un livreur valide pendant que
    // l'application etait ouverte voyait donc « DOSSIER VALIDE » dans Profil
    // et « DOSSIER EN COURS » sur l'accueil, en meme temps.
    //
    // LE PROFIL FAIT FOI, LA SESSION SERT DE REPLI. Le profil est celui des
    // deux qui sait se relire ; la session garde son role quand « /me » n'a
    // jamais repondu — premier demarrage hors ligne, profil pas encore ne —,
    // cas ou elle porte le nom saisi a l'inscription et rien d'autre.
    final profil = ref.watch(profilProvider).valueOrNull;

    final approved = profil != null
        ? profil.dossier == EtatDossier.valide
        : session is SessionSignedIn && session.kycApproved;

    final name = profil != null && profil.displayName.isNotEmpty
        ? profil.displayName
        : session is SessionSignedIn
            ? session.displayName
            : '';

    final mission = _mission;

    // La carte occupe tout le corps ; le reste flotte par-dessus. Google
    // Maps recoit la hauteur du panneau pour decaler sa mention legale, que
    // ses conditions d'utilisation interdisent de masquer.
    //
    // ABAISSE DE 210 A 160 : la bascule en ligne / hors ligne a quitte cette
    // pile pour flotter librement, et les quelque cinquante pixels qu'elle
    // occupait n'ont plus a etre reserves. La mention Google remonte donc
    // moins haut sur la carte.
    //
    // CETTE MEME VALEUR BORNE LE DEPLACEMENT DE LA BASCULE, plus bas : la
    // bande du bas appartient aux cartes empilees et a la mention legale, et
    // rien de deplacable ne doit pouvoir y descendre. Un seul nombre pour les
    // deux, sinon l'un des deux derivera.
    const hauteurPanneau = 160.0;

    return Scaffold(
      body: Stack(
        children: [
          Positioned.fill(
            child: CarteLivreur(
              position: _ici,
              mission: mission,

              // L'EPINGLE DIT LE FAIT, PAS L'INTENTION, comme la bascule :
              // deux signaux du meme etat qui ne diraient pas la meme chose
              // seraient pires qu'un seul.
              enLigne: _online && _aPortee,
              margeBasse: hauteurPanneau,

              // LE VEHICULE VIENT DU PROFIL, ET SON ABSENCE N'EST PAS UNE
              // PANNE. « valueOrNull » plutot que « when » : si /me n'a pas
              // encore repondu — ou echoue —, l'epingle reste un disque nu et
              // la carte fonctionne. Un pictogramme n'a jamais valu de casser
              // un ecran.
              vehicule: ref.watch(profilProvider).valueOrNull?.vehicule?.type,
            ),
          ),
          SafeArea(
            child: Column(
              children: [
                _Salutation(nom: name),
                const Spacer(),
                Padding(
                  padding: const EdgeInsets.fromLTRB(
                    HbaSpacing.gutter,
                    0,
                    HbaSpacing.gutter,
                    HbaSpacing.md,
                  ),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      // CE QUI ATTEND DE PARTIR SE VOIT. Une file silencieuse
                      // n'est pas mieux qu'une action perdue : le livreur qui
                      // a confirme une remise dans un sous-sol doit savoir
                      // qu'elle n'est pas encore arrivee, sinon il conclura
                      // qu'elle l'est.
                      if (_enAttente > 0) ...[
                        _Flottant(
                          child: Row(
                            children: [
                              const Icon(
                                Icons.cloud_upload_outlined,
                                size: 20,
                                color: HbaColors.warning,
                              ),
                              const SizedBox(width: HbaSpacing.sm),
                              Expanded(
                                child: Text(
                                  _enAttente == 1
                                      ? 'Une action attend le réseau. Elle partira toute seule.'
                                      : '$_enAttente actions attendent le réseau. Elles partiront toutes seules.',
                                  style: Theme.of(context).textTheme.bodySmall,
                                ),
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(height: HbaSpacing.sm),
                      ],

                      if (_notice != null) ...[
                        _Flottant(
                          child: Text(
                            _notice!,
                            style: Theme.of(context).textTheme.bodyMedium,
                          ),
                        ),
                        const SizedBox(height: HbaSpacing.sm),
                      ],
                      if (!approved) ...[
                        const _KycPending(),
                        const SizedBox(height: HbaSpacing.sm),
                      ],
                      if (mission != null) ...[
                        _CarteMission(
                          mission: mission,
                          onOuvrir: _ouvrirMission,
                        ),
                        const SizedBox(height: HbaSpacing.sm),
                      ],
                    ],
                  ),
                ),
              ],
            ),
          ),

          // LA BASCULE EST LA DERNIERE DU TAS, donc au-dessus de tout. Elle est
          // petite et le livreur la place ou il veut ; la mettre dessous
          // reviendrait a la rendre insaisissable des qu'une carte de course
          // s'affiche au meme endroit.
          Positioned.fill(
            child: SafeArea(
              child: BasculeFlottante(
                enLigne: _online,
                aPortee: _aPortee,
                occupe: _busy,
                actif: approved,
                onChanged: _toggle,
                margeHaute: 56,
                margeBasse: hauteurPanneau,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Future<void> _ouvrirMission(Mission mission) async {
    await Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => MissionScreen(
          mission: mission,
          onClosed: () => setState(() => _mission = null),
        ),
      ),
    );
    await _refreshMission();
  }
}

/// Le nom, pose sur la carte.
///
/// DANS UNE PASTILLE, PAS EN TEXTE NU : sur une carte, un titre sombre passe
/// sur une autoroute jaune ou un parc vert et devient illisible.
class _Salutation extends StatelessWidget {
  const _Salutation({required this.nom});

  final String nom;

  @override
  Widget build(BuildContext context) => Align(
        alignment: Alignment.centerLeft,
        child: Padding(
          padding: const EdgeInsets.all(HbaSpacing.md),
          child: _Flottant(
            padding: const EdgeInsets.symmetric(
              horizontal: HbaSpacing.md,
              vertical: HbaSpacing.sm,
            ),
            rayon: HbaRadius.chip,
            child: Text(
              nom.isEmpty ? 'Bonjour' : 'Bonjour $nom',
              style: const TextStyle(
                fontWeight: FontWeight.w700,
                color: HbaColors.ink,
              ),
            ),
          ),
        ),
      );
}

/// Carte flottante posee SUR LA CARTE GOOGLE.
///
/// LA SEULE DU SYSTEME QUI GARDE UNE OMBRE PORTEE CLASSIQUE, et c'est la
/// limite du neumorphisme : son relief tient a une lumiere claire en haut a
/// gauche, qui suppose de connaitre la couleur de ce qu'il y a dessous. Sur
/// une photo aerienne — des toits, de la vegetation, une route — cette lumiere
/// devient une trainee blanche sale. Une ombre franche, elle, marche sur
/// n'importe quel fond.
///
/// LA REGLE GENERALE EST DONC : le relief sur la matiere du systeme, l'ombre
/// portee des qu'on flotte au-dessus d'autre chose.
class _Flottant extends StatelessWidget {
  const _Flottant({
    required this.child,
    this.padding = const EdgeInsets.all(HbaSpacing.md),
    this.rayon = HbaRadius.card,
    this.onTap,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final double rayon;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final contenu = Padding(padding: padding, child: child);

    return DecoratedBox(
      decoration: BoxDecoration(
        color: HbaColors.surface,
        borderRadius: BorderRadius.circular(rayon),
        boxShadow: const [
          BoxShadow(
            color: Color(0x1F141B2D),
            blurRadius: 18,
            offset: Offset(0, 6),
          ),
        ],
      ),
      child: onTap == null
          ? contenu
          : Material(
              color: Colors.transparent,
              child: InkWell(
                onTap: onTap,
                borderRadius: BorderRadius.circular(rayon),
                child: contenu,
              ),
            ),
    );
  }
}

/// La bascule en ligne / hors ligne, posee sur la carte.
/// La course en cours, posee sur la carte.
class _CarteMission extends StatelessWidget {
  const _CarteMission({required this.mission, required this.onOuvrir});

  final Mission mission;
  final Future<void> Function(Mission) onOuvrir;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return _Flottant(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      onTap: () => onOuvrir(mission),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const HbaChip(label: 'COURSE EN COURS', tone: HbaChipTone.primary),
                const SizedBox(height: HbaSpacing.sm),
                Text(mission.reference, style: theme.textTheme.titleMedium),
                const SizedBox(height: 2),
                Text(
                  mission.status.nextDriverAction ?? 'Course en cours',
                  style: theme.textTheme.bodyMedium,
                ),
              ],
            ),
          ),
          const Icon(Icons.chevron_right, color: HbaColors.inkFaint),
        ],
      ),
    );
  }
}

/// Inscrit, mais pas encore autorise a travailler.
class _KycPending extends StatelessWidget {
  const _KycPending();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    // LA CARTE MENE AU DOSSIER. Dire au livreur que ses pieces doivent etre
    // verifiees sans lui donner le chemin pour les deposer le laisse
    // attendre quelque chose qui n'arrivera jamais.
    return _Flottant(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      onTap: () => context.push('/profil/dossier'),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const HbaChip(label: 'DOSSIER EN COURS', tone: HbaChipTone.warning),
                const SizedBox(height: HbaSpacing.sm),
                Text('Complétez votre dossier', style: theme.textTheme.titleMedium),
                const SizedBox(height: HbaSpacing.xs),
                Text(
                  'Vous pourrez passer en ligne dès que vos pièces auront été '
                  'vérifiées.',
                  style: theme.textTheme.bodyMedium,
                ),
              ],
            ),
          ),
          const Icon(Icons.chevron_right, color: HbaColors.inkFaint),
        ],
      ),
    );
  }
}

/// Pourquoi la position n'est pas arrivee.
///
/// CHAQUE CAS A SON GESTE. Melanger « le GPS est eteint » et « vous avez
/// refuse l'acces » donne un message que le livreur ne peut pas suivre : il
/// verifie le mauvais reglage et conclut que l'application est cassee.
enum _Localisation {
  obtenue,
  serviceEteint,
  refusee,
  refuseeDefinitivement,
  impossible;

  String get explication => switch (this) {
        obtenue => '',
        serviceEteint =>
          'Activez la localisation du téléphone pour passer en ligne : le '
              'dispatch cherche les livreurs les plus proches.',
        refusee =>
          'HBA Livreur a besoin de votre position pour recevoir des courses. '
              'Réessayez et acceptez la demande.',
        refuseeDefinitivement =>
          'L\'accès à la position est bloqué. Ouvrez les réglages du '
              'téléphone, autorisez la localisation pour HBA Livreur, puis '
              'reessayez.',
        impossible =>
          'Position introuvable. Vérifiez que vous n\'êtes pas en mode avion, '
              'puis réessayez.',
      };
}
