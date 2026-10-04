import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import '../../core/paiement_en_attente.dart';
import '../../core/reprise_de_paiement.dart';
import '../commande/elements.dart';
import '../commande/feuille_modale.dart';
import '../deliveries/delivery_providers.dart';
import '../deliveries/map_picker_screen.dart';
import '../deliveries/models.dart';
import '../deliveries/paiement_webview.dart';
import '../deliveries/point_field.dart';
import 'course_models.dart';
import 'course_providers.dart';

/// Commander une course de personnes.
///
/// DEUX POINTS, UN VEHICULE, UN PRIX. C'est tout ce qu'une course demande, et
/// c'est la difference avec l'envoi d'un colis : pas de destinataire, pas de
/// repere, pas de contenu ni de poids. Le passager EST le destinataire, et il
/// est la pour monter.
///
/// LE DEVIS PASSE PAR LA ROUTE DES COLIS, avec « serviceType: course ». Le prix
/// a un seul chemin dans ce depot, et c'est voulu. Le champ n'est PAS facultatif
/// ici : depuis le 1er octobre 2026 la nature fait partie de la cle de la
/// grille, et sans elle Pricing rendrait un tarif de colis que la creation
/// refuserait par QUOTE_SERVICE_MISMATCH.
class NouvelleCourseScreen extends ConsumerStatefulWidget {
  const NouvelleCourseScreen({this.destination, super.key});

  /// Destination deja choisie, quand l'ecran est ouvert depuis un lieu
  /// enregistre de l'accueil.
  ///
  /// ELLE NE REMPLIT QU'UN CHAMP, ET NE DECIDE DE RIEN D'AUTRE. Le depart
  /// reste a choisir, le devis reste a demander : une adresse connue fait
  /// gagner un geste, pas une etape.
  final PickedPoint? destination;

  @override
  ConsumerState<NouvelleCourseScreen> createState() => _NouvelleCourseScreenState();
}

class _NouvelleCourseScreenState extends ConsumerState<NouvelleCourseScreen> {
  PickedPoint? _depart;
  PickedPoint? _destination;
  VehiculeDeCourse _vehicule = VehiculeDeCourse.moto;

  /// Un devis PAR VEHICULE, parce que l'ecran montre les trois prix.
  ///
  /// TROIS DEVIS POUR UNE COURSE, ET UN SEUL SERA CONSOMME. Les deux autres
  /// expirent d'eux-memes au bout du quart d'heure : Pricing les garde, nul ne
  /// les reclame, rien ne les transforme en course. C'est le prix a payer pour
  /// que le passager COMPARE avant de choisir, au lieu de decouvrir le tarif du
  /// tricycle en l'ayant deja selectionne.
  ///
  /// LA CARTE EST VIDE TANT QU'ON N'A PAS DEMANDE, et chaque entree peut
  /// manquer : si Pricing refuse un vehicule — une grille absente, une zone non
  /// couverte —, sa ligne reste sans prix au lieu de faire echouer les deux
  /// autres.
  final Map<VehiculeDeCourse, Quote> _devis = {};

  bool _envoi = false;
  String? _erreur;

  /// La cle de la tentative en cours.
  ///
  /// ELLE NE CHANGE QUE QUAND LE CONTENU CHANGE. Tant que le passager n'a rien
  /// modifie, reprendre le paiement doit rendre LA MEME course et LA MEME
  /// adresse — pas une seconde course a cote de la premiere.
  String _cle = ApiClient.newIdempotencyKey();
  bool _courseEnvoyee = false;

  @override
  void initState() {
    super.initState();

    // LA DESTINATION ARRIVE PAR LE CONSTRUCTEUR, ET ON LA COPIE UNE FOIS. La
    // lire a chaque rendu empecherait le passager de la changer : il la
    // choisirait sur la carte, et l'ecran la remettrait a celle du raccourci.
    _destination = widget.destination;
  }

  void _contenuModifie() {
    if (!_courseEnvoyee) return;

    _cle = ApiClient.newIdempotencyKey();
    _courseEnvoyee = false;
  }

  /// Les devis ne valent plus : le trajet a change.
  ///
  /// LES TROIS TOMBENT ENSEMBLE, parce qu'ils portent tous le meme trajet. Un
  /// seul point deplace, et aucun des trois prix ne decrit plus la course.
  void _annulerLesDevis() {
    _contenuModifie();
    if (_devis.isNotEmpty) setState(_devis.clear);
  }

