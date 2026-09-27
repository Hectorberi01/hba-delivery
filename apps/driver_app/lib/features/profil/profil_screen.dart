import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';
import 'package:image_picker/image_picker.dart';
import 'package:package_info_plus/package_info_plus.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../core/preferences.dart';
import '../../core/rafraichir.dart';

import '../auth/session_controller.dart';
import '../dossier/dossier_repository.dart';
import 'profil_repository.dart';
import '../legal/conditions_livreur.dart';
import '../legal/confidentialite.dart';
import '../legal/document_legal.dart';
import '../legal/document_screen.dart';
import 'reglages_app.dart';
import 'suppression_compte.dart';

/// Version de l'application. Lue une fois, puis gardee.
final versionProvider = FutureProvider<String>((ref) async {
  final info = await PackageInfo.fromPlatform();
  return '${info.version} (${info.buildNumber})';
});

/// Profil du livreur.
///
/// CE QUI S'Y CHANGE ET CE QUI NE S'Y CHANGE PAS.
///
/// Le nom et le telephone sont en lecture seule : ils viennent d'Identity et
/// du dossier KYC, et les corriger demande a ops. La photo de profil, elle, se
/// change ICI — elle n'est pas une piece, elle sert au client a reconnaitre
/// qui arrive (ADR 0021). Le vehicule et les pieces se changent sur l'ecran
/// Dossier, vers lequel cet ecran renvoie.
///
/// La version precedente de ce commentaire annoncait « pas de declaration de
/// vehicule, pas d'envoi de pieces » et renvoyait a une carte « Pas encore
/// disponible ». Les trois etaient faux : les routes existent, l'ecran Dossier
/// les appelle, et la carte est devenue un simple lien.
class ProfilScreen extends ConsumerWidget {
  const ProfilScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final profil = ref.watch(profilProvider);

    return Scaffold(
      body: SafeArea(
        child: RefreshIndicator(
          onRefresh: () async {
            // LES DEUX ENSEMBLE : la photo vit dans le dossier, le reste dans
            // le profil. Ne relire que l'un laisserait un ecran a moitie
            // rafraichi, ce qui est plus trompeur que pas de rafraichissement.
            await rafraichirDepuis(ref, profilProvider.future);
            await rafraichirDepuis(ref, dossierProvider.future);
          },
          child: ListView(
            padding: const EdgeInsets.all(HbaSpacing.gutter),
            children: [
              Text('Profil', style: theme.textTheme.headlineMedium),
              const SizedBox(height: HbaSpacing.lg),
              profil.when(
                loading: () => const Padding(
                  padding: EdgeInsets.symmetric(vertical: HbaSpacing.xxl),
                  child: Center(child: CircularProgressIndicator()),
                ),
                error: (_, __) => const _ProfilIndisponible(),
                data: (p) => Column(
                  children: [
                    // LA PHOTO VIENT DU DOSSIER, PAS DU PROFIL, et ce n'est
                    // pas un detour : /me ne la porte pas. Elle n'y est pas
                    // par oubli mais par cout — l'adresse de la photo est une
                    // URL SIGNEE qui expire, qu'il faut refabriquer a chaque
                    // lecture. /me est appele par l'ecran d'accueil a chaque
                    // ouverture ; y ajouter une signature ferait payer a tout
                    // le monde une image que seul cet ecran affiche.
                    //
                    // « valueOrNull » : si le dossier ne répond pas, l'ecran
                    // montre les initiales et continue.
                    _Identite(
                      profil: p,
                      photo: ref.watch(dossierProvider).valueOrNull?.photoProfil,
                    ),
                    const SizedBox(height: HbaSpacing.md),
                    _Details(profil: p),
                  ],
                ),
              ),
              const SizedBox(height: HbaSpacing.md),

              // L'IDENTIFIANT PASSE PAR « valueOrNull », PAS PAR « when ».
              // L'assistance doit s'afficher meme quand le profil n'a pas pu
              // etre lu — c'est precisement le moment ou un livreur a besoin
              // d'appeler. L'objet du courriel est alors sans identifiant, ce
              // qui vaut mieux qu'un bouton absent.
              const _CarteReglages(),
              const SizedBox(height: HbaSpacing.md),
              _Assistance(livreurId: profil.valueOrNull?.id),
              const SizedBox(height: HbaSpacing.md),
              const _LienDossier(),
              const SizedBox(height: HbaSpacing.md),
              const _Mentions(),
              const SizedBox(height: HbaSpacing.lg),
              const _Deconnexion(),
              const SizedBox(height: HbaSpacing.sm),
              const _LienSuppression(),
              const SizedBox(height: HbaSpacing.xl),
            ],
          ),
        ),
      ),
    );
  }
}

