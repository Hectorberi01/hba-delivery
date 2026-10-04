import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';
import 'package:image_picker/image_picker.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/navigation_externe.dart';
import '../../core/rafraichir.dart';
import '../profil/profil_repository.dart';
import '../profil/reglages_app.dart';
import 'dossier_repository.dart';

/// Constitution du dossier.
///
/// UN SEUL ECRAN, PAS UN ASSISTANT EN QUATRE PAGES. Un livreur depose sa CNI
/// aujourd'hui, son permis demain quand il l'aura retrouve : un parcours
/// lineaire l'obligerait a recommencer depuis le debut a chaque fois. La
/// liste montre ce qui est fait, ce qui manque, et laisse prendre dans
/// l'ordre qu'on veut.
class DossierScreen extends ConsumerStatefulWidget {
  const DossierScreen({super.key});

  @override
  ConsumerState<DossierScreen> createState() => _DossierScreenState();
}

class _DossierScreenState extends ConsumerState<DossierScreen> {
  /// Piece en cours d'envoi, s'il y en a une.
  Piece? _envoiEnCours;
  double _avancement = 0;
  String? _erreur;

  Future<void> _deposer(Piece piece) async {
    // UN SEUL ENVOI A LA FOIS. La tuile se grise pendant l'envoi, mais elle
    // reste active pendant la feuille de choix et l'appareil photo : ces
    // deux-la sont modaux, donc la fenetre est etroite — elle n'est pas
    // nulle. Deux depots simultanes de la meme piece se disputent la meme
    // ligne en base, et le perdant remonte une erreur au livreur pour un
    // geste qu'il n'a fait qu'une fois.
    if (_envoiEnCours != null) return;

    final source = await _choisirSource();
    if (source == null) return;

    final XFile? prise;
    try {
      prise = await ImagePicker().pickImage(
        source: source,
        // LE REDIMENSIONNEMENT SERIEUX EST FAIT APRES, par la compression
        // native. Ces bornes-ci evitent seulement de charger en memoire une
        // photo de 50 megapixels le temps d'arriver la.
        maxWidth: 3000,
        maxHeight: 3000,
      );
    } on Object {
      if (mounted) setState(() => _erreur = "Impossible d'ouvrir l'appareil photo.");
      return;
    }

    if (prise == null) return;

    setState(() {
      _envoiEnCours = piece;
      _avancement = 0;
      _erreur = null;
    });

    try {
      await ref.read(dossierRepositoryProvider).deposer(
            piece: piece,
            fichier: File(prise.path),
            progression: (envoye, total) {
              if (total > 0 && mounted) {
                setState(() => _avancement = envoye / total);
              }
            },
          );

      ref.invalidate(dossierProvider);
    } on ApiException catch (erreur) {
      // LE MESSAGE DU SERVEUR EST AFFICHE TEL QUEL. « Votre piece depasse
      // 5 Mo » est exactement ce que le livreur doit lire ; le remplacer par
      // « une erreur est survenue » lui retirerait le seul indice utile.
      if (mounted) setState(() => _erreur = erreur.message);
    } on OfflineException {
      if (mounted) setState(() => _erreur = 'Pas de réseau. Réessayez plus tard.');
    } finally {
      if (mounted) setState(() => _envoiEnCours = null);
    }
  }

