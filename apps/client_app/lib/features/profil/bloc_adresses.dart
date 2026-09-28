import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import '../deliveries/map_picker_screen.dart';
import '../deliveries/point_field.dart';
import 'adresses.dart';

/// Les adresses favorites du client.
///
/// A QUOI ELLES SERVENT VRAIMENT. Pas a decorer un profil : a eviter de reposer
/// un point sur une carte a chaque commande. « Maison » et « bureau » couvrent
/// l'essentiel des courses d'un particulier, et les resaisir chaque fois est le
/// genre de friction qui fait abandonner un formulaire.
///
/// LE DRAPEAU « PAR DEFAUT » N'EST PAS CALCULE ICI. Poser une nouvelle adresse
/// par defaut retire le drapeau de l'ancienne, et c'est le service qui le
/// decide : les quatre routes rendent donc la LISTE ENTIERE, jamais l'element
/// touche. L'application se contente de la remplacer.
class BlocAdresses extends ConsumerWidget {
  const BlocAdresses({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final async = ref.watch(adressesProvider);

    return async.when(
      loading: () => const HbaCard(
        padding: EdgeInsets.all(HbaSpacing.xl),
        child: Center(child: CircularProgressIndicator()),
      ),

      // L'ECHEC EST DISCRET ICI, A DESSEIN : les adresses sont un confort, pas
      // le contenu de l'ecran. Une carte d'erreur pleine largeur donnerait a un
      // agrement le poids d'une panne.
      error: (_, __) => HbaCard(
        padding: const EdgeInsets.all(HbaSpacing.lg),
        child: Text(
          'Adresses indisponibles.',
          style:
              theme.textTheme.bodyMedium?.copyWith(color: HbaColors.inkMuted),
        ),
      ),

      data: (adresses) => HbaCard(
        padding: const EdgeInsets.all(HbaSpacing.lg),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (adresses.isEmpty)
              Padding(
                padding: const EdgeInsets.only(bottom: HbaSpacing.md),
                child: Text(
                  "Aucune adresse enregistrée pour l'instant.",
                  textAlign: TextAlign.center,
                  style: theme.textTheme.bodyMedium
                      ?.copyWith(color: HbaColors.inkMuted),
                ),
              )
            else
              for (final adresse in adresses) ...[
                _Carte(
                  adresse: adresse,
                  onSupprimer: () => _supprimer(context, ref, adresse),
                ),
                const SizedBox(height: HbaSpacing.sm),
              ],

            // LE BOUTON EST DANS LA CARTE, PAS DANS L'EN-TETE DE SECTION. Un
            // « + Ajouter » perdu a cote d'un titre se confond avec un
            // ornement ; pose sous la liste qu'il complete, il se lit comme la
            // suite naturelle de ce qu'on vient de parcourir.
            Align(
              alignment: Alignment.center,
              child: OutlinedButton.icon(
                onPressed: () => _ajouter(context, ref),
                icon: const Icon(Icons.add, size: 18),
                label: const Text('Ajouter une adresse'),
                style: OutlinedButton.styleFrom(
                  foregroundColor: HbaColors.primary,
                  side: const BorderSide(color: HbaColors.primary),
                  shape: const StadiumBorder(),
                  padding: const EdgeInsets.symmetric(
                    horizontal: HbaSpacing.lg,
                    vertical: HbaSpacing.sm,
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Future<void> _ajouter(BuildContext context, WidgetRef ref) async {
    // LE POINT D'ABORD, LE NOM ENSUITE. Demander « comment appelez-vous cette
    // adresse ? » avant de savoir de quelle adresse il s'agit oblige le client
    // a garder la reponse en tete pendant qu'il cherche sur la carte.
    final point = await Navigator.of(context).push<PickedPoint>(
      MaterialPageRoute(
        builder: (_) => const MapPickerScreen(title: 'Choisir le point'),
      ),
    );

    if (point == null || !context.mounted) return;

    final saisie = await showDialog<(String, String, bool)>(
      context: context,
      builder: (_) => const _Formulaire(),
    );

    if (saisie == null || !context.mounted) return;

    try {
      await ref.read(adressesRepositoryProvider).ajouter(
            libelle: saisie.$1,
            repere: saisie.$2,
            latitude: point.latitude,
            longitude: point.longitude,
            parDefaut: saisie.$3,
          );
      ref.invalidate(adressesProvider);
    } on Object catch (erreur) {
      if (!context.mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            erreur is ApiException ? erreur.message : 'Enregistrement impossible.',
          ),
        ),
      );
    }
  }

  /// SUPPRIMER SE CONFIRME. Une adresse se reconstruit en reposant un point sur
  /// une carte — ce n'est pas grave, mais c'est agacant, et un appui de trop
  /// sur une petite icone est vite arrive.
  Future<void> _supprimer(
    BuildContext context,
    WidgetRef ref,
    AdresseFavorite adresse,
  ) async {
    final oui = await showDialog<bool>(
      context: context,
      builder: (contexte) => AlertDialog(
        title: Text('Supprimer « ${adresse.libelle} » ?'),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(contexte).pop(false),
            child: const Text('Garder'),
          ),
          TextButton(
            onPressed: () => Navigator.of(contexte).pop(true),
            child: const Text('Supprimer'),
          ),
        ],
      ),
    );

    if (!(oui ?? false) || !context.mounted) return;

    try {
      await ref.read(adressesRepositoryProvider).supprimer(adresse.id);
      ref.invalidate(adressesProvider);
    } on Object {
      if (!context.mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Suppression impossible.')),
      );
    }
  }
}

class _Carte extends StatelessWidget {
  const _Carte({required this.adresse, required this.onSupprimer});

  final AdresseFavorite adresse;
  final VoidCallback onSupprimer;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Row(
        children: [
          Icon(
            adresse.parDefaut ? Icons.star : Icons.place_outlined,
            color: adresse.parDefaut ? HbaColors.primary : HbaColors.inkMuted,
          ),
          const SizedBox(width: HbaSpacing.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  adresse.libelle.isEmpty ? 'Sans nom' : adresse.libelle,
                  style: theme.textTheme.titleMedium,
                ),
                const SizedBox(height: 2),
                Text(
                  adresse.repere,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodyMedium
                      ?.copyWith(color: HbaColors.inkMuted),
                ),
              ],
            ),
          ),
          IconButton(
            onPressed: onSupprimer,
            icon: const Icon(Icons.delete_outline),
            tooltip: 'Supprimer',
            color: HbaColors.inkFaint,
          ),
        ],
      ),
    );
  }
}