class _Identite extends StatelessWidget {
  const _Identite({required this.profil, this.photo});

  final ProfilLivreur profil;

  /// Adresse SIGNEE de la photo de profil, ou nulle. Elle expire : l'ecran ne
  /// la garde pas, il la relit avec le dossier.
  final String? photo;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    final ton = switch (profil.dossier) {
      EtatDossier.valide => HbaChipTone.success,
      EtatDossier.enAttente => HbaChipTone.warning,
      EtatDossier.rejete || EtatDossier.suspendu => HbaChipTone.danger,
      EtatDossier.inconnu => HbaChipTone.neutral,
    };

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              _Avatar(nom: profil.displayName, photo: photo),
              const SizedBox(width: HbaSpacing.md),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      profil.displayName.isEmpty ? 'Sans nom' : profil.displayName,
                      style: theme.textTheme.titleMedium,
                    ),
                    const SizedBox(height: 2),
                    Text(profil.phone, style: theme.textTheme.bodyMedium),
                  ],
                ),
              ),
            ],
          ),
          const SizedBox(height: HbaSpacing.md),
          Wrap(
            spacing: HbaSpacing.sm,
            runSpacing: HbaSpacing.sm,
            children: [
              HbaChip(label: profil.dossier.libelle.toUpperCase(), tone: ton),
              HbaChip(label: profil.operationnel.libelle.toUpperCase()),
            ],
          ),

          // LE MOTIF EST LA SEULE CHOSE QUI DISE QUOI CORRIGER. Un dossier
          // rejete sans motif affiche laisse le livreur sans recours.
          if (profil.motif.isNotEmpty) ...[
            const SizedBox(height: HbaSpacing.sm),
            Text(profil.motif, style: theme.textTheme.bodyMedium),
          ],
          if (profil.dossier == EtatDossier.enAttente) ...[
            const SizedBox(height: HbaSpacing.sm),
            Text(
              'Vous pourrez passer en ligne dès que vos pièces auront été '
              'verifiees.',
              style: theme.textTheme.bodyMedium,
            ),
          ],
        ],
      ),
    );
  }
}

class _Details extends StatelessWidget {
  const _Details({required this.profil});

  final ProfilLivreur profil;

  @override
  Widget build(BuildContext context) {
    final vehicule = profil.vehicule;

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        children: [
          _Ligne(
            terme: 'Véhicule',
            valeur: vehicule == null || !vehicule.estRenseigne
                ? 'Non déclaré'
                : vehicule.libelle,
          ),
          if (vehicule != null && vehicule.plaque.isNotEmpty)
            _Ligne(terme: 'Immatriculation', valeur: vehicule.plaque),
          _Ligne(terme: 'Inscrit le', valeur: _date(profil.inscritLe)),
          _Ligne(terme: 'Dossier valide le', valeur: _date(profil.valideLe)),
        ],
      ),
    );
  }

  static String _date(DateTime? value) {
    if (value == null) return '—';

    String deux(int n) => n.toString().padLeft(2, '0');
    return '${deux(value.day)}/${deux(value.month)}/${value.year}';
  }
}

class _Ligne extends StatelessWidget {
  const _Ligne({required this.terme, required this.valeur});

  final String terme;
  final String valeur;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 5),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: Text(terme, style: Theme.of(context).textTheme.bodyMedium),
            ),
            const SizedBox(width: HbaSpacing.md),
            Text(
              valeur,
              textAlign: TextAlign.right,
              style: const TextStyle(
                color: HbaColors.ink,
                fontWeight: FontWeight.w600,
              ),
            ),
          ],
        ),
      );
}