  Future<ImageSource?> _choisirSource() => showModalBottomSheet<ImageSource>(
        context: context,
        // MEME RAISON QUE LA FEUILLE DE RECAPITULATIF : les onglets ont
        // chacun leur Navigator, loge au-dessus de la barre du bas.
        useRootNavigator: true,
        // LA FEUILLE EST DE LA COULEUR DU FOND, pas blanche : les blocs
        // qu'elle contient sont en relief, et un relief ne se lit que
        // s'il sort de la meme matiere que le reste.
        backgroundColor: HbaColors.background,
        shape: const RoundedRectangleBorder(
          borderRadius: BorderRadius.vertical(top: Radius.circular(HbaRadius.card)),
        ),
        builder: (feuille) => SafeArea(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              ListTile(
                leading: const Icon(Icons.photo_camera_outlined),
                title: const Text('Prendre une photo'),
                onTap: () => Navigator.of(feuille).pop(ImageSource.camera),
              ),
              ListTile(
                leading: const Icon(Icons.photo_library_outlined),
                title: const Text('Choisir dans la galerie'),
                onTap: () => Navigator.of(feuille).pop(ImageSource.gallery),
              ),
              const SizedBox(height: HbaSpacing.sm),
            ],
          ),
        ),
      );

  Future<void> _soumettre() async {
    setState(() => _erreur = null);

    try {
      await ref.read(dossierRepositoryProvider).soumettre();
      ref.invalidate(dossierProvider);

      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Dossier envoye. HBA vous repondra.')),
        );
      }
    } on ApiException catch (erreur) {
      if (mounted) setState(() => _erreur = erreur.message);
    } on OfflineException {
      if (mounted) setState(() => _erreur = 'Pas de réseau. Réessayez plus tard.');
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final dossier = ref.watch(dossierProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Mon dossier')),
      body: SafeArea(
        child: dossier.when(
          loading: () => const Center(child: CircularProgressIndicator()),
          error: (_, __) => const _Indisponible(),
          data: (d) => RefreshIndicator(
            onRefresh: () => rafraichirDepuis(ref, dossierProvider.future),
            child: ListView(
              padding: const EdgeInsets.all(HbaSpacing.gutter),
              children: [
                _Etat(dossier: d),
                const SizedBox(height: HbaSpacing.md),
                _Vehicule(dossier: d),
                const SizedBox(height: HbaSpacing.md),
                _Habilitations(dossier: d),
                const SizedBox(height: HbaSpacing.md),
                Text('Pièces', style: theme.textTheme.titleMedium),
                const SizedBox(height: HbaSpacing.sm),

                // LA CARTE D'ETAT EST EN HAUT DE PAGE, DONC HORS DE VUE des
                // qu'on descend jusqu'aux pieces. Un livreur qui appuie sur
                // une ligne et ne voit rien arriver conclut que l'ecran est
                // casse — pas que son dossier est fige. Le motif du blocage
                // doit etre la ou le geste echoue.
                if (d.valide || d.suspendu) ...[
                  _Bandeau(
                    texte: d.valide
                        ? 'Dossier valide : les pièces ne se modifient plus. '
                            "Si l'une d'elles n'est plus valable, HBA doit "
                            'rouvrir votre dossier.'
                        : 'Compte suspendu : les pièces ne se modifient plus.',
                  ),
                  const SizedBox(height: HbaSpacing.sm),
                  if (d.valide) ...[
                    const _DemanderAHba(sujet: 'Renouvellement d\'une pièce'),
                    const SizedBox(height: HbaSpacing.sm),
                  ],
                ],
                // LES PIECES EXIGEES POUR CE VEHICULE, rendues par le
                // service — plus les cinq natures en dur. Un cycliste y voyait
                // « permis » et « carte grise », deux lignes qu'il n'aurait
                // jamais pu satisfaire.
                //
                // LA LISTE COMPLETE RESTE LE REPLI : un service plus ancien qui
                // ne renverrait pas ce champ afficherait tout, comme avant.
                // Trop de lignes se corrige ; aucune ligne bloque le dossier.
                for (final piece in (d.exigees.isEmpty ? Piece.values : d.exigees)) ...[
                  _LignePiece(
                    piece: piece,
                    deposee: d.pieces.where((p) => p.type == piece).firstOrNull,
                    envoiEnCours: _envoiEnCours == piece,
                    avancement: _avancement,
                    modifiable: !d.valide && !d.suspendu,
                    onDeposer: () => _deposer(piece),
                  ),
                  const SizedBox(height: HbaSpacing.sm),
                ],
                if (_erreur != null) ...[
                  const SizedBox(height: HbaSpacing.xs),
                  Text(
                    _erreur!,
                    style: theme.textTheme.bodyMedium
                        ?.copyWith(color: theme.colorScheme.error),
                  ),
                ],
                const SizedBox(height: HbaSpacing.lg),
                _Soumission(dossier: d, onSoumettre: _soumettre),
                const SizedBox(height: HbaSpacing.xl),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _Etat extends StatelessWidget {
  const _Etat({required this.dossier});

  final Dossier dossier;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    final (libelle, ton, phrase) = switch (dossier) {
      _ when dossier.valide => (
          'DOSSIER VALIDE',
          HbaChipTone.success,
          'Vous pouvez travailler. Les pièces ne se modifient plus.',
        ),
      _ when dossier.suspendu => (
          'COMPTE SUSPENDU',
          HbaChipTone.danger,
          'Contactez HBA. Résoumettre un dossier ne debloque pas un compte suspendu.',
        ),
      _ when dossier.rejete => (
          'DOSSIER REFUSÉ',
          HbaChipTone.danger,
          'Corrigez ce qui est indique, puis renvoyez votre dossier.',
        ),
      _ when dossier.soumisLe != null => (
          'EN COURS D\'EXAMEN',
          HbaChipTone.warning,
          'HBA examine vos pièces. Vous pouvez encore les corriger.',
        ),
      _ => (
          'DOSSIER A COMPLETER',
          HbaChipTone.neutral,
          'Déposez vos pièces, declarez votre véhicule, puis envoyez le tout.',
        ),
    };

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          HbaChip(label: libelle, tone: ton),
          const SizedBox(height: HbaSpacing.sm),
          Text(phrase, style: theme.textTheme.bodyMedium),

          // LE MOTIF DU REFUS EST LA SEULE CHOSE QUI DISE QUOI CORRIGER.
          // Sans lui, le livreur renvoie la meme piece et se fait refuser
          // une seconde fois.
          if (dossier.motif.isNotEmpty) ...[
            const SizedBox(height: HbaSpacing.sm),
            Container(
              width: double.infinity,
              padding: const EdgeInsets.all(HbaSpacing.md),
              decoration: BoxDecoration(
                color: HbaColors.dangerSoft,
                borderRadius: BorderRadius.circular(HbaRadius.field),
              ),
              child: Text(
                dossier.motif,
                style: theme.textTheme.bodyMedium?.copyWith(color: HbaColors.danger),
              ),
            ),
          ],
        ],
      ),
    );
  }
}

class _Vehicule extends ConsumerStatefulWidget {
  const _Vehicule({required this.dossier});

  final Dossier dossier;

  @override
  ConsumerState<_Vehicule> createState() => _VehiculeState();
}

class _VehiculeState extends ConsumerState<_Vehicule> {
  static const _types = {
    'MOTORCYCLE': 'Moto',
    'CAR': 'Voiture',
    'VAN': 'Camionnette',
    'BICYCLE': 'Vélo',
    'TRICYCLE': 'Tricycle',
  };

  /// Les vehicules sans plaque ni papiers.
  ///
  /// UN SEUL POUR L'INSTANT, ET LA LISTE EXISTE QUAND MEME : ecrire
  /// « _type == BICYCLE » a trois endroits de cet ecran garantirait qu'un
  /// quatrieme oublie le jour ou un second type sans moteur arrive.
  static const _sansPlaque = {'BICYCLE'};

  bool get _plaqueAttendue => !_sansPlaque.contains(_type);

  late String _type = _types.containsKey(_depuisDossier())
      ? _depuisDossier()
      : 'MOTORCYCLE';
  /// Les vehicules qui ne prennent pas de passagers.
  ///
  /// ELLE DOUBLE UNE REGLE DU DOMAINE, ET C'EST ASSUME. La liste du service
  /// (capacitesPossibles) decrit le vehicule DEJA ENREGISTRE ; ici il faut
  /// decider si le champ existe pour le type EN COURS DE SAISIE, avant tout
  /// enregistrement. Le risque est borne : si cette liste se trompe, le service
  /// refuse par VEHICLE_CANNOT_CARRY_PASSENGERS et le refus s'affiche sous le
  /// formulaire. Au 1er octobre 2026, moto, voiture et tricycle prennent des
  /// passagers ; camionnette et velo, non.
  static const _sansPassagers = {'VAN', 'BICYCLE'};

  bool get _passagersPossibles => !_sansPassagers.contains(_type);

  late final _plaque = TextEditingController(text: widget.dossier.plaque);

  /// LA CHARGE SE SAISIT EN KILOS, PAS EN GRAMMES. Le service compte en
  /// grammes — l'unite entiere de tout le depot — mais personne ne declare
  /// « 50 000 » pour cinquante kilos.
  late final _charge = TextEditingController(
    text: widget.dossier.capaciteGrammes > 0
        ? '${widget.dossier.capaciteGrammes ~/ 1000}'
        : '',
  );

  late final _places = TextEditingController(
    text: widget.dossier.placesPassager > 0 ? '${widget.dossier.placesPassager}' : '',
  );

  /// Marque, modele et couleur. DECLARATIFS : ils n'autorisent rien, ils servent
  /// au client a reconnaitre le vehicule qui arrive. « La voiture blanche » se
  /// lit a trente metres, une immatriculation non — et c'est pour cela que le
  /// profil public les rend. Jusqu'au soir du 1er octobre 2026 aucun ecran ne
  /// les saisissait : ils restaient vides pour toujours.
  late final _marque = TextEditingController(text: widget.dossier.marque);
  late final _modele = TextEditingController(text: widget.dossier.modele);
  late final _couleur = TextEditingController(text: widget.dossier.couleur);

  bool _envoi = false;
  String? _erreur;

  static int _entier(String texte) => int.tryParse(texte.trim()) ?? 0;

  String _depuisDossier() => switch (widget.dossier.vehiculeType) {
        'Car' => 'CAR',
        'Van' => 'VAN',
        'Bicycle' => 'BICYCLE',
        'Tricycle' => 'TRICYCLE',
        _ => 'MOTORCYCLE',
      };

  @override
  void dispose() {
    _plaque.dispose();
    _charge.dispose();
    _places.dispose();
    _marque.dispose();
    _modele.dispose();
    _couleur.dispose();
    super.dispose();
  }

  Future<void> _enregistrer() async {
    setState(() {
      _envoi = true;
      _erreur = null;
    });

    try {
      await ref.read(dossierRepositoryProvider).declarerVehicule(
            type: _type,

            // ON N'ENVOIE PAS CE QU'ON NE DEMANDE PLUS. Le champ garde ce qui y
            // etait saisi avant un changement de type ; l'envoyer inscrirait
            // une immatriculation de moto sur un velo.
            plaque: _plaqueAttendue ? _plaque.text.trim() : '',
            capaciteGrammes: _entier(_charge.text) * 1000,

            // ZERO POUR UN VEHICULE QUI NE PREND PERSONNE, et non le contenu
            // du champ : il garde ce qui y etait saisi avant un changement de
            // type, exactement comme la plaque juste au-dessus.
            placesPassager: _passagersPossibles ? _entier(_places.text) : 0,

            marque: _marque.text.trim(),
            modele: _modele.text.trim(),
            couleur: _couleur.text.trim(),
          );
      ref.invalidate(dossierProvider);
    } on ApiException catch (erreur) {
      if (mounted) setState(() => _erreur = erreur.message);
    } on OfflineException {
      if (mounted) setState(() => _erreur = 'Pas de réseau.');
    } finally {
      if (mounted) setState(() => _envoi = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    // SEULE LA SUSPENSION GELE DESORMAIS. Un dossier valide se modifie depuis
    // le 3 octobre 2026 ; ce qu'il en coute est dit par le bandeau ci-dessous,
    // pas par un champ grise.
    final fige = widget.dossier.suspendu;
    final renvoieALExamen = widget.dossier.valide;

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(child: Text('Mon véhicule', style: theme.textTheme.titleMedium)),
              if (widget.dossier.vehiculeDeclare)
                const Icon(Icons.check_circle, color: HbaColors.success, size: 20),
            ],
          ),
          const SizedBox(height: HbaSpacing.md),
          DropdownButtonFormField<String>(
            initialValue: _type,
            items: [
              for (final entree in _types.entries)
                DropdownMenuItem(value: entree.key, child: Text(entree.value)),
            ],
            // LE setState PORTE PLUS QUE LE TYPE : il decide aussi si le
            // champ d'immatriculation existe et si le bouton s'active.
            onChanged: fige ? null : (v) => setState(() => _type = v ?? _type),
            decoration: const InputDecoration(labelText: 'Type'),
          ),
          const SizedBox(height: HbaSpacing.md),

          // LE VELO N'A PAS DE PLAQUE, ET LE CHAMP DISPARAIT PLUTOT QUE DE SE
          // GRISER. Un champ grise se lit « vous y viendrez plus tard » ; ici
          // il n'y a rien a saisir, jamais. Le laisser visible ferait chercher
          // au livreur une immatriculation qui n'existe pas.
          if (!_plaqueAttendue)
            Text(
              "Un vélo n'a ni plaque ni carte grise : votre dossier ne les "
              'demande pas.',
              style: theme.textTheme.bodySmall,
            )
          else
          TextField(
            controller: _plaque,
            enabled: !fige,
            textCapitalization: TextCapitalization.characters,
            inputFormatters: [
              LengthLimitingTextInputFormatter(32),
              // L'IMMATRICULATION EST COMPAREE A LA CARTE GRISE PAR OPS :
              // les minuscules et les espaces surnumeraires ne feraient que
              // rendre la comparaison penible.
              TextInputFormatter.withFunction(
                (avant, apres) => apres.copyWith(text: apres.text.toUpperCase()),
              ),
            ],
            // SANS CE setState, LE BOUTON RESTE GRISE POUR TOUJOURS : son
            // « onPressed » est evalue au build, et taper dans un TextField
            // n'en declenche aucun. Le livreur saisit sa plaque et rien ne
            // s'active — il n'a aucun moyen de deviner pourquoi.
            onChanged: (_) => setState(() {}),
            decoration: const InputDecoration(
              labelText: 'Immatriculation',
              hintText: 'AB 1234 RB',
            ),
          ),
          const SizedBox(height: HbaSpacing.md),
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: TextField(
                  controller: _marque,
                  enabled: !fige,
                  inputFormatters: [LengthLimitingTextInputFormatter(64)],
                  decoration: const InputDecoration(
                    labelText: 'Marque',
                    hintText: 'ex. Toyota',
                  ),
                ),
              ),
              const SizedBox(width: HbaSpacing.sm),
              Expanded(
                child: TextField(
                  controller: _modele,
                  enabled: !fige,
                  inputFormatters: [LengthLimitingTextInputFormatter(64)],
                  decoration: const InputDecoration(
                    labelText: 'Modèle',
                    hintText: 'ex. Corolla',
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: HbaSpacing.md),

          // LA COULEUR EST CE QUI SE VOIT LE PLUS LOIN. Un client qui attend au
          // bord d'une rue cherche « la voiture blanche » ; c'est elle, et non
          // la plaque, qui lui fait lever la main au bon moment.
          TextField(
            controller: _couleur,
            enabled: !fige,
            inputFormatters: [LengthLimitingTextInputFormatter(32)],
            decoration: const InputDecoration(
              labelText: 'Couleur',
              hintText: 'ex. blanche',
            ),
          ),
          const SizedBox(height: HbaSpacing.md),
          TextField(
            controller: _charge,
            enabled: !fige,
            keyboardType: TextInputType.number,
            inputFormatters: [
              FilteringTextInputFormatter.digitsOnly,
              LengthLimitingTextInputFormatter(4),
            ],
            decoration: const InputDecoration(
              labelText: 'Charge utile (kg)',
              hintText: 'Facultatif',
            ),
          ),

          // LE CHAMP DE PLACES DISPARAIT PLUTOT QUE DE SE GRISER, comme la
          // plaque : une camionnette ne prend pas de passagers, et il n'y a
          // rien a y saisir, jamais. Ce que le vehicule permet est dit une
          // seule fois, dans la carte des activites juste en dessous.
          if (_passagersPossibles) ...[
            const SizedBox(height: HbaSpacing.md),
            TextField(
              controller: _places,
              enabled: !fige,
              keyboardType: TextInputType.number,
              inputFormatters: [
                FilteringTextInputFormatter.digitsOnly,
                LengthLimitingTextInputFormatter(2),
              ],
              decoration: const InputDecoration(
                labelText: 'Places passagers',
                hintText: 'Sans compter la vôtre',
              ),
            ),
          ],
          if (_erreur != null) ...[
            const SizedBox(height: HbaSpacing.sm),
            Text(
              _erreur!,
              style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.error),
            ),
          ],
          const SizedBox(height: HbaSpacing.md),
          if (fige) ...[
            // CACHER LE BOUTON LAISSAIT CROIRE A UN ECRAN INCOMPLET. Dire
            // pourquoi il n'y en a pas coute une ligne et repond a la
            // question avant qu'elle ne se pose.
            const _Bandeau(
              texte: 'Votre compte est suspendu : le véhicule ne se modifie '
                  'pas. Contactez HBA.',
            ),
            const SizedBox(height: HbaSpacing.sm),
            const _DemanderAHba(sujet: 'Compte suspendu'),
          ] else ...[
            if (renvoieALExamen) ...[
              // LE PRIX EST DIT AVANT LE GESTE, PAS APRES. Un livreur qui
              // corrige la couleur de sa voiture et se retrouve hors ligne sans
              // avoir ete prevenu croit l'application cassee — et il a perdu sa
              // journee. La phrase est longue parce que la consequence l'est.
              const _Bandeau(
                texte: 'Votre dossier est validé. Enregistrer un changement le '
                    'renverra à l\'examen : vous ne recevrez plus de courses '
                    'tant qu\'HBA n\'aura pas revu vos pièces. Ne le faites que '
                    'si votre véhicule a réellement changé.',
              ),
              const SizedBox(height: HbaSpacing.sm),
            ],
            HbaButton(
              label: renvoieALExamen
                  ? 'Enregistrer et repasser à l\'examen'
                  : 'Enregistrer le véhicule',
              tone: HbaButtonTone.neutral,
              busy: _envoi,

              // SANS CETTE CONDITION, LE VELO NE S'ENREGISTRAIT JAMAIS : le
              // bouton attendait une plaque que l'ecran ne demande plus.
              onPressed: _plaqueAttendue && _plaque.text.trim().isEmpty
                  ? null
                  : _enregistrer,
            ),
          ],
        ],
      ),
    );
  }
}

