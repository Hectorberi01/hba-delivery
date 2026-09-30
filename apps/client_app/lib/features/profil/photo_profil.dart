import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';
import 'package:image_picker/image_picker.dart';

import 'cadrage_photo.dart';
import 'profil_providers.dart';

/// Le rond du profil : la photo si elle existe, les initiales sinon, et la
/// pastille qui dit que ce rond se touche.
///
/// LA PASTILLE EST LA SEULE CHOSE QUI DISE QU'ON PEUT APPUYER. Sans elle,
/// personne ne pense a toucher son propre portrait — et la photo resterait vide
/// alors que tout est en place pour la poser. C'est la meme pastille que dans
/// l'application livreur, et c'est voulu : les deux applications sont faites
/// par la meme maison et se ressemblent la ou elles font la meme chose.
///
/// LES INITIALES PLUTOT QU'UNE SILHOUETTE GRISE. Une silhouette est la meme
/// pour tout le monde ; deux lettres disent a qui appartient le compte, ce qui
/// compte sur un telephone qu'on prete.
class PhotoProfil extends ConsumerStatefulWidget {
  const PhotoProfil({required this.nom, required this.aUnePhoto, super.key});

  final String nom;
  final bool aUnePhoto;

  /// Deux lettres, tirees du premier et du dernier mot.
  static String initiales(String nom) {
    final mots =
        nom.trim().split(RegExp(r'\s+')).where((m) => m.isNotEmpty).toList();

    // SUBSTRING(0, 1) ET NON [0] : un nom peut commencer par un caractere
    // accentue, et les deux donnent ici le meme resultat — mais substring dit
    // ce qu'on veut, un prefixe, la ou l'index dit une unite de code.
    String premiere(String mot) => mot.substring(0, 1).toUpperCase();

    if (mots.isEmpty) return '?';
    if (mots.length == 1) return premiere(mots.first);

    return premiere(mots.first) + premiere(mots.last);
  }

  @override
  ConsumerState<PhotoProfil> createState() => _PhotoProfilState();
}

class _PhotoProfilState extends ConsumerState<PhotoProfil> {
  static const _cote = 92.0;

  bool _envoi = false;

