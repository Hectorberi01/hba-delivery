import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import '../auth/session_controller.dart';
import 'profil_providers.dart';

/// Ouvre le formulaire de modification du profil.
///
/// UNE FEUILLE DU BAS, PAS UNE PAGE. Le client modifie deux champs et revient :
/// une page entiere lui ferait quitter son profil pour y revenir, et
/// l'obligerait a retrouver ou il en etait. La feuille laisse l'ecran derriere,
/// visible et intact.
Future<void> ouvrirFormulaireProfil(BuildContext context, Profil profil) {
  return showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    backgroundColor: HbaColors.surface,
    shape: const RoundedRectangleBorder(
      borderRadius: BorderRadius.vertical(top: Radius.circular(HbaRadius.card)),
    ),
    builder: (_) => FormulaireProfil(profil: profil),
  );
}

/// Le formulaire : nom complet, telephone verrouille, courriel.
///
/// UN SEUL CHAMP DE NOM, ET C'EST UNE DECISION, PAS UN RACCOURCI. Directory ne
/// porte qu'un « display_name », et le jeton qu'un « name ». Scinder en nom et
/// prenom demanderait une migration, un changement de contrat et une regle pour
/// couper les fiches existantes — mais surtout, au Benin l'ordre des deux varie
/// selon les usages, et deux champs imposent un ordre qui trahit la moitie des
/// gens. Un champ unique laisse chacun ecrire son nom comme il se presente.
///
/// LE TELEPHONE EST AFFICHE ET VERROUILLE, PAS CACHE. C'est l'identifiant du
/// compte : le montrer rassure — on voit a quel numero on est connecte — et le
/// cadenas dit pourquoi il ne se modifie pas, la ou son absence laisserait
/// croire a un oubli.
class FormulaireProfil extends ConsumerStatefulWidget {
  const FormulaireProfil({required this.profil, super.key});

  final Profil profil;

  @override
  ConsumerState<FormulaireProfil> createState() => _FormulaireProfilState();
}

class _FormulaireProfilState extends ConsumerState<FormulaireProfil> {
  late final TextEditingController _nom =
      TextEditingController(text: widget.profil.nom);
  late final TextEditingController _courriel =
      TextEditingController(text: widget.profil.courriel);

  // UN CONTROLEUR, MEME POUR UN CHAMP DESACTIVE. En construire un dans build()
  // en creerait un nouveau a chaque reconstruction, sans jamais liberer le
  // precedent : une fuite discrete, et le genre de chose qu'un champ en
  // lecture seule ne fait pas soupconner.
  late final TextEditingController _telephone =
      TextEditingController(text: widget.profil.telephone);

  bool _occupe = false;
  String? _erreur;

  @override
  void dispose() {
    _nom.dispose();
    _courriel.dispose();
    _telephone.dispose();
    super.dispose();
  }

  Future<void> _enregistrer() async {
    final nom = _nom.text.trim();

    if (nom.isEmpty) {
      setState(() => _erreur = 'Votre nom est nécessaire pour les livraisons.');
      return;
    }

    setState(() {
      _occupe = true;
      _erreur = null;
    });

    try {
      final enregistre = await ref.read(profilRepositoryProvider).enregistrer(
            nom: nom,
            courriel: _courriel.text.trim(),
          );

      ref.invalidate(profilProvider);

      // LES DEUX COPIES DU NOM SONT REMISES D'ACCORD ICI. La session en garde
      // une, lue au demarrage ; sans cette ligne elle resterait sur l'ancien
      // nom jusqu'a la prochaine ouverture de l'application.
      ref.read(sessionProvider.notifier).renommer(enregistre.nom);

      if (!mounted) return;
      Navigator.of(context).pop();
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Profil enregistré.')),
      );
    } on OfflineException {
      if (mounted) setState(() => _erreur = 'Pas de réseau. Réessayez.');
    } on ApiException catch (erreur) {
      if (mounted) setState(() => _erreur = erreur.message);
    } finally {
      if (mounted) setState(() => _occupe = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      // LE CLAVIER EST PRIS EN COMPTE, SINON IL COUVRE LE BOUTON. « Enregistrer »
      // sous un clavier ouvert est un formulaire qu'on ne peut pas valider sans
      // le refermer d'abord, et beaucoup concluent que le bouton ne marche pas.
      padding: EdgeInsets.only(
        left: HbaSpacing.gutter,
        right: HbaSpacing.gutter,
        top: HbaSpacing.md,
        bottom: MediaQuery.viewInsetsOf(context).bottom + HbaSpacing.lg,
      ),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Center(
              child: Container(
                height: 4,
                width: 44,
                decoration: BoxDecoration(
                  color: HbaColors.border,
                  borderRadius: BorderRadius.circular(HbaRadius.chip),
                ),
              ),
            ),
            const SizedBox(height: HbaSpacing.lg),

            Row(
              children: [
                Expanded(
                  child: Text(
                    'Modifier mes informations',
                    style: theme.textTheme.titleLarge,
                  ),
                ),
                IconButton(
                  onPressed: () => Navigator.of(context).pop(),
                  icon: const Icon(Icons.close),
                  color: HbaColors.inkMuted,
                  tooltip: 'Fermer',
                ),
              ],
            ),
            const SizedBox(height: HbaSpacing.lg),

            TextField(
              controller: _nom,
              textCapitalization: TextCapitalization.words,
              enabled: !_occupe,
              decoration: const InputDecoration(
                labelText: 'Nom complet',
                hintText: 'Celui que verra le livreur',
              ),
            ),
            const SizedBox(height: HbaSpacing.md),

            TextField(
              controller: _telephone,
              enabled: false,
              decoration: const InputDecoration(
                labelText: 'Téléphone',
                helperText: "Non modifiable : c'est l'identifiant de votre "
                    'compte.',
                helperMaxLines: 2,
                suffixIcon: Icon(Icons.lock_outline, size: 18),
              ),
            ),
            const SizedBox(height: HbaSpacing.md),

            TextField(
              controller: _courriel,
              keyboardType: TextInputType.emailAddress,
              autocorrect: false,
              enabled: !_occupe,
              decoration: const InputDecoration(
                labelText: 'Courriel',
                hintText: 'Ex : h.beri@email.com',
                // CE QUE LE COURRIEL SERT AUJOURD'HUI, ET RIEN DE PLUS.
                helperText: 'Facultatif. Sert au support pour vous joindre.',
                helperMaxLines: 2,
              ),
            ),

            if (_erreur != null) ...[
              const SizedBox(height: HbaSpacing.sm),
              Text(
                _erreur!,
                style:
                    theme.textTheme.bodyMedium?.copyWith(color: HbaColors.danger),
              ),
            ],

            const SizedBox(height: HbaSpacing.lg),
            HbaButton(
              label: 'Enregistrer les modifications',
              busy: _occupe,
              onPressed: _occupe ? null : _enregistrer,
            ),
            const SizedBox(height: HbaSpacing.sm),
            TextButton(
              onPressed: _occupe ? null : () => Navigator.of(context).pop(),
              child: const Text('Annuler'),
            ),
          ],
        ),
      ),
    );
  }
}