  /// Demande les trois prix d'un coup.
  ///
  /// EN PARALLELE, PAS L'UN APRES L'AUTRE. Trois allers-retours en file
  /// prendraient trois fois le temps du plus lent, pour un ecran qui ne montre
  /// rien tant que le dernier n'est pas rentre. « Future.wait » les lance
  /// ensemble : l'attente est celle d'une seule requete.
  ///
  /// UN VEHICULE QUI ECHOUE N'EMPORTE PAS LES AUTRES. Chaque demande rattrape
  /// sa propre panne et rend null ; la ligne reste alors sans prix, et le
  /// passager garde le choix entre les deux qui ont repondu. Sans cela, une
  /// grille manquante pour le tricycle rendrait la course impossible a
  /// commander en moto.
  Future<void> _demanderLesPrix() async {
    final depart = _depart;
    final destination = _destination;

    if (depart == null || destination == null) {
      setState(() => _erreur = 'Choisissez le départ et la destination.');
      return;
    }

    setState(() {
      _envoi = true;
      _erreur = null;
      _devis.clear();
    });

    // CE QUE LE SERVEUR A REPONDU QUAND IL A REFUSE.
    //
    // ON LE GARDE PARCE QUE « AUCUN PRIX POUR CE TRAJET » SERAIT UN MENSONGE
    // QUAND LE SERVICE EST A TERRE. Les deux cas se ressemblent vus d'ici — la
    // carte des devis reste vide — mais ils ne se corrigent pas pareil : une
    // zone non tarifee demande de changer de trajet, un service injoignable
    // demande d'attendre. La passerelle fait deja cette distinction dans ses
    // messages ; les jeter pour en ecrire un seul l'effacerait.
    ApiException? refus;

    Future<(VehiculeDeCourse, Quote)?> pour(VehiculeDeCourse vehicule) async {
      try {
        final devis = await ref.read(deliveryRepositoryProvider).quote(
              pickupLatitude: depart.latitude,
              pickupLongitude: depart.longitude,
              dropoffLatitude: destination.latitude,
              dropoffLongitude: destination.longitude,

              // UNE COURSE NE PESE RIEN, ET LA GRILLE DES COURSES N'A PAS DE
              // SUPPLEMENT AU KILO. Zero est la valeur juste : envoyer un poids
              // de colis par habitude ferait payer un passager au gramme.
              packageWeightGrams: 0,
              vehicleType: vehicule.code,
              serviceType: 'course',
            );

        // LE DEVIS RENDU DOIT ETRE UN DEVIS DE COURSE. S'il revient en
        // livraison, c'est que la nature n'a pas ete prise en compte : creer la
        // course echouerait sur QUOTE_SERVICE_MISMATCH, que le passager ne peut
        // pas interpreter. On jette la ligne plutot que d'afficher un tarif de
        // colis sous un dessin de moto.
        return devis.estUneLivraison ? null : (vehicule, devis);
      } on OfflineException {
        rethrow;
      } on ApiException catch (erreur) {
        // LE PREMIER REFUS, PAS LE DERNIER. Les trois requetes partent
        // ensemble et reviennent dans le desordre ; garder le dernier arrive
        // ferait changer le message d'une tentative a l'autre sans que rien
        // n'ait change.
        refus ??= erreur;
        return null;
      }
    }

    try {
      final rendus =
          await Future.wait(VehiculeDeCourse.values.map(pour));
      if (!mounted) return;

      setState(() {
        for (final rendu in rendus) {
          if (rendu != null) _devis[rendu.$1] = rendu.$2;
        }

        if (_devis.isEmpty) {
          _erreur = refus?.message ??
              "Aucun prix n'est disponible pour ce trajet.";
          return;
        }

        // LE CHOIX SE POSE SUR UN VEHICULE QUI A UN PRIX. Le laisser sur une
        // ligne refusee donnerait un bouton « Commander » sans montant, qui
        // echouerait a la creation.
        if (!_devis.containsKey(_vehicule)) _vehicule = _devis.keys.first;
      });
    } on OfflineException {
      if (mounted) setState(() => _erreur = 'Pas de réseau. Réessayez.');
    } finally {
      if (mounted) setState(() => _envoi = false);
    }
  }

