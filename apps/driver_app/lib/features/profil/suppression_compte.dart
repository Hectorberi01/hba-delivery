import 'package:flutter/material.dart';
import 'package:hba_ui/hba_ui.dart';
import 'package:url_launcher/url_launcher.dart';

import 'reglages_app.dart';

/// Suppression du compte.
///
/// GOOGLE PLAY ET APPLE L'EXIGENT des qu'une application permet de creer un
/// compte : il doit exister, DANS l'application, un chemin pour demander la
/// suppression. Sans lui, la publication est refusee.
///
/// AUCUNE ROUTE NE SUPPRIME UN COMPTE AUJOURD'HUI, et ce n'est pas qu'un
/// manque de code. Trois questions n'ont pas de reponse dans le referentiel :
/// que devient l'historique des courses d'un livreur parti — les commercants
/// et les clients y figurent aussi ; que devient une course en cours ; et
/// combien de temps les pieces KYC doivent-elles etre conservees pour
/// repondre a un litige. Les deux magasins acceptent un chemin de DEMANDE
/// tant que la suppression effective suit ; c'est ce que fait cet ecran.
///
/// IL NE PROMET DONC RIEN QU'IL NE TIENNE : il dit que la demande part vers
/// une personne, pas que le compte disparaitra en appuyant.
class SuppressionCompteScreen extends StatelessWidget {
  const SuppressionCompteScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(title: const Text('Supprimer mon compte')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(HbaSpacing.gutter),
          children: [
            HbaCard(
              padding: const EdgeInsets.all(HbaSpacing.lg),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text('Ce qui sera supprime', style: theme.textTheme.titleMedium),
                  const SizedBox(height: HbaSpacing.sm),
                  Text(
                    'Votre compte, votre profil livreur et vos pièces '
                    'justificatives. Vous ne recevrez plus aucune offre et '
                    'vous ne pourrez plus vous connecter.',
                    style: theme.textTheme.bodyMedium,
                  ),
                  const SizedBox(height: HbaSpacing.md),
                  Text('Ce qui sera conserve', style: theme.textTheme.titleMedium),
                  const SizedBox(height: HbaSpacing.sm),
                  Text(
                    'Les courses déjà effectuées restent dans l\'historique '
                    'de HBA : elles concernent aussi les clients et les '
                    'commerçants, et servent en cas de litige. Votre nom en '
                    'est retiré.',
                    style: theme.textTheme.bodyMedium,
                  ),
                ],
              ),
            ),
            const SizedBox(height: HbaSpacing.md),
            HbaCard(
              padding: const EdgeInsets.all(HbaSpacing.lg),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const HbaChip(
                    label: 'DEMANDE, PAS SUPPRESSION IMMÉDIATE',
                    tone: HbaChipTone.warning,
                  ),
                  const SizedBox(height: HbaSpacing.md),
                  Text(
                    'Votre demande part vers l\'équipe HBA, qui la traite et '
                    'vous confirme la suppression. Une course en cours doit '
                    'être terminée avant.',
                    style: theme.textTheme.bodyMedium,
                  ),
                ],
              ),
            ),
            const SizedBox(height: HbaSpacing.lg),
            if (Reglages.aSupport)
              HbaButton(
                label: 'Demander la suppression',
                tone: HbaButtonTone.danger,
                icon: Icons.delete_outline,
                onPressed: () => _demander(context),
              )
            else
              HbaCard(
                padding: const EdgeInsets.all(HbaSpacing.lg),
                child: Text(
                  'Aucun contact HBA n\'est configure dans cette version de '
                  'l\'application. Passez par le site hbatechettrade.com.',
                  style: theme.textTheme.bodyMedium,
                ),
              ),
            const SizedBox(height: HbaSpacing.xl),
          ],
        ),
      ),
    );
  }

  /// Une confirmation avant d'ouvrir le canal : on ne declenche pas une
  /// demande de suppression sur un appui unique.
  Future<void> _demander(BuildContext context) async {
    final confirme = await showDialog<bool>(
      context: context,
      builder: (dialogue) => AlertDialog(
        title: const Text('Confirmer la demande'),
        content: const Text(
          'Vous allez contacter HBA pour demander la suppression de votre '
          'compte. Continuer ?',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogue).pop(false),
            child: const Text('Annuler'),
          ),
          TextButton(
            onPressed: () => Navigator.of(dialogue).pop(true),
            child: const Text('Continuer'),
          ),
        ],
      ),
    );

    if (confirme != true) return;

    // LA DEMANDE PART PAR COURRIEL, PLUS PAR WHATSAPP (27 septembre 2026).
    //
    // CE CHEMIN-LA EST CELUI QUI AVAIT LE PLUS BESOIN DU CHANGEMENT : une
    // demande de suppression de compte doit laisser une trace datee, qu'on
    // puisse retrouver et transmettre. Dans une conversation WhatsApp, elle
    // vit dans le telephone d'une personne et disparait avec lui.
    //
    // LE TELEPHONE RESTE LE SECOURS quand aucune adresse n'est configuree :
    // mieux vaut un appel qu'aucun recours.
    final cible = Reglages.supportEmail.isNotEmpty
        ? Uri.parse(
            'mailto:${Reglages.supportEmail}'
            '?subject=${Uri.encodeComponent('Suppression de compte livreur')}'
            '&body=${Uri.encodeComponent('Bonjour, je demande la suppression de mon compte livreur HBA.')}',
          )
        : Uri.parse('tel:${Reglages.supportTelephone}');

    try {
      await launchUrl(cible, mode: LaunchMode.externalApplication);
    } on Object {
      if (context.mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Impossible d\'ouvrir le contact.')),
        );
      }
    }
  }
}