/// Ce que le livreur declare exercer.
///
/// LE COLIS NE SE COCHE PAS : il est le socle. « Habilite » le rend vrai sans
/// declaration, et un dossier vide n'est pas un livreur sans metier — c'est le
/// metier d'origine. L'ecran n'affiche donc que ce qui s'AJOUTE au socle, et le
/// dit en une ligne.
///
/// LA LISTE DES CASES VIENT DU SERVICE, jamais de cette application. La table
/// vehicule vers habilitations est une regle metier : la recopier ici en aurait
/// fait une seconde version, qui aurait propose « transport de personnes » a un
/// cycliste. Sans la liste du service, l'ecran se tait plutot que de deviner.
///
/// UNE CARTE SEPAREE DU VEHICULE, AVEC SON PROPRE BOUTON, parce que ce sont deux
/// appels distincts cote serveur : enregistrer un vehicule et declarer des
/// activites ne reussissent ni n'echouent ensemble.
class _Habilitations extends ConsumerStatefulWidget {
  const _Habilitations({required this.dossier});

  final Dossier dossier;

  @override
  ConsumerState<_Habilitations> createState() => _HabilitationsState();
}

class _HabilitationsState extends ConsumerState<_Habilitations> {
  /// Les libelles, par nom normalise. Le colis n'y figure pas : voir la classe.
  static const _libelles = {
    'FOODDELIVERY': 'Restauration',
    'PASSENGERTRANSPORT': 'Transport de personnes',
  };

