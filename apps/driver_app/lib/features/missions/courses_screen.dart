import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_ui/hba_ui.dart';

import '../../core/rafraichir.dart';

import 'mission_providers.dart';
import 'models.dart';

/// Historique des courses du livreur.
///
/// CE N'EST PAS TOUT SON HISTORIQUE, ET L'ECRAN LE DIT. Le BFF appelle
/// ListDeliveries avec PageSize = 25 et n'expose aucun jeton de page : au-dela
/// de vingt-cinq courses, les plus anciennes sont hors de portee. Laisser
/// croire le contraire ferait douter le livreur de ses propres gains.
final coursesProvider = FutureProvider.autoDispose<List<Mission>>(
  (ref) => ref.watch(missionRepositoryProvider).allMissions(),
);

class CoursesScreen extends ConsumerWidget {
  const CoursesScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final courses = ref.watch(coursesProvider);

    return Scaffold(
      body: SafeArea(
        child: RefreshIndicator(
          onRefresh: () => rafraichirDepuis(ref, coursesProvider.future),
          child: courses.when(
            loading: () => const Center(child: CircularProgressIndicator()),
            error: (_, __) => const _Message(
              titre: 'Historique indisponible',
              detail: 'Le service ne répond pas. Tirez vers le bas pour '
                  'reessayer.',
            ),
            data: (liste) => ListView(
              padding: const EdgeInsets.all(HbaSpacing.gutter),
              children: [
                Text('Mes courses', style: theme.textTheme.headlineMedium),
                const SizedBox(height: HbaSpacing.md),
                if (liste.isEmpty)
                  const _Message(
                    titre: 'Aucune course',
                    detail: 'Vos courses apparaîtront ici des la première '
                        'acceptee.',
                  )
                else ...[
                  _Recapitulatif(courses: liste),
                  const SizedBox(height: HbaSpacing.md),
                  for (final course in liste) ...[
                    _LigneCourse(course: course),
                    const SizedBox(height: HbaSpacing.sm),
                  ],
                  const SizedBox(height: HbaSpacing.xs),
                  Text(
                    'Les vingt-cinq dernières courses. Au-delà, l\'historique '
                    'n\'est pas encore consultable depuis l\'application.',
                    style: theme.textTheme.bodySmall,
                  ),
                ],
                const SizedBox(height: HbaSpacing.xl),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// Deux chiffres, et rien de plus.
///
/// PAS DE TOTAL EN FRANCS ICI. Il porterait sur vingt-cinq courses et se
/// lirait comme un solde ; le livreur compterait dessus. L'onglet Gains dit
/// pourquoi aucun solde n'existe encore.
class _Recapitulatif extends StatelessWidget {
  const _Recapitulatif({required this.courses});

  final List<Mission> courses;

  @override
  Widget build(BuildContext context) {
    final livrees = courses.where((c) => c.status.estReussie).length;
    final closes = courses.where((c) => c.status.estClose).length;

    return HbaCard(
      padding: const EdgeInsets.symmetric(
        horizontal: HbaSpacing.lg,
        vertical: HbaSpacing.md,
      ),
      child: Row(
        children: [
          Expanded(
            child: _Chiffre(valeur: '${courses.length}', libelle: 'Affichées'),
          ),
          Container(width: 1, height: 34, color: HbaColors.border),
          Expanded(child: _Chiffre(valeur: '$livrees', libelle: 'Livrées')),
          Container(width: 1, height: 34, color: HbaColors.border),
          Expanded(
            child: _Chiffre(
              valeur: closes == 0 ? '—' : '${(livrees * 100 / closes).round()} %',
              libelle: closes == 0 ? 'Aucune close' : 'sur $closes closes',
            ),
          ),
        ],
      ),
    );
  }
}

class _Chiffre extends StatelessWidget {
  const _Chiffre({required this.valeur, required this.libelle});

  final String valeur;
  final String libelle;

  @override
  Widget build(BuildContext context) => Column(
        children: [
          Text(
            valeur,
            style: const TextStyle(
              fontSize: 22,
              fontWeight: FontWeight.w800,
              color: HbaColors.ink,
            ),
          ),
          const SizedBox(height: 2),
          Text(
            libelle,
            textAlign: TextAlign.center,
            style: const TextStyle(fontSize: 12, color: HbaColors.inkMuted),
          ),
        ],
      );
}

class _LigneCourse extends StatelessWidget {
  const _LigneCourse({required this.course});

  final Mission course;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    final ton = switch (course.status) {
      DeliveryStatus.delivered => HbaChipTone.success,
      DeliveryStatus.cancelled ||
      DeliveryStatus.failed ||
      DeliveryStatus.paymentFailed ||
      DeliveryStatus.noDriverFound =>
        HbaChipTone.danger,
      DeliveryStatus.unknown => HbaChipTone.neutral,
      _ => HbaChipTone.primary,
    };

    return HbaCard(
      highlighted: course.isOpen,
      padding: const EdgeInsets.all(HbaSpacing.md),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      _titre(course),
                      style: theme.textTheme.titleMedium,
                    ),
                    const SizedBox(height: 2),
                    Text(_date(course.dateAffichee), style: theme.textTheme.bodySmall),
                  ],
                ),
              ),
              HbaChip(label: course.status.libelle, tone: ton),
            ],
          ),
          if (course.pickup != null || course.dropoff != null) ...[
            const SizedBox(height: HbaSpacing.sm),
            Text(
              [
                course.pickup?.landmark,
                course.dropoff?.landmark,
              ].where((l) => l != null && l.isNotEmpty).join('  →  '),
              style: theme.textTheme.bodyMedium,
            ),
          ],

          // LE MONTANT NE S'AFFICHE QUE SUR UNE COURSE LIVREE. Sur une course
          // annulee, la remuneration figee dans la course n'a jamais ete due :
          // l'afficher promettrait de l'argent qui n'arrivera pas.
          if (course.status.estReussie && course.driverEarningXof > 0) ...[
            const SizedBox(height: HbaSpacing.sm),
            Text(
              Xof.format(course.driverEarningXof),
              style: const TextStyle(
                fontWeight: FontWeight.w800,
                color: HbaColors.ink,
              ),
            ),
          ],
          if (course.closureReason.isNotEmpty) ...[
            const SizedBox(height: HbaSpacing.xs),
            Text(course.closureReason, style: theme.textTheme.bodySmall),
          ],
        ],
      ),
    );
  }

  /// Reference si elle existe, sinon le debut de l'identifiant. Jamais vide :
  /// une ligne sans titre ne se distingue pas de la suivante.
  static String _titre(Mission course) {
    if (course.reference.isNotEmpty) return course.reference;
    if (course.id.isEmpty) return 'Course';
    return course.id.length <= 8 ? course.id : course.id.substring(0, 8);
  }

  static String _date(DateTime? value) {
    if (value == null) return 'Date inconnue';

    String deux(int n) => n.toString().padLeft(2, '0');
    return '${deux(value.day)}/${deux(value.month)} à ${deux(value.hour)}h${deux(value.minute)}';
  }
}

class _Message extends StatelessWidget {
  const _Message({required this.titre, required this.detail});

  final String titre;
  final String detail;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(titre, style: theme.textTheme.titleMedium),
          const SizedBox(height: HbaSpacing.xs),
          Text(detail, style: theme.textTheme.bodyMedium),
        ],
      ),
    );
  }
}