  @override
  Widget build(BuildContext context) {
    final lien = ref.watch(photoProvider).valueOrNull;

    return Semantics(
      button: true,
      label: widget.aUnePhoto
          ? 'Changer la photo de profil'
          : 'Ajouter une photo de profil',
      child: GestureDetector(
        // OPAQUE : sans cela, le toucher traverse le rond et ouvre le
        // formulaire de la carte qui l'entoure. Le client croirait avoir
        // appuye sur sa photo et se retrouverait a modifier son nom.
        behavior: HitTestBehavior.opaque,
        onTap: _envoi ? null : _ouvrirLeMenu,
        child: Stack(
          alignment: Alignment.center,
          children: [
            Container(
              height: _cote,
              width: _cote,
              alignment: Alignment.center,
              decoration: const BoxDecoration(
                color: HbaColors.primarySoft,
                shape: BoxShape.circle,
              ),

              // PAS DE ClipRRect NI DE ClipPath ICI : ce rond n'entoure aucune
              // vue native. ClipOval sur une Image est sans danger — la
              // consigne iOS ne vise que les vues natives, la carte Google en
              // tete.
              child: ClipOval(
                child: _envoi
                    ? const SizedBox(
                        height: 24,
                        width: 24,
                        child: CircularProgressIndicator(strokeWidth: 2.4),
                      )
                    : lien == null
                        ? _Initiales(nom: widget.nom)
                        : Image.network(
                            lien,
                            width: _cote,
                            height: _cote,
                            fit: BoxFit.cover,

                            // L'ADRESSE EST SIGNEE ET ELLE EXPIRE. Quand elle
                            // est perimee — ou que le reseau manque — on
                            // retombe sur les initiales plutot que sur l'icone
                            // d'image cassee de Flutter, qui se lit comme une
                            // panne de l'application.
                            errorBuilder: (_, __, ___) =>
                                _Initiales(nom: widget.nom),
                            loadingBuilder: (_, enfant, avancement) =>
                                avancement == null
                                    ? enfant
                                    : _Initiales(nom: widget.nom),
                          ),
              ),
            ),
            Positioned(
              right: 2,
              bottom: 2,
              child: Container(
                width: 28,
                height: 28,
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
                  widget.aUnePhoto
                      ? Icons.edit_outlined
                      : Icons.photo_camera_outlined,
                  size: 16,
                  color: HbaColors.primaryInk,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _ouvrirLeMenu() async {
    final choix = await showModalBottomSheet<_Geste>(
      context: context,
      // LES ONGLETS ONT CHACUN LEUR NAVIGATOR, loge au-dessus de la barre du
      // bas : sans useRootNavigator, la feuille s'ouvre SOUS la barre.
      useRootNavigator: true,
      backgroundColor: HbaColors.background,
      shape: const RoundedRectangleBorder(
        borderRadius:
            BorderRadius.vertical(top: Radius.circular(HbaRadius.card)),
      ),
      builder: (feuille) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ListTile(
              leading: const Icon(Icons.photo_camera_outlined),
              title: const Text('Prendre une photo'),
              onTap: () => Navigator.of(feuille).pop(_Geste.appareil),
            ),
            ListTile(
              leading: const Icon(Icons.photo_library_outlined),
              title: const Text('Choisir dans la galerie'),
              onTap: () => Navigator.of(feuille).pop(_Geste.galerie),
            ),

            // RETIRER N'APPARAIT QUE S'IL Y A QUELQUE CHOSE A RETIRER. Une
            // ligne grisee en permanence apprend a ne plus lire le menu.
            if (widget.aUnePhoto)
              ListTile(
                leading: const Icon(
                  Icons.delete_outline,
                  color: HbaColors.danger,
                ),
                title: const Text(
                  'Retirer ma photo',
                  style: TextStyle(color: HbaColors.danger),
                ),
                onTap: () => Navigator.of(feuille).pop(_Geste.retirer),
              ),
            const SizedBox(height: HbaSpacing.sm),
          ],
        ),
      ),
    );

    if (choix == null || !mounted) return;

    if (choix == _Geste.retirer) {
      await _retirer();
      return;
    }

    await _envoyer(
      choix == _Geste.appareil ? ImageSource.camera : ImageSource.gallery,
    );
  }

  Future<void> _envoyer(ImageSource source) async {
    final XFile? prise;

    try {
      prise = await ImagePicker().pickImage(
        source: source,
        // CES BORNES NE REMPLACENT PAS LE CADRAGE : elles evitent de charger
        // cinquante megapixels en memoire. Le recadrage et la compression se
        // font a l'ecran suivant.
        maxWidth: 3000,
        maxHeight: 3000,
      );
    } on Object {
      _dire("Impossible d'ouvrir l'appareil photo.");
      return;
    }

    if (prise == null || !mounted) return;

    // LES OCTETS SONT LUS AVANT DE TOUCHER AU CONTEXTE. Passer
    // « await prise.readAsBytes() » en argument ferait attendre entre le
    // moment ou l'on prend « context » et celui ou on s'en sert — ce que
    // « use_build_context_synchronously » signale a juste titre : l'ecran peut
    // avoir ete demonte entre les deux.
    final brut = await prise.readAsBytes();

    if (!mounted) return;

    // LE CADRAGE S'INTERCALE ICI, ET IL PEUT ETRE ABANDONNE. Renoncer au
    // cadrage renonce a l'envoi : on ne veut surtout pas remonter une photo
    // brute « au cas ou », qui serait rognee au centre par l'affichage — ce
    // qui est exactement le defaut que cet ecran corrige.
    final octets = await CadragePhoto.ouvrir(context, brut);

    if (octets == null || !mounted) return;

    setState(() => _envoi = true);

    try {
      await ref.read(profilRepositoryProvider).deposerPhoto(octets);

      // LE PROFIL EST RELU, ET C'EST CE QUI RAFRAICHIT L'IMAGE. Le lien
      // d'affichage se recalcule a partir de l'identifiant de media : tant que
      // le profil n'est pas relu, l'ecran garde l'ancienne photo.
      ref.invalidate(profilProvider);
    } on ApiException catch (erreur) {
      _dire(erreur.message);
    } on OfflineException {
      _dire("Pas de réseau. La photo n'a pas été envoyée.");
    } on Object {
      _dire("L'envoi de la photo a échoué.");
    } finally {
      if (mounted) setState(() => _envoi = false);
    }
  }

  Future<void> _retirer() async {
    // RETIRER SA PHOTO SE CONFIRME. Le fichier est vraiment efface du
    // stockage : ce n'est pas la colonne qu'on oublie, c'est l'image qui
    // disparaît, et elle ne revient pas.
    final sur = await showDialog<bool>(
      context: context,
      builder: (contexte) => AlertDialog(
        title: const Text('Retirer votre photo ?'),
        content: const Text(
          'Elle sera définitivement supprimée. Vous pourrez en envoyer une '
          'autre à tout moment.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(contexte).pop(false),
            child: const Text('Garder'),
          ),
          TextButton(
            onPressed: () => Navigator.of(contexte).pop(true),
            child: const Text('Retirer'),
          ),
        ],
      ),
    );

    if (!(sur ?? false) || !mounted) return;

    setState(() => _envoi = true);

    try {
      await ref.read(profilRepositoryProvider).supprimerPhoto();
      ref.invalidate(profilProvider);
    } on ApiException catch (erreur) {
      _dire(erreur.message);
    } on OfflineException {
      _dire("Pas de réseau. La photo n'a pas été retirée.");
    } on Object {
      _dire('La suppression a échoué.');
    } finally {
      if (mounted) setState(() => _envoi = false);
    }
  }

  void _dire(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(message)));
  }
}

enum _Geste { appareil, galerie, retirer }

class _Initiales extends StatelessWidget {
  const _Initiales({required this.nom});

  final String nom;

  @override
  Widget build(BuildContext context) => Center(
        child: Text(
          PhotoProfil.initiales(nom),
          style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                color: HbaColors.primaryInk,
                fontWeight: FontWeight.w600,
              ),
        ),
      );
}