  static const _explications = {
    'FOODDELIVERY': 'Les commandes des commerces HBA Food.',
    'PASSENGERTRANSPORT': 'Les courses de personnes. Elle demande aussi une '
        'place passager déclarée sur votre véhicule.',
  };

  /// Le nom, reduit a ses lettres en majuscules.
  ///
  /// DEUX ORTHOGRAPHES CIRCULENT DANS CE DEPOT — « PassengerTransport » dans les
  /// reponses, « PASSENGER_TRANSPORT » dans les contrats. Normaliser pour
  /// AFFICHER, et renvoyer au serveur la chaine qu'il a lui-meme donnee : un
  /// changement d'orthographe cote serveur ne casse alors rien ici.
  static String _cle(String nom) =>
      nom.replaceAll(RegExp('[^A-Za-z]'), '').toUpperCase();

  /// Les choix en cours, dans l'orthographe du serveur.
  late final Set<String> _choix = {...widget.dossier.capacites};

  bool _envoi = false;
  String? _erreur;

  /// Ce que le vehicule permet, moins le socle.
  List<String> get _proposables =>
      [for (final c in widget.dossier.capacitesPossibles) if (_libelles.containsKey(_cle(c))) c];

  /// Ce qui partira au serveur.
  ///
  /// FILTRE PAR CE QUE LE VEHICULE PERMET, et pas seulement par ce qui est
  /// coche. Un livreur qui declare « transport de personnes » puis change son
  /// vehicule pour une camionnette garderait sinon un choix devenu impossible,
  /// invisible a l'ecran — et le refus du service nommerait une habilitation
  /// dont plus aucun interrupteur ne parle.
  ///
  /// DANS L'ORTHOGRAPHE DU SERVEUR : on renvoie la chaine recue, pas la notre.
  List<String> get _aEnvoyer => [
        for (final c in _proposables)
          if (_choix.any((x) => _cle(x) == _cle(c))) c,
      ];

