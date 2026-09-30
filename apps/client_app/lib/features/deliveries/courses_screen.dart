import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_ui/hba_ui.dart';

import 'delivery_providers.dart';
import 'models.dart';

/// Les courses du client : celles qui tournent, puis celles qui sont closes.
///
/// CET ECRAN EXISTAIT DEJA, EN BAS DE L'ACCUEIL. Il en sort parce que
/// l'accueil devient une carte : une liste et une carte plein ecran ne
/// cohabitent pas sans que l'une des deux soit a l'etroit.
///
/// DEUX SECTIONS, PAS DEUX ONGLETS. « En cours » tient en une ou deux lignes
/// la plupart du temps ; lui donner un onglet obligerait le client a chercher
/// derriere un deuxieme geste ce qu'il vient voir en premier.
class CoursesScreen extends ConsumerWidget {
  const CoursesScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final async = ref.watch(deliveriesProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Mes courses')),
      body: SafeArea(
        child: RefreshIndicator(
          onRefresh: () async => ref.invalidate(deliveriesProvider),
          child: async.when(
            loading: () => const Center(child: CircularProgressIndicator()),
            error: (_, __) => ListView(
              // LA LISTE RESTE UNE LISTE MEME VIDE, sans quoi le geste de
              // tirer vers le bas ne marche plus — et c'est precisement le
              // geste qu'on demande au client de faire pour reessayer.
              padding: const EdgeInsets.all(HbaSpacing.gutter),
              children: [
                HbaCard(
                  padding: const EdgeInsets.all(HbaSpacing.lg),
                  child: Text(
                    'Impossible de charger vos courses. Tirez vers le bas '
                    'pour réessayer.',
                    style: theme.textTheme.bodyMedium,
                  ),
                ),
              ],
            ),
            data: (courses) => _Liste(courses: courses),
          ),
        ),
      ),
    );
  }
}

class _Liste extends StatelessWidget {
  const _Liste({required this.courses});

  final List<Delivery> courses;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    if (courses.isEmpty) {
      return ListView(
        padding: const EdgeInsets.all(HbaSpacing.gutter),
        children: [
          const SizedBox(height: HbaSpacing.xxl),
          const Icon(Icons.local_shipping_outlined,
              size: 48, color: HbaColors.inkFaint),
          const SizedBox(height: HbaSpacing.md),
          Text(
            "Vous n'avez encore envoyé aucun colis.",
            textAlign: TextAlign.center,
            style: theme.textTheme.titleMedium,
          ),
          const SizedBox(height: HbaSpacing.xs),
          Text(
            "Vos courses s'afficheront ici, en cours puis terminées.",
            textAlign: TextAlign.center,
            style: theme.textTheme.bodyMedium
                ?.copyWith(color: HbaColors.inkMuted),
          ),
        ],
      );
    }

    final enCours = courses.where((c) => !c.status.isClosed).toList();
    final closes = courses.where((c) => c.status.isClosed).toList();

    return ListView(
      padding: const EdgeInsets.all(HbaSpacing.gutter),
      children: [
        if (enCours.isNotEmpty) ...[
          Text('EN COURS', style: theme.textTheme.bodySmall),
          const SizedBox(height: HbaSpacing.sm),
          for (final course in enCours) ...[
            CarteCourse(course: course, enAvant: true),
            const SizedBox(height: HbaSpacing.sm),
          ],
          const SizedBox(height: HbaSpacing.md),
        ],
        if (closes.isNotEmpty) ...[
          Text('TERMINÉES', style: theme.textTheme.bodySmall),
          const SizedBox(height: HbaSpacing.sm),
          for (final course in closes) ...[
            CarteCourse(course: course, enAvant: false),
            const SizedBox(height: HbaSpacing.sm),
          ],
        ],
      ],
    );
  }
}

/// Une course dans une liste.
///
/// PUBLIQUE PARCE QUE DEUX ECRANS L'UTILISENT : cette liste, et l'accueil, qui
/// montre la course en cours par-dessus la carte. La dupliquer ferait diverger
/// les deux le jour ou l'une des deux change.
class CarteCourse extends StatelessWidget {
  const CarteCourse({required this.course, required this.enAvant, super.key});

  final Delivery course;
  final bool enAvant;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      highlighted: enAvant,
      onTap: () => context.push('/suivi/${course.id}'),
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // LE STATUT PASSE EN PREMIERE LIGNE, avec la reference et le prix.
          //
          // Il etait relegue en bas a droite, sous le montant. Or c'est la
          // seule chose que le client vient chercher : il connait deja sa
          // reference et son prix. Le classement d'une liste se lit de haut en
          // bas et de gauche a droite ; ce qui compte se met la ou l'oeil se
          // pose, pas dans le coin qui reste.
          Row(
            children: [
              Expanded(
                child: HbaChip(
                  label: course.status.label,
                  tone: _ton(course.status),
                ),
              ),
              const SizedBox(width: HbaSpacing.sm),
              Text(Xof.format(course.totalXof),
                  style: theme.textTheme.titleMedium),
            ],
          ),
          const SizedBox(height: HbaSpacing.md),
          _Ligne(
            icone: Icons.place_outlined,
            texte: course.dropoffLandmark.isEmpty
                ? 'Destination non precisee'
                : course.dropoffLandmark,
            estompe: course.dropoffLandmark.isEmpty,
          ),
          const SizedBox(height: HbaSpacing.xs),
          _Ligne(
            icone: Icons.tag,
            texte: course.reference,
            estompe: true,
          ),
        ],
      ),
    );
  }

  /// LA COULEUR DIT L'ISSUE, PAS L'ETAPE. Vert : c'est arrive. Rouge : c'est
  /// perdu, et le client doit le voir sans lire. Orange : c'est en cours, quel
  /// que soit le cran — distinguer huit crans par huit teintes ne ferait
  /// qu'obliger a apprendre un code.
  static HbaChipTone _ton(DeliveryStatus statut) => switch (statut) {
        DeliveryStatus.delivered => HbaChipTone.success,
        DeliveryStatus.cancelled ||
        DeliveryStatus.failed ||
        DeliveryStatus.noDriverFound ||
        DeliveryStatus.paymentFailed =>
          HbaChipTone.danger,
        DeliveryStatus.unknown => HbaChipTone.neutral,
        _ => HbaChipTone.primary,
      };
}

/// Une ligne d'appoint dans la carte d'une course.
class _Ligne extends StatelessWidget {
  const _Ligne({
    required this.icone,
    required this.texte,
    required this.estompe,
  });

  final IconData icone;
  final String texte;
  final bool estompe;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final couleur = estompe ? HbaColors.inkFaint : HbaColors.inkMuted;

    return Row(
      children: [
        Icon(icone, size: 16, color: couleur),
        const SizedBox(width: HbaSpacing.sm),
        Expanded(
          child: Text(
            texte,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.bodyMedium?.copyWith(color: couleur),
          ),
        ),
      ],
    );
  }
}