  Future<void> _commander() async {
    // LE DEVIS DU VEHICULE CHOISI, ET LUI SEUL. Les deux autres restent en
    // memoire sans etre envoyes : ils ne seront jamais consommes, et c'est
    // exactement ce qu'on veut.
    final devis = _devis[_vehicule];
    final depart = _depart;
    final destination = _destination;

    if (devis == null || depart == null || destination == null) return;

    // UN DEVIS EXPIRE NE SE CONSOMME PLUS. Mieux vaut en redemander un que
    // d'envoyer au service une creation qu'il refusera.
    //
    // LES TROIS TOMBENT ENSEMBLE : ils ont ete etablis a la meme seconde, donc
    // ils expirent a la meme seconde. En garder deux donnerait au passager le
    // choix entre deux prix deja perimes.
    if (devis.hasExpiredAt(DateTime.now())) {
      setState(() {
        _devis.clear();
        _erreur = 'Le prix a expiré. Demandez-en un nouveau.';
      });
      return;
    }

    setState(() {
      _envoi = true;
      _erreur = null;
    });

    // LE CORPS EST COMPOSE UNE FOIS, PUIS ENVOYE **ET** GARDE. C'est ce qui rend
    // la reprise possible apres un redemarrage : rejouer la meme cle avec le meme
    // corps rend la MEME course et la MEME adresse de paiement. Le recomposer plus
    // tard depuis les champs de l'ecran ne marcherait pas — l'ecran n'existe plus.
    final corps = <String, Object?>{
      'quoteId': devis.id,
      'pickupLatitude': depart.latitude,
      'pickupLongitude': depart.longitude,
      'destinationLatitude': destination.latitude,
      'destinationLongitude': destination.longitude,
      'vehicleType': _vehicule.code,

    };

    try {
      final creee = await ref
          .read(courseRepositoryProvider)
          .envoyer(idempotencyKey: _cle, corps: corps);

      // LA COURSE EXISTE MAINTENANT SOUS CETTE CLE. Toute modification ulterieure
      // doit en tirer une nouvelle, sans quoi le service rendrait celle-ci.
      _courseEnvoyee = true;

      // GARDE AVANT D'OUVRIR LA PAGE DE PAIEMENT, ET L'ORDRE EST TOUT L'INTERET :
      // c'est pendant cette page que le systeme peut tuer l'application, et le cas
      // a couvrir est justement celui ou l'on ne revient pas.
      await ref.read(paiementsEnAttenteProvider).poser(CommandeAPayer(
            nature: 'course',
            id: creee.course.id,
            cle: _cle,
            corps: corps,
            envoyeeLe: DateTime.now().toUtc(),
          ));

      ref
        ..invalidate(coursesActivesProvider)
        ..invalidate(commandesAPayerProvider);
      if (!mounted) return;

      // AUCUNE ADRESSE VEUT DIRE QU'IL N'Y A RIEN A OUVRIR. Le prix est encaisse
      // AVANT la recherche d'un chauffeur (point 35) : le dire vaut mieux
      // qu'envoyer le passager attendre devant un suivi qui n'avancera jamais.
      if (!creee.paiementOuvrable) {
        setState(() => _erreur =
            "Le paiement n'a pas pu s'ouvrir. La course sera abandonnée dans "
            '$delaiDePaiementMinutes minutes : reprenez-la pour la lancer.');
        return;
      }

      // SORTIR DE CETTE PAGE NE PROUVE AUCUN PAIEMENT, ni encaisse ni refuse :
      // seul le webhook signe fait foi (ADR 0017). On n'en lit donc pas l'issue
      // pour decider quoi que ce soit — on enchaine sur le suivi, qui lira
      // l'etat reel aupres du service.
      await Navigator.of(context).push<IssuePaiement>(
        MaterialPageRoute(
          builder: (_) => PaiementWebView(url: creee.adresseDePaiement!),
        ),
      );

      if (!mounted) return;

      // MEME MENAGE QUE POUR UN COLIS : on demande au service si la course
      // attend encore d'etre payee, et on oublie l'entree locale sinon.
      unawaited(oublierSiReglee(ref, estUneCourse: true, id: creee.course.id));

      // ON CONFIE L'IDENTIFIANT AVANT DE RENDRE L'ACCUEIL : une course impayee
      // est hors du perimetre du passager dans les listes, et c'est ce marqueur
      // qui permet a l'accueil de la retrouver quand meme.
      ref.read(courseEnAttenteDePaiementProvider.notifier).state = creee.course.id;

      // L'ACCUEIL, PAS LE SUIVI, DEPUIS LE 3 OCTOBRE 2026. Le suivi annonçait
      // « Recherche d'un chauffeur » sur un ecran vide : le passager quittait
      // l'accueil pour voir MOINS de choses qu'il n'en avait. L'accueil montre
      // desormais le radar — la carte, le halo, le compteur — comme il le fait
      // depuis toujours pour un colis, et il ouvrira le suivi tout seul des
      // qu'un chauffeur aura accepte.
      context.go('/');
    } on OfflineException {
      if (mounted) setState(() => _erreur = 'Pas de réseau. Réessayez.');
    } on ApiException catch (erreur) {
      if (!mounted) return;

      setState(() {
        _erreur = erreur.message;

        // UN DEVIS REFUSE NE RESSERVIRA JAMAIS — expire, deja consomme, ou
        // etabli pour un autre trajet. Le laisser a l'ecran envoie le passager
        // se cogner au meme mur, et ses deux voisins sont du meme age.
        _devis.clear();
      });
    } finally {
      if (mounted) setState(() => _envoi = false);
    }
  }