  bool get _modifie {
    final declarees = widget.dossier.capacites.map(_cle).toSet();
    final choisies = _aEnvoyer.map(_cle).toSet();
    return declarees.length != choisies.length || !declarees.containsAll(choisies);
  }

  Future<void> _enregistrer() async {
    setState(() {
      _envoi = true;
      _erreur = null;
    });

    try {
      await ref.read(dossierRepositoryProvider).declarerHabilitations(_aEnvoyer);
      ref.invalidate(dossierProvider);
    } on ApiException catch (erreur) {
      if (mounted) setState(() => _erreur = erreur.message);
    } on OfflineException {
      if (mounted) setState(() => _erreur = 'Pas de réseau.');
    } finally {
      if (mounted) setState(() => _envoi = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    // LES ACTIVITES SE DECLARENT LIBREMENT SUR UN DOSSIER VALIDE depuis le
    // 3 octobre 2026, et SANS repasser a l'examen : cocher « transport de
    // personnes » n'apporte aucune piece nouvelle, et le vehicule qui
    // l'autorise a deja ete verifie. Seule la suspension gele.
    final fige = widget.dossier.suspendu;
    final proposables = _proposables;

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Mes activités', style: theme.textTheme.titleMedium),
          const SizedBox(height: HbaSpacing.xs),
          Text(
            'La livraison de colis est comprise : elle ne se déclare pas.',
            style: theme.textTheme.bodySmall,
          ),
          const SizedBox(height: HbaSpacing.md),

          // SANS VEHICULE DECLARE, LE SERVICE N'AUTORISE RIEN et la liste
          // revient vide. Le dire vaut mieux qu'une carte vide, qui se lit
          // comme un ecran casse.
          if (proposables.isEmpty)
            Text(
              widget.dossier.vehiculeDeclare
                  ? "Votre véhicule ne permet aucune activité en plus du colis."
                  : "Enregistrez d'abord votre véhicule : c'est lui qui "
                      'détermine les activités possibles.',
              style: theme.textTheme.bodySmall,
            )
          else
            for (final capacite in proposables) ...[
              Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          _libelles[_cle(capacite)] ?? capacite,
                          style: theme.textTheme.bodyMedium,
                        ),
                        Text(
                          _explications[_cle(capacite)] ?? '',
                          style: theme.textTheme.bodySmall,
                        ),
                      ],
                    ),
                  ),
                  Switch(
                    value: _choix.any((c) => _cle(c) == _cle(capacite)),
                    onChanged: fige
                        ? null
                        : (actif) => setState(() {
                              _choix.removeWhere((c) => _cle(c) == _cle(capacite));
                              if (actif) _choix.add(capacite);
                            }),
                  ),
                ],
              ),
              const SizedBox(height: HbaSpacing.sm),
            ],
          if (_erreur != null) ...[
            const SizedBox(height: HbaSpacing.xs),
            Text(
              _erreur!,
              style: theme.textTheme.bodySmall?.copyWith(color: theme.colorScheme.error),
            ),
          ],
          if (fige) ...[
            const SizedBox(height: HbaSpacing.sm),

            // MEME RAISON QUE POUR LE VEHICULE : cacher le bouton sans rien
            // dire laisse croire a un ecran incomplet.
            const _Bandeau(
              texte: 'Votre compte est suspendu : vos activités ne se modifient '
                  'pas. Contactez HBA.',
            ),
            const SizedBox(height: HbaSpacing.sm),
            const _DemanderAHba(sujet: 'Compte suspendu'),
          ] else if (proposables.isNotEmpty)
            HbaButton(
              label: 'Enregistrer mes activités',
              tone: HbaButtonTone.neutral,
              busy: _envoi,

              // RIEN A ENVOYER SI RIEN N'A CHANGE : le bouton reste gris
              // plutot que de rejouer la meme declaration.
              onPressed: _modifie ? _enregistrer : null,
            ),
        ],
      ),
    );
  }
}

