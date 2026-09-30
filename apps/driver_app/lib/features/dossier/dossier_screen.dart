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
  late final _plaque = TextEditingController(text: widget.dossier.plaque);
  bool _envoi = false;
  String? _erreur;

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
    final fige = widget.dossier.valide || widget.dossier.suspendu;

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
              texte: 'Le véhicule ne se modifie plus. Si vous en avez change, '
                  'HBA doit rouvrir votre dossier : la plaque enregistrée est '
                  'celle qu\'ops a comparée à votre carte grise.',
            ),
            const SizedBox(height: HbaSpacing.sm),
            const _DemanderAHba(sujet: 'Changement de véhicule'),
          ] else
            HbaButton(
              label: 'Enregistrer le véhicule',
              tone: HbaButtonTone.neutral,
              busy: _envoi,

              // SANS CETTE CONDITION, LE VELO NE S'ENREGISTRAIT JAMAIS : le
              // bouton attendait une plaque que l'ecran ne demande plus.
              onPressed: _plaqueAttendue && _plaque.text.trim().isEmpty
                  ? null
                  : _enregistrer,
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
