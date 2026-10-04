import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
import 'reputation_models.dart';
import 'reputation_repository.dart';

final reputationRepositoryProvider = Provider<ReputationRepository>(
  (ref) => ReputationRepository(ref.watch(apiClientProvider)),
);

/// La note moyenne du livreur.
final reputationProvider = FutureProvider<Reputation>(
  (ref) => ref.watch(reputationRepositoryProvider).moyenne(),
);

/// Ses avis, un par un. Lus seulement par l'ecran qui les montre.
final avisRecusProvider = FutureProvider<List<AvisRecu>>(
  (ref) => ref.watch(reputationRepositoryProvider).avis(),
);
