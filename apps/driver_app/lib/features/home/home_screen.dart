import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_ui/hba_ui.dart';

import '../auth/session_controller.dart';

/// Ecran d'accueil du livreur : en ligne ou hors ligne, et rien d'autre tant
/// qu'aucune offre n'arrive.
class HomeScreen extends ConsumerStatefulWidget {
  const HomeScreen({super.key});

  @override
  ConsumerState<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends ConsumerState<HomeScreen> {
  bool _online = false;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final session = ref.watch(sessionProvider);
    final approved = session is SessionSignedIn && session.kycApproved;
    final name = session is SessionSignedIn ? session.displayName : '';

    return Scaffold(
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(HbaSpacing.gutter),
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text('Bonjour', style: theme.textTheme.bodyMedium),
                      Text(
                        name.isEmpty ? 'Livreur' : name,
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
            if (!approved) const _KycPending(),
            if (!approved) const SizedBox(height: HbaSpacing.md),
            HbaCard(
              highlighted: _online,
              padding: const EdgeInsets.all(HbaSpacing.lg),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          _online ? 'Vous etes en ligne' : 'Vous etes hors ligne',
                          style: theme.textTheme.titleMedium,
                        ),
                      ),
                      Switch(
                        value: _online,
                        activeColor: HbaColors.primary,
                        onChanged: approved
                            ? (value) => setState(() => _online = value)
                            : null,
                      ),
                    ],
                  ),
                  const SizedBox(height: HbaSpacing.xs),
                  Text(
                    _online
                        ? 'Les offres de course arrivent ici. Gardez le telephone allume.'
                        : "Passez en ligne pour recevoir des offres.",
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
                  Text("Gains d'aujourd'hui", style: theme.textTheme.bodyMedium),
                  const SizedBox(height: HbaSpacing.xs),
                  // Placeholder assume : le service Driver n'existe pas encore.
                  Text(Xof.format(0), style: theme.textTheme.displaySmall),
                  const SizedBox(height: HbaSpacing.xs),
                  Text(
                    'Aucune course terminee aujourd hui.',
                    style: theme.textTheme.bodyMedium,
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Etat intermediaire : inscrit, mais pas encore autorise a travailler.
class _KycPending extends StatelessWidget {
  const _KycPending();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const HbaChip(label: 'DOSSIER EN COURS', tone: HbaChipTone.warning),
          const SizedBox(height: HbaSpacing.sm),
          Text('Votre compte est cree', style: theme.textTheme.titleMedium),
          const SizedBox(height: HbaSpacing.xs),
          Text(
            "Vous pourrez passer en ligne des que vos pieces auront ete verifiees. "
            "Nous vous previenons des que c'est fait.",
            style: theme.textTheme.bodyMedium,
          ),
        ],
      ),
    );
  }
}