/// La photo de profil, et le moyen de la changer.
///
/// ELLE NE S'AFFICHAIT NULLE PART, ET ELLE NE POUVAIT PAS NON PLUS SE POSER.
/// La route de depot existait cote passerelle, la methode existait dans le
/// depot du dossier, l'adresse de lecture revenait deja dans la reponse — mais
/// aucun ecran n'appelait rien de tout cela. Afficher la photo sans donner le
/// moyen de la choisir aurait donne un rond vide a tout le monde, pour
/// toujours : les deux moities vont ensemble.
///
/// LES INITIALES RESTENT LE SOCLE, pas un pis-aller temporaire. Un livreur qui
/// n'a pas mis de photo, celui dont le telephone n'a pas de reseau, celui dont
/// l'adresse signee a expire — tous les trois voient la meme chose, et elle est
/// lisible.
class _Avatar extends ConsumerStatefulWidget {
  const _Avatar({required this.nom, this.photo});

  final String nom;
  final String? photo;

  @override
  ConsumerState<_Avatar> createState() => _AvatarState();
}

class _AvatarState extends ConsumerState<_Avatar> {
  static const _cote = 56.0;

  bool _envoi = false;

  @override
  Widget build(BuildContext context) {
    final photo = widget.photo;
    final aUnePhoto = photo != null && photo.isNotEmpty;

    return Semantics(
      button: true,
      label: aUnePhoto ? 'Changer votre photo' : 'Ajouter une photo',
      child: GestureDetector(
        onTap: _envoi ? null : _changer,
        behavior: HitTestBehavior.opaque,
        child: SizedBox(
          // LA CIBLE DEBORDE LE DESSIN de quelques pixels en bas et a droite,
          // parce que la pastille d'appareil photo y depasse.
          width: _cote + 6,
          height: _cote + 6,
          child: Stack(
            children: [
              Container(
                width: _cote,
                height: _cote,
                alignment: Alignment.center,
                decoration: const BoxDecoration(
                  color: HbaColors.primarySoft,
                  shape: BoxShape.circle,
                  boxShadow: [
                    BoxShadow(
                      color: HbaColors.ombre,
                      offset: Offset(3, 3),
                      blurRadius: 8,
                    ),
                    BoxShadow(
                      color: HbaColors.lumiere,
                      offset: Offset(-3, -3),
                      blurRadius: 8,
                    ),
                  ],
                ),
                child: ClipOval(
                  child: _envoi
                      ? const SizedBox(
                          height: 22,
                          width: 22,
                          child: CircularProgressIndicator(strokeWidth: 2.4),
                        )
                      : aUnePhoto
                          ? Image.network(
                              photo,
                              width: _cote,
                              height: _cote,
                              fit: BoxFit.cover,

                              // L'ADRESSE EST SIGNEE ET ELLE EXPIRE. Quand
                              // elle est perimee — ou que le reseau manque —,
                              // on retombe sur les initiales plutot que sur
                              // l'icone d'image cassee de Flutter, qui se lit
                              // comme une panne de l'application.
                              errorBuilder: (_, __, ___) => _Initiales(nom: widget.nom),
                              loadingBuilder: (_, enfant, avancement) =>
                                  avancement == null ? enfant : _Initiales(nom: widget.nom),
                            )
                          : _Initiales(nom: widget.nom),
                ),
              ),

              // LA PASTILLE D'APPAREIL PHOTO EST LA SEULE CHOSE QUI DISE QUE
              // CE ROND SE TOUCHE. Sans elle, personne ne pense a appuyer sur
              // son propre portrait — et la photo resterait vide alors que
              // tout est en place pour la poser.
              Positioned(
                right: 0,
                bottom: 0,
                child: Container(
                  width: 22,
                  height: 22,
                  alignment: Alignment.center,
                  decoration: const BoxDecoration(
                    color: HbaColors.surface,
                    shape: BoxShape.circle,
                    boxShadow: [
                      BoxShadow(
                        color: HbaColors.ombre,
                        offset: Offset(1, 1),
                        blurRadius: 4,
                      ),
                    ],
                  ),
                  child: Icon(
                    aUnePhoto ? Icons.edit_outlined : Icons.photo_camera_outlined,
                    size: 13,
                    color: HbaColors.primaryInk,
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Future<void> _changer() async {
    final source = await _choisirSource();
    if (source == null || !mounted) return;

    final XFile? prise;
    try {
      prise = await ImagePicker().pickImage(
        source: source,

        // MEMES BORNES QUE POUR LES PIECES DU DOSSIER : elles evitent de
        // charger cinquante megapixels en memoire, la compression serieuse
        // vient apres, cote depot.
        maxWidth: 3000,
        maxHeight: 3000,
      );
    } on Object {
      _dire('Impossible d\'ouvrir l\'appareil photo.');
      return;
    }

    if (prise == null || !mounted) return;

    setState(() => _envoi = true);

    try {
      await ref
          .read(dossierRepositoryProvider)
          .deposerPhotoProfil(File(prise.path));

      // LE DOSSIER EST RELU, PAS LE PROFIL. C'est lui qui porte l'adresse de
      // la photo, et elle est refabriquee a chaque lecture : sans cette
      // relecture, l'ecran garderait l'ancienne image jusqu'au prochain
      // passage sur l'onglet.
      ref.invalidate(dossierProvider);
    } on ApiException catch (erreur) {
      _dire(erreur.message);
    } on OfflineException {
      _dire('Pas de réseau. La photo n\'a pas été envoyée.');
    } on Object {
      _dire('L\'envoi de la photo a échoué.');
    } finally {
      if (mounted) setState(() => _envoi = false);
    }
  }

  Future<ImageSource?> _choisirSource() => showModalBottomSheet<ImageSource>(
        context: context,
        // MEME RAISON QUE LA FEUILLE DE RECAPITULATIF : les onglets ont
        // chacun leur Navigator, loge au-dessus de la barre du bas.
        useRootNavigator: true,
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

  void _dire(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
  }
}

class _Initiales extends StatelessWidget {
  const _Initiales({required this.nom});

  final String nom;

  @override
  Widget build(BuildContext context) {
    final lettres = nom
        .split(RegExp(r'\s+'))
        .where((m) => m.isNotEmpty)
        .take(2)
        .map((m) => m[0].toUpperCase())
        .join();

    return Container(
      alignment: Alignment.center,
      color: HbaColors.primarySoft,
      child: Text(
        lettres.isEmpty ? '?' : lettres,
        style: const TextStyle(
          color: HbaColors.primaryInk,
          fontWeight: FontWeight.w800,
          fontSize: 18,
        ),
      ),
    );
  }
}

class _ProfilIndisponible extends StatelessWidget {
  const _ProfilIndisponible();

  @override
  Widget build(BuildContext context) => HbaCard(
        padding: const EdgeInsets.all(HbaSpacing.lg),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Profil indisponible',
                style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: HbaSpacing.xs),
            Text(
              'Le service ne répond pas, ou votre profil vient d\'être créé et '
              'n\'est pas encore enregistré. Tirez vers le bas pour réessayer.',
              style: Theme.of(context).textTheme.bodyMedium,
            ),
          ],
        ),
      );
}

/// Joindre HBA.
///
/// RIEN NE S'AFFICHE SI AUCUN CONTACT N'EST CONFIGURE. Un bouton « Appeler le
/// support » qui ouvre un composeur vide est pire que pas de bouton : le
/// livreur croit avoir un recours qu'il n'a pas.
/// Ce que le livreur regle lui-meme.
///
/// UN SEUL INTERRUPTEUR, ET IL NE COUPE QUE LE SON. La vibration continue :
/// pouvoir tout eteindre reviendrait a se rendre injoignable sans le savoir —
/// un livreur en ligne qui ne recoit plus rien conclut que l'application est
/// cassee, pas qu'il a eteint son propre signal. Le sous-titre le dit, pour
/// qu'il n'ait pas a le decouvrir.
///
/// LE REGLAGE VIT SUR CE TELEPHONE. Changer d'appareil le remet a « actif ».
/// C'est le prix assume de ne pas ajouter un attribut au livreur cote service
/// pour un reglage de confort.
class _CarteReglages extends ConsumerWidget {
  const _CarteReglages();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final son = ref.watch(sonDOffreProvider);

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Réglages', style: theme.textTheme.titleMedium),
          const SizedBox(height: HbaSpacing.sm),
          Row(
            children: [
              const Icon(Icons.volume_up_outlined,
                  size: 20, color: HbaColors.inkMuted),
              const SizedBox(width: HbaSpacing.md),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Text(
                      'Son à l\'arrivée d\'une offre',
                      style: TextStyle(
                        color: HbaColors.ink,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                    Text(
                      son
                          ? 'Le téléphone sonne même en sourdine.'
                          : 'Le téléphone vibre seulement.',
                      style: theme.textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
              // AUCUNE COULEUR PASSEE ICI : le theme pose deja
              // « colorScheme.primary », c'est-a-dire #C4550C, le seul orange
              // de la palette qui tienne le contraste sous du blanc. Le nom du
              // parametre qui aurait servi a le surcharger a change entre deux
              // versions de Flutter ; s'en passer evite les deux problemes.
              Switch(
                value: son,
                onChanged: (actif) => ref
                    .read(sonDOffreProvider.notifier)
                    .definir(actif: actif),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _Assistance extends StatelessWidget {
  const _Assistance({this.livreurId});

  /// Recopie dans l'objet du courriel. LE SUPPORT DEMANDE TOUJOURS CET
  /// IDENTIFIANT EN PREMIER, et un livreur qui ecrit depuis la route ne va pas
  /// aller le chercher dans son profil. Le mettre dans l'objet epargne un
  /// aller-retour a chaque demande.
  final String? livreurId;

  @override
  Widget build(BuildContext context) {
    if (!Reglages.aSupport) return const SizedBox.shrink();

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Assistance', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: HbaSpacing.sm),
          if (Reglages.supportTelephone.isNotEmpty)
            const _Action(
              icone: Icons.call_outlined,
              libelle: 'Appeler le support',
              detail: Reglages.supportTelephone,

              // « tel: » OUVRE LE COMPOSEUR, IL N'APPELLE PAS. Android et iOS
              // interdisent a une application de declencher un appel elle-meme,
              // et c'est tant mieux : le livreur voit le numero avant que la
              // ligne ne parte.
              cible: 'tel:${Reglages.supportTelephone}',
            ),
          if (Reglages.supportEmail.isNotEmpty)
            _Action(
              icone: Icons.mail_outline,
              libelle: 'Écrire au support',
              detail: Reglages.supportEmail,
              cible: courrielSupport(livreurId: livreurId),
            ),
        ],
      ),
    );
  }
}

class _Mentions extends ConsumerWidget {
  const _Mentions();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final version = ref.watch(versionProvider);

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('A propos', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: HbaSpacing.sm),
          // LES DEUX DOCUMENTS S'OUVRENT DANS L'APPLICATION, et ils ne
          // dependent plus d'une adresse configuree : ils s'affichaient
          // autrefois seulement si HBA_CGU et HBA_CONFIDENTIALITE etaient
          // renseignes, c'est-a-dire jamais. Un livreur n'avait donc acces a
          // aucun des deux — or l'un est un contrat et l'autre une obligation
          // des deux magasins d'applications.
          const _Ouvrir(
            icone: Icons.description_outlined,
            libelle: 'Conditions du livreur',
            detail: 'Offres, courses, rémunération, versements',
            document: conditionsLivreur,
            enLigne: Reglages.conditions,
          ),
          const _Ouvrir(
            icone: Icons.shield_outlined,
            libelle: 'Politique de confidentialité',
            detail: 'Position, pièces du dossier, vos droits',
            document: confidentialite,
            enLigne: Reglages.confidentialite,
          ),
          const SizedBox(height: HbaSpacing.sm),

          // LE NUMERO DE VERSION EST LA PREMIERE CHOSE QU'ON DEMANDE a un
          // livreur qui signale un bug. Sans lui, on debogue a l'aveugle
          // contre une version qu'il n'a peut-etre plus.
          _Ligne(
            terme: 'Version',
            valeur: version.maybeWhen(
              data: (v) => v,
              orElse: () => '—',
            ),
          ),
        ],
      ),
    );
  }
}

class _Action extends StatelessWidget {
  const _Action({
    required this.icone,
    required this.libelle,
    required this.cible,
    this.detail,
  });

  final IconData icone;
  final String libelle;
  final String? detail;
  final String cible;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return InkWell(
      onTap: () async {
        final uri = Uri.tryParse(cible);
        if (uri == null) return;

        try {
          await launchUrl(uri, mode: LaunchMode.externalApplication);
        } on Object {
          if (context.mounted) {
            ScaffoldMessenger.of(context).showSnackBar(
              SnackBar(content: Text('Impossible d\'ouvrir : $libelle')),
            );
          }
        }
      },
      borderRadius: BorderRadius.circular(HbaRadius.field),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: HbaSpacing.sm),
        child: Row(
          children: [
            Icon(icone, size: 20, color: HbaColors.inkMuted),
            const SizedBox(width: HbaSpacing.md),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    libelle,
                    style: const TextStyle(
                      color: HbaColors.ink,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  if (detail != null)
                    Text(detail!, style: theme.textTheme.bodySmall),
                ],
              ),
            ),
            const Icon(Icons.chevron_right, color: HbaColors.inkFaint, size: 20),
          ],
        ),
      ),
    );
  }
}

/// Ouvre un document juridique dans l'application.
///
/// IL RESSEMBLE A _Action MAIS NE LUI RESSEMBLE QUE DE DEHORS : l'un sort de
/// l'application — un composeur, un client de courriel, un navigateur —,
/// l'autre pousse un ecran. Les fondre demanderait de decider a l'execution ce
/// que signifie « cible », et c'est le genre d'economie qui coute un ecran
/// blanc six mois plus tard.
class _Ouvrir extends StatelessWidget {
  const _Ouvrir({
    required this.icone,
    required this.libelle,
    required this.document,
    this.detail,
    this.enLigne,
  });

  final IconData icone;
  final String libelle;
  final String? detail;
  final DocumentLegal document;
  final String? enLigne;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return InkWell(
      onTap: () => Navigator.of(context).push(
        MaterialPageRoute<void>(
          builder: (_) => DocumentScreen(document: document, enLigne: enLigne),
        ),
      ),
      borderRadius: BorderRadius.circular(HbaRadius.field),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: HbaSpacing.sm),
        child: Row(
          children: [
            Icon(icone, size: 20, color: HbaColors.inkMuted),
            const SizedBox(width: HbaSpacing.md),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    libelle,
                    style: const TextStyle(
                      color: HbaColors.ink,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  if (detail != null)
                    Text(detail!, style: theme.textTheme.bodySmall),
                ],
              ),
            ),
            const Icon(Icons.chevron_right, color: HbaColors.inkFaint, size: 20),
          ],
        ),
      ),
    );
  }
}

