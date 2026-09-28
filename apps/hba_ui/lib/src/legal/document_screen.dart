import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';

import '../tokens.dart';
import '../widgets/hba_button.dart';
import '../widgets/hba_card.dart';
import '../widgets/hba_chip.dart';
import '../widgets/hba_relief.dart';
import 'document_legal.dart';

/// Affiche un document juridique.
///
/// UN SEUL ECRAN POUR LES DEUX TEXTES. Conditions et confidentialite ont la
/// meme forme — un chapeau, des sections, une version — et deux ecrans
/// jumeaux finiraient par diverger sur un detail de mise en page qui ferait
/// croire a une difference de nature.
class DocumentScreen extends StatelessWidget {
  const DocumentScreen({required this.document, this.enLigne, super.key});

  final DocumentLegal document;

  /// Adresse de la version publiee, quand elle est configuree.
  final String? enLigne;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(title: Text(document.titre)),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(HbaSpacing.gutter),
          children: [
            if (document.brouillon) ...[
              const _Brouillon(),
              const SizedBox(height: HbaSpacing.md),
            ],

            HbaCard(
              padding: const EdgeInsets.all(HbaSpacing.lg),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(document.chapeau, style: theme.textTheme.bodyMedium),
                  const SizedBox(height: HbaSpacing.md),
                  Row(
                    children: [
                      HbaChip(label: document.version),
                      const SizedBox(width: HbaSpacing.sm),
                      Expanded(
                        child: Text(
                          'Mise à jour : ${document.miseAJour}',
                          style: theme.textTheme.bodySmall,
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),

            for (final section in document.sections) ...[
              const SizedBox(height: HbaSpacing.md),
              _Section(section: section),
            ],

            if (enLigne != null && enLigne!.isNotEmpty) ...[
              const SizedBox(height: HbaSpacing.lg),
              HbaButton(
                label: 'Voir la version publiée',
                tone: HbaButtonTone.neutral,
                icon: Icons.open_in_new,
                onPressed: () async {
                  final uri = Uri.tryParse(enLigne!);
                  if (uri == null) return;

                  try {
                    await launchUrl(uri, mode: LaunchMode.externalApplication);
                  } on Object {
                    if (context.mounted) {
                      ScaffoldMessenger.of(context).showSnackBar(
                        const SnackBar(
                          content: Text('Impossible d\'ouvrir la page.'),
                        ),
                      );
                    }
                  }
                },
              ),
            ],

            const SizedBox(height: HbaSpacing.xl),
          ],
        ),
      ),
    );
  }
}

class _Section extends StatelessWidget {
  const _Section({required this.section});

  final SectionLegale section;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(section.titre, style: theme.textTheme.titleMedium),

          for (final paragraphe in section.paragraphes) ...[
            const SizedBox(height: HbaSpacing.sm),
            Text(paragraphe, style: theme.textTheme.bodyMedium),
          ],

          for (final point in section.points) ...[
            const SizedBox(height: HbaSpacing.sm),
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Padding(
                  padding: const EdgeInsets.only(top: 7),
                  child: Container(
                    height: 5,
                    width: 5,
                    decoration: const BoxDecoration(
                      color: HbaColors.primaryInk,
                      shape: BoxShape.circle,
                    ),
                  ),
                ),
                const SizedBox(width: HbaSpacing.sm),
                Expanded(child: Text(point, style: theme.textTheme.bodyMedium)),
              ],
            ),
          ],

          if (section.aTrancher != null) ...[
            const SizedBox(height: HbaSpacing.md),
            HbaCreux(
              padding: const EdgeInsets.all(HbaSpacing.md),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const HbaChip(
                    label: 'PAS ENCORE ARRÊTÉ',
                    tone: HbaChipTone.warning,
                  ),
                  const SizedBox(height: HbaSpacing.sm),
                  Text(section.aTrancher!, style: theme.textTheme.bodySmall),
                ],
              ),
            ),
          ],
        ],
      ),
    );
  }
}

/// Le bandeau de brouillon.
///
/// IL EST EN HAUT, ET IL NE SE REPLIE PAS. Un avertissement qu'on peut fermer
/// est un avertissement que personne ne lit deux fois — et celui-ci s'adresse
/// autant au livreur qu'a quiconque, chez HBA, ouvrirait l'application en
/// croyant le texte definitif.
class _Brouillon extends StatelessWidget {
  const _Brouillon();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      highlighted: true,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const HbaChip(label: 'TEXTE PROVISOIRE', tone: HbaChipTone.warning),
          const SizedBox(height: HbaSpacing.sm),
          Text(
            'Ce document décrit fidèlement ce que fait l\'application '
            'aujourd\'hui, mais il n\'a pas été relu par un juriste et il '
            'n\'engage pas encore HBA. Il sera remplace par la version '
            'definitive avant l\'ouverture du service.',
            style: theme.textTheme.bodyMedium,
          ),
        ],
      ),
    );
  }
}