class _LignePiece extends StatelessWidget {
  const _LignePiece({
    required this.piece,
    required this.deposee,
    required this.envoiEnCours,
    required this.avancement,
    required this.modifiable,
    required this.onDeposer,
  });

  final Piece piece;
  final PieceDeposee? deposee;
  final bool envoiEnCours;
  final double avancement;
  final bool modifiable;
  final VoidCallback onDeposer;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final presente = deposee != null;

    // UN CONTROLE DESACTIVE QUI RESSEMBLE A UN CONTROLE ACTIF est pire
    // qu'un controle absent : on appuie, rien ne se passe, et on recommence.
    // Le cadenas et l'encre attenuee disent l'etat avant le geste.
    return Opacity(
      opacity: modifiable ? 1 : 0.55,
      child: HbaCard(
        padding: const EdgeInsets.all(HbaSpacing.md),
        onTap: modifiable && !envoiEnCours ? onDeposer : null,
        child: Row(
        children: [
          _Vignette(url: deposee?.url, presente: presente),
          const SizedBox(width: HbaSpacing.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(piece.libelle, style: theme.textTheme.titleMedium),
                const SizedBox(height: 2),
                Text(
                  envoiEnCours
                      ? 'Envoi en cours…'
                      : !modifiable
                          ? (presente ? 'Déposée. Non modifiable.' : 'Non modifiable.')
                          : presente
                              ? 'Déposée. Appuyez pour remplacer.'
                              : piece.aide,
                  style: theme.textTheme.bodySmall,
                ),
                if (envoiEnCours) ...[
                  const SizedBox(height: HbaSpacing.sm),
                  ClipRRect(
                    borderRadius: BorderRadius.circular(4),
                    child: LinearProgressIndicator(
                      value: avancement == 0 ? null : avancement,
                      minHeight: 4,
                    ),
                  ),
                ],
              ],
            ),
          ),
          if (envoiEnCours)
            const SizedBox.shrink()
          else
            Icon(
              modifiable
                  ? (presente ? Icons.refresh : Icons.add_a_photo_outlined)
                  : Icons.lock_outline,
              color: HbaColors.inkFaint,
              size: 20,
            ),
          ],
        ),
      ),
    );
  }
}

