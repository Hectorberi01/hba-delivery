import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_ui/hba_ui.dart';

import '../../core/rafraichir.dart';
import 'reputation_models.dart';
import 'reputation_providers.dart';

/// Les avis recus, un par un, avec la moyenne en tete. Point 41.
///
/// UNE MOYENNE SANS DETAIL EST UNE SANCTION SANS MOTIF, et c'est la raison
/// d'etre de cet ecran : la carte du profil annonce un chiffre, celui-ci dit ce
/// qu'il y a derriere. Un livreur qui voit sa note baisser sans pouvoir lire
/// pourquoi n'a aucun moyen de corriger quoi que ce soit.
///
/// AUCUNE SANCTION N'EN DECOULE, ET LE DIRE FAIT PARTIE DE L'ECRAN. Une note
/// basse ne desactive personne et le dispatch ne lit pas les moyennes (point
/// 41) : sans cette phrase, un livreur a deux etoiles croit son compte menace
/// et arrete de travailler de lui-meme.
class ReputationScreen extends ConsumerWidget {
  const ReputationScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final note = ref.watch(reputationProvider);
    final avis = ref.watch(avisRecusProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Mes avis')),
      body: SafeArea(
        child: RefreshIndicator(
          onRefresh: () async {
            // LES DEUX ENSEMBLE : la moyenne vient de Rating comme les avis, et
            // ne relire que l'une afficherait un chiffre qui ne correspond plus
            // a la liste juste en dessous.
            await rafraichirDepuis(ref, reputationProvider.future);
            await rafraichirDepuis(ref, avisRecusProvider.future);
          },
          child: ListView(
            padding: const EdgeInsets.all(HbaSpacing.gutter),
            children: [
              _Moyenne(note: note.valueOrNull ?? Reputation.vide),
              const SizedBox(height: HbaSpacing.md),
              Text(
                "Une note basse ne désactive pas votre compte et n'influence "
                "pas l'attribution des courses.",
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: HbaColors.inkMuted),
              ),
              const SizedBox(height: HbaSpacing.lg),
              avis.when(
                loading: () => const Padding(
                  padding: EdgeInsets.symmetric(vertical: HbaSpacing.xxl),
                  child: Center(child: CircularProgressIndicator()),
                ),
                error: (_, __) => Text(
                  "Vos avis n'ont pas pu être lus. Tirez pour réessayer.",
                  style: theme.textTheme.bodyMedium,
                ),
                data: (liste) => liste.isEmpty
                    ? Text(
                        'Personne ne vous a encore noté.',
                        style: theme.textTheme.bodyMedium,
                      )
                    : Column(
                        children: [
                          for (final a in liste) ...[
                            _Avis(avis: a),
                            const SizedBox(height: HbaSpacing.sm),
                          ],
                        ],
                      ),
              ),
              const SizedBox(height: HbaSpacing.xl),
            ],
          ),
        ),
      ),
    );
  }
}

/// La moyenne, ou son absence.
class _Moyenne extends StatelessWidget {
  const _Moyenne({required this.note});

  final Reputation note;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                // « PAS ENCORE DE NOTE » N'EST PAS « ZERO ». Le service dit par
                // « assez » quand la moyenne tient debout ; afficher 2,0 sur un
                // seul avis serait plus faux que de n'afficher rien.
                Text(
                  note.assez
                      ? note.moyenne.toStringAsFixed(1).replaceAll('.', ',')
                      : 'Pas encore de note',
                  style: note.assez
                      ? theme.textTheme.headlineMedium
                      : theme.textTheme.titleMedium,
                ),
                const SizedBox(height: 2),
                Text(
                  switch (note.nombre) {
                    0 => 'Aucun avis reçu',
                    1 => '1 avis reçu',
                    final n => '$n avis reçus',
                  },
                  style: theme.textTheme.bodyMedium
                      ?.copyWith(color: HbaColors.inkMuted),
                ),
              ],
            ),
          ),
          if (note.assez)
            const Icon(Icons.star_rounded, size: 40, color: HbaColors.primary),
        ],
      ),
    );
  }
}

/// Un avis : des etoiles, une date, et le mot du client s'il y en a un.
class _Avis extends StatelessWidget {
  const _Avis({required this.avis});

  final AvisRecu avis;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              for (var n = 1; n <= 5; n++)
                Icon(
                  n <= avis.etoiles
                      ? Icons.star_rounded
                      : Icons.star_border_rounded,
                  size: 18,
                  color: n <= avis.etoiles
                      ? HbaColors.primary
                      : HbaColors.inkFaint,
                ),
              const Spacer(),

              // LA MISSION EST NOMMEE PAR SA NATURE, PAS PAR SON IDENTIFIANT.
              // Un UUID de trente-six caracteres n'aide personne a se souvenir
              // d'un trajet ; la date, si.
              Text(
                [
                  avis.estUneCourse ? 'Course' : 'Livraison',
                  if (_quand() case final String quand) quand,
                ].join(' · '),
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: HbaColors.inkMuted),
              ),
            ],
          ),
          if (avis.commentaire case final String mot) ...[
            const SizedBox(height: HbaSpacing.sm),
            Text(mot, style: theme.textTheme.bodyMedium),
          ],
        ],
      ),
    );
  }

  String? _quand() {
    final date = avis.deposeLe;
    if (date == null) return null;

    final jours = DateTime.now().difference(date).inDays;

    return switch (jours) {
      0 => "aujourd'hui",
      1 => 'hier',
      final n => 'il y a $n jours',
    };
  }
}