  /// Ce que le bandeau du titre annonce : la distance et la duree du trajet.
  ///
  /// PRIS SUR N'IMPORTE QUEL DEVIS, parce que les trois portent le MEME
  /// trajet : seule la grille change d'un vehicule a l'autre, pas la mesure.
  ///
  /// LA DUREE EST CELLE DU TRAJET, PAS CELLE DE L'ATTENTE. Les maquettes
  /// annoncaient aussi « a 2 min » sur chaque vehicule — le delai d'arrivee du
  /// chauffeur. Il n'est ecrit nulle part : aucun chauffeur n'est encore
  /// cherche a ce stade, et le dire serait une promesse que le service ne peut
  /// pas tenir. Voir points-a-trancher.md.
  String? get _mesureDuTrajet {
    if (_devis.isEmpty) return null;
    final devis = _devis.values.first;

    final km = (devis.distanceMeters / 1000).toStringAsFixed(1).replaceAll('.', ',');
    final minutes = (devis.durationSeconds / 60).round();

    return '$km km · $minutes min';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final depart = _depart;
    final destination = _destination;
    final devisChoisi = _devis[_vehicule];
    final mesure = _mesureDuTrajet;

    return FeuilleModale(
      // LA FEUILLE NE SE FERME PAS PENDANT L'ENVOI. La detruire a cet
      // instant emporterait l'ecran qui attend la reponse, et avec lui
      // l'adresse de paiement qu'il allait ouvrir.
      retourPossible: !_envoi,

      // LE PIED EST HORS DU DEFILEMENT. Le prix et le bouton restent sous les
      // yeux pendant que le passager compare les vehicules : on ne commande
      // jamais sans voir ce qu'on paie.
      pied: PiedDeCommande(
        montant: devisChoisi == null ? null : Xof.format(devisChoisi.totalXof),
        detail: devisChoisi == null ? null : 'Prix ferme · ${_vehicule.libelle}',
        action: _devis.isEmpty
            ? HbaButton(
                label: 'Voir les prix',
                busy: _envoi,
                onPressed: _demanderLesPrix,
              )
            : HbaButton(
                label: 'Commander',
                busy: _envoi,
                onPressed: devisChoisi == null ? null : _commander,
              ),
      ),
      enfant: ListView(
        shrinkWrap: true,
        padding: const EdgeInsets.fromLTRB(
          HbaSpacing.gutter,
          HbaSpacing.sm,
          HbaSpacing.gutter,
          HbaSpacing.md,
        ),
        children: [
          CarteDuTrajet(
            depart: PointDuTrajet(
              intitule: 'Départ',
              valeur: depart?.pretty ?? 'Où vous êtes',
              renseigne: depart != null,
              onTap: () => _choisir(
                titre: 'Départ',
                actuel: depart,
                pose: (point) => _depart = point,
              ),
            ),
            arrivee: PointDuTrajet(
              intitule: 'Destination',
              valeur: destination?.pretty ?? 'Où vous allez',
              renseigne: destination != null,
              onTap: () => _choisir(
                titre: 'Destination',
                actuel: destination,
                pose: (point) => _destination = point,
              ),
            ),
          ),
          const SizedBox(height: HbaSpacing.lg),
          Row(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Expanded(
                child: Text(
                  'Choisissez votre trajet',
                  style: theme.textTheme.titleLarge,
                ),
              ),
              if (mesure != null)
                Text(
                  mesure,
                  style: theme.textTheme.bodyMedium
                      ?.copyWith(color: HbaColors.inkMuted),
                ),
            ],
          ),
          const SizedBox(height: HbaSpacing.md),

          // TROIS VEHICULES, PAS CINQ. Ni la camionnette ni le velo ne
          // transportent de personnes, et le contrat de Ride ne les connait
          // pas : les proposer serait promettre un refus.
          //
          // DES CARTES, PLUS UNE LISTE DEROULANTE. Un menu cache deux
          // options sur trois et ne peut pas porter de prix ; les maquettes
          // posent les trois a plat, et c'est ce qui permet de comparer.
          for (final vehicule in VehiculeDeCourse.values) ...[
            CarteDeChoix(
              icone: _icone(vehicule),
              titre: vehicule.libelle,
              detail: _devis.isEmpty
                  ? 'Prix à demander'
                  : _devis.containsKey(vehicule)
                      ? 'Prix ferme, réglé avant la recherche'
                      : 'Indisponible pour ce trajet',
              valeur: _devis[vehicule] == null
                  ? null
                  : Xof.format(_devis[vehicule]!.totalXof),
              choisie: _vehicule == vehicule,
              onTap: () {
                // UN VEHICULE SANS PRIX NE SE CHOISIT PAS une fois les devis
                // rentres : le bouton resterait sans montant, et la creation
                // echouerait. Avant la demande, les trois sont choisissables.
                if (_devis.isNotEmpty && !_devis.containsKey(vehicule)) return;
                setState(() => _vehicule = vehicule);
              },
            ),
            const SizedBox(height: HbaSpacing.sm),
          ],
          if (_erreur != null) ...[
            const SizedBox(height: HbaSpacing.sm),
            Text(
              _erreur!,
              style: theme.textTheme.bodyMedium
                  ?.copyWith(color: HbaColors.danger),
            ),
          ],
        ],
      ),
    );
  }

  /// Ouvre la carte et pose le point choisi.
  ///
  /// LE MEME ECRAN QU'AVANT, APPELE AUTREMENT. PointField portait a la fois le
  /// dessin du champ et l'ouverture de la carte ; la carte du trajet reprend le
  /// dessin, donc il ne reste ici que l'ouverture.
  Future<void> _choisir({
    required String titre,
    required PickedPoint? actuel,
    required void Function(PickedPoint) pose,
  }) async {
    final choisi = await Navigator.of(context).push<PickedPoint>(
      MaterialPageRoute(
        builder: (_) => MapPickerScreen(title: titre, initial: actuel),
      ),
    );

    if (choisi == null || !mounted) return;

    setState(() => pose(choisi));
    _annulerLesDevis();
  }

  /// LES MEMES DESSINS QUE LES EPINGLES DE LA CARTE, recopies a la main.
  ///
  /// POURQUOI NE PAS APPELER Epingles.iconeDuVehicule. Cette table-la est
  /// indexee sur les noms du protobuf — « Motorcycle », « Tricycle » — et sur
  /// leurs numeros. VehiculeDeCourse, lui, porte les codes francais que la
  /// passerelle attend : « moto », « voiture », « tricycle ». Lui passer un
  /// code francais rend null, et les trois lignes se retrouveraient avec la
  /// meme icone de secours. Les deux tables disent donc la meme chose dans deux
  /// alphabets, et c'est ce qu'il faudra reunir le jour ou le vocabulaire sera
  /// unifie.
  IconData _icone(VehiculeDeCourse vehicule) => switch (vehicule) {
        VehiculeDeCourse.moto => Icons.two_wheeler,
        VehiculeDeCourse.voiture => Icons.directions_car,
        VehiculeDeCourse.tricycle => Icons.electric_rickshaw,
      };
}