/// Aperçu de la piece.
///
/// L'URL EST SIGNEE ET EXPIRE EN QUELQUES MINUTES. Une image cassee n'est
/// donc pas une anomalie mais un signe que la page est restee ouverte : le
/// repli le dit avec une icone, sans alarmer.
class _Vignette extends StatelessWidget {
  const _Vignette({required this.url, required this.presente});

  final String? url;
  final bool presente;

  @override
  Widget build(BuildContext context) {
    final cadre = BorderRadius.circular(HbaRadius.field);

    return ClipRRect(
      borderRadius: cadre,
      child: Container(
        width: 56,
        height: 56,
        color: presente ? HbaColors.successSoft : HbaColors.surfaceSunken,
        child: url == null || url!.isEmpty
            ? Icon(
                presente ? Icons.check : Icons.description_outlined,
                color: presente ? HbaColors.success : HbaColors.inkMuted,
              )
            : Image.network(
                url!,
                fit: BoxFit.cover,
                errorBuilder: (_, __, ___) =>
                    const Icon(Icons.check, color: HbaColors.success),
              ),
      ),
    );
  }
}

class _Soumission extends StatelessWidget {
  const _Soumission({required this.dossier, required this.onSoumettre});

  final Dossier dossier;
  final Future<void> Function() onSoumettre;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    if (dossier.valide) return const SizedBox.shrink();

    if (dossier.suspendu) {
      return HbaCard(
        padding: const EdgeInsets.all(HbaSpacing.lg),
        child: Text(
          'Un compte suspendu ne se rouvre pas depuis l\'application.',
          style: theme.textTheme.bodyMedium,
        ),
      );
    }

    // CE QUI MANQUE EST NOMME, pas compte. « Il manque 2 pieces » oblige le
    // livreur a chercher lesquelles dans la liste au-dessus.
    final aCompleter = <String>[
      for (final p in dossier.manquantes) p.libelle,
      if (!dossier.vehiculeDeclare) 'l\'immatriculation du véhicule',
    ];

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (aCompleter.isNotEmpty) ...[
          Text(
            'Il reste à fournir : ${aCompleter.join(', ')}.',
            style: theme.textTheme.bodyMedium,
          ),
          const SizedBox(height: HbaSpacing.sm),
        ],
        HbaButton(
          label: dossier.rejete ? 'Renvoyer mon dossier' : 'Envoyer mon dossier',
          icon: Icons.send_outlined,
          onPressed: dossier.peutSoumettre ? onSoumettre : null,
        ),
      ],
    );
  }
}