/// Acces au dossier.
///
/// IL A REMPLACE UNE CARTE QUI DISAIT « PAS ENCORE DISPONIBLE ». Le vehicule
/// se declare et les pieces s'envoient depuis l'application ; laisser
/// l'ancien texte serait desormais un mensonge.
/// Le lien vers l'ecran Dossier.
///
/// ELLE S'APPELAIT « _CeQuiManque » DU TEMPS OU ELLE ANNONCAIT CE QUE
/// L'APPLICATION NE SAVAIT PAS FAIRE. Elle ouvre aujourd'hui l'ecran qui le
/// fait ; le nom a suivi.
class _LienDossier extends StatelessWidget {
  const _LienDossier();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      onTap: () => context.push('/profil/dossier'),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('Mon dossier', style: theme.textTheme.titleMedium),
                const SizedBox(height: 2),
                Text(
                  'Pièces justificatives et véhicule.',
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

class _Deconnexion extends ConsumerWidget {
  const _Deconnexion();

  @override
  Widget build(BuildContext context, WidgetRef ref) => HbaButton(
        label: 'Se déconnecter',
        tone: HbaButtonTone.neutral,
        icon: Icons.logout,
        onPressed: () => ref.read(sessionProvider.notifier).signOut(),
      );
}

/// Discret, mais present : les deux magasins exigent ce chemin dans l'app.
class _LienSuppression extends StatelessWidget {
  const _LienSuppression();

  @override
  Widget build(BuildContext context) => Center(
        child: TextButton(
          onPressed: () => Navigator.of(context).push(
            MaterialPageRoute<void>(
              builder: (_) => const SuppressionCompteScreen(),
            ),
          ),
          child: const Text(
            'Supprimer mon compte',
            style: TextStyle(color: HbaColors.inkMuted, fontSize: 13),
          ),
        ),
      );
}
