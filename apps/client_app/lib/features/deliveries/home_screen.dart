import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_ui/hba_ui.dart';

import '../auth/session_controller.dart';
import 'delivery_providers.dart';
import 'models.dart';

/// Accueil : la course en cours s'il y en a une, puis l'historique.
class HomeScreen extends ConsumerWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final session = ref.watch(sessionProvider);
    final name = session is SessionSignedIn ? session.displayName : '';
    final async = ref.watch(deliveriesProvider);

    return Scaffold(
      body: SafeArea(
        child: RefreshIndicator(
          onRefresh: () async => ref.invalidate(deliveriesProvider),
          child: ListView(
            padding: const EdgeInsets.all(HbaSpacing.gutter),
            children: [
              Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('Bonjour', style: theme.textTheme.bodyMedium),
                        Text(
                          name.isEmpty ? 'Bienvenue' : name,
                          style: theme.textTheme.headlineMedium,
                        ),
                      ],
                    ),
                  ),
                  IconButton(
                    onPressed: () => ref.read(sessionProvider.notifier).signOut(),
                    icon: const Icon(Icons.logout),
                    color: HbaColors.inkMuted,
                  ),
                ],
              ),
              const SizedBox(height: HbaSpacing.lg),
              HbaButton(
                label: 'Envoyer un colis',
                icon: Icons.add,
                onPressed: () => context.push('/nouvelle'),
              ),
              const SizedBox(height: HbaSpacing.lg),
              async.when(
                loading: () => const Padding(
                  padding: EdgeInsets.all(HbaSpacing.xl),
                  child: Center(child: CircularProgressIndicator()),
                ),
                error: (_, __) => HbaCard(
                  padding: const EdgeInsets.all(HbaSpacing.lg),
                  child: Text(
                    "Impossible de charger vos livraisons. Tirez vers le bas "
                    "pour reessayer.",
                    style: theme.textTheme.bodyMedium,
                  ),
                ),
                data: (deliveries) => _List(deliveries: deliveries),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _List extends StatelessWidget {
  const _List({required this.deliveries});

  final List<Delivery> deliveries;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    if (deliveries.isEmpty) {
      return HbaCard(
        padding: const EdgeInsets.all(HbaSpacing.lg),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Aucune livraison', style: theme.textTheme.titleMedium),
            const SizedBox(height: HbaSpacing.xs),
            Text(
              "Vos envois apparaitront ici.",
              style: theme.textTheme.bodyMedium,
            ),
          ],
        ),
      );
    }

    final ongoing = deliveries.where((d) => !d.status.isClosed).toList();
    final past = deliveries.where((d) => d.status.isClosed).toList();

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (ongoing.isNotEmpty) ...[
          Text('EN COURS', style: theme.textTheme.bodySmall),
          const SizedBox(height: HbaSpacing.sm),
          for (final delivery in ongoing) ...[
            _Tile(delivery: delivery, highlighted: true),
            const SizedBox(height: HbaSpacing.sm),
          ],
          const SizedBox(height: HbaSpacing.md),
        ],
        if (past.isNotEmpty) ...[
          Text('HISTORIQUE', style: theme.textTheme.bodySmall),
          const SizedBox(height: HbaSpacing.sm),
          for (final delivery in past) ...[
            _Tile(delivery: delivery, highlighted: false),
            const SizedBox(height: HbaSpacing.sm),
          ],
        ],
      ],
    );
  }
}

class _Tile extends StatelessWidget {
  const _Tile({required this.delivery, required this.highlighted});

  final Delivery delivery;
  final bool highlighted;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      highlighted: highlighted,
      onTap: () => context.push('/suivi/${delivery.id}'),
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(delivery.reference, style: theme.textTheme.titleMedium),
                const SizedBox(height: 2),
                Text(
                  delivery.dropoffLandmark.isEmpty
                      ? delivery.status.label
                      : delivery.dropoffLandmark,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodyMedium,
                ),
              ],
            ),
          ),
          const SizedBox(width: HbaSpacing.sm),
          Column(
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              Text(Xof.format(delivery.totalXof),
                  style: theme.textTheme.titleMedium),
              const SizedBox(height: 4),
              HbaChip(
                label: delivery.status.label,
                tone: delivery.status == DeliveryStatus.delivered
                    ? HbaChipTone.success
                    : HbaChipTone.neutral,
              ),
            ],
          ),
        ],
      ),
    );
  }
}