/// Bandeau d'information, pose au point ou le geste echoue.
class _Bandeau extends StatelessWidget {
  const _Bandeau({required this.texte});

  final String texte;

  @override
  Widget build(BuildContext context) => HbaCreux(
        padding: const EdgeInsets.all(HbaSpacing.md),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Icon(Icons.lock_outline, size: 18, color: HbaColors.inkMuted),
            const SizedBox(width: HbaSpacing.sm),
            Expanded(
              child: Text(texte, style: Theme.of(context).textTheme.bodySmall),
            ),
          ],
        ),
      );
}

class _Indisponible extends StatelessWidget {
  const _Indisponible();

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.all(HbaSpacing.gutter),
        child: HbaCard(
          padding: const EdgeInsets.all(HbaSpacing.lg),
          child: Text(
            'Dossier indisponible. Le service ne répond pas, ou votre profil '
            'vient d\'être créé et n\'est pas encore enregistré.',
            style: Theme.of(context).textTheme.bodyMedium,
          ),
        ),
      );
}

/// LA SORTIE DE SECOURS D'UN DOSSIER FIGE.
///
/// UN REFUS QUI NE DIT PAS QUOI FAIRE EST UN CUL-DE-SAC. L'ecran annoncait
/// « contactez HBA » et ne donnait ni numero, ni adresse, ni bouton : le
/// livreur qui venait de changer de moto lisait une phrase et refermait
/// l'application. Le chemin existait — il n'etait simplement nulle part.
///
/// AUCUNE REGLE NOUVELLE N'EST CREEE ICI. Le domaine fige un dossier valide
/// (DriverAggregate.DossierModifiable) parce que remplacer une CNI apres
/// validation reviendrait a valider une personne et a en laisser travailler
/// une autre. La reouverture passe par ops, qui suspend d'abord. Ce bloc ne
/// fait que rendre ce chemin atteignable depuis l'endroit ou le geste echoue.
///
/// Voir le point 20 des points a trancher : ni le changement de vehicule
/// apres validation, ni l'expiration d'une piece n'ont aujourd'hui de
/// parcours dans le systeme.
class _DemanderAHba extends ConsumerWidget {
  const _DemanderAHba({required this.sujet});

  /// Ce que le livreur demande, porte dans l'objet du courriel.
  final String sujet;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    if (!Reglages.aSupport) return const SizedBox.shrink();

    final livreurId = ref.watch(profilProvider).valueOrNull?.id;

    return Row(
      children: [
        if (Reglages.supportEmail.isNotEmpty)
          Expanded(
            child: _ActionSupport(
              icone: Icons.mail_outline,
              libelle: 'Écrire à HBA',
              onTap: () => _ouvrir(
                context,
                courrielSupport(livreurId: livreurId, sujet: sujet),
              ),
            ),
          ),
        if (Reglages.supportEmail.isNotEmpty &&
            Reglages.supportTelephone.isNotEmpty)
          const SizedBox(width: HbaSpacing.sm),
        if (Reglages.supportTelephone.isNotEmpty)
          Expanded(
            child: _ActionSupport(
              icone: Icons.call_outlined,
              libelle: 'Appeler HBA',
              onTap: () => _appeler(context),
            ),
          ),
      ],
    );
  }

  static Future<void> _ouvrir(BuildContext context, String cible) async {
    final uri = Uri.tryParse(cible);
    if (uri == null) return;

    try {
      await launchUrl(uri, mode: LaunchMode.externalApplication);
    } on Object {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          // CONSTANT PARCE QUE L'ADRESSE L'EST : elle vient d'un
          // « --dart-define », fige a la compilation.
          const SnackBar(content: Text('Écrivez à ${Reglages.supportEmail}')),
        );
      }
    }
  }

  static Future<void> _appeler(BuildContext context) async {
    final ok = await appeler(Reglages.supportTelephone);

    // UN BOUTON QUI NE FAIT RIEN EST PIRE QU'UN BOUTON ABSENT : on donne le
    // numero a composer a la main plutot que de laisser le livreur appuyer
    // dans le vide.
    if (!ok && context.mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Composez le ${Reglages.supportTelephone}')),
      );
    }
  }
}

class _ActionSupport extends StatelessWidget {
  const _ActionSupport({
    required this.icone,
    required this.libelle,
    required this.onTap,
  });

  final IconData icone;
  final String libelle;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => HbaPressable(
        onTap: onTap,
        elevation: HbaElevation.douce,
        radius: HbaRadius.button,
        alignment: Alignment.center,
        padding: const EdgeInsets.symmetric(
          horizontal: HbaSpacing.sm,
          vertical: 14,
        ),
        semantique: libelle,
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icone, size: 18, color: HbaColors.primaryInk),
            const SizedBox(width: HbaSpacing.sm),
            Flexible(
              child: Text(
                libelle,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(
                  color: HbaColors.primaryInk,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ),
          ],
        ),
      );
}