/// Le nom et le repère, une fois le point choisi.
class _Formulaire extends StatefulWidget {
  const _Formulaire();

  @override
  State<_Formulaire> createState() => _FormulaireState();
}

class _FormulaireState extends State<_Formulaire> {
  final _libelle = TextEditingController();
  final _repere = TextEditingController();
  bool _parDefaut = false;

  // LE REPERE EST OBLIGATOIRE, ET L'ECRAN DOIT LE SAVOIR AVANT LE SERVICE.
  //
  // Directory refuse une adresse sans repere ecrit (MISSING_LANDMARK) : ce
  // n'est pas une politesse d'interface, c'est le domaine. Laisser le bouton
  // actif faisait partir une requete condamnee, et rendait l'echec en message
  // rouge apres coup — pour une saisie que l'ecran pouvait verifier seul.
  //
  // LE NOM AUSSI EST EXIGE : une adresse « sans nom » ne se retrouve pas dans
  // une liste, et c'est precisement a cela qu'elle sert.
  @override
  void initState() {
    super.initState();
    _libelle.addListener(_rafraichir);
    _repere.addListener(_rafraichir);
  }

  void _rafraichir() => setState(() {});

  bool get _complet =>
      _libelle.text.trim().isNotEmpty && _repere.text.trim().isNotEmpty;

  @override
  void dispose() {
    _libelle.removeListener(_rafraichir);
    _repere.removeListener(_rafraichir);
    _libelle.dispose();
    _repere.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
        title: const Text('Nommer cette adresse'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextField(
              controller: _libelle,
              autofocus: true,
              decoration: const InputDecoration(
                labelText: 'Nom',
                hintText: 'maison, bureau, chez maman',
              ),
            ),
            const SizedBox(height: HbaSpacing.md),
            TextField(
              controller: _repere,
              decoration: const InputDecoration(
                labelText: 'Repère',
                // LE REPERE VAUT SOUVENT MIEUX QUE LE POINT. Il n'y a pas de
                // numero de rue a lire sur les maisons : c'est la phrase qui
                // conduit le livreur, pas les coordonnees.
                hintText: 'Carré 442, en face de la pharmacie',
                helperText: 'Obligatoire : c\'est ce qui guide le livreur.',
                helperMaxLines: 2,
              ),
            ),
            const SizedBox(height: HbaSpacing.sm),
            CheckboxListTile(
              value: _parDefaut,
              onChanged: (v) => setState(() => _parDefaut = v ?? false),
              title: const Text('Adresse par défaut'),
              contentPadding: EdgeInsets.zero,
              controlAffinity: ListTileControlAffinity.leading,
            ),
          ],
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(context).pop(),
            child: const Text('Annuler'),
          ),
          TextButton(
            onPressed: _complet
                ? () => Navigator.of(context).pop(
                      (_libelle.text.trim(), _repere.text.trim(), _parDefaut),
                    )
                : null,
            child: const Text('Enregistrer'),
          ),
        ],
      );
}
