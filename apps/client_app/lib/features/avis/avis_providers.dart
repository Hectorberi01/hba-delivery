import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
import 'avis_models.dart';
import 'avis_repository.dart';

final avisRepositoryProvider = Provider<AvisRepository>(
  (ref) => AvisRepository(ref.watch(apiClientProvider)),
);

/// Les missions que ce client peut encore noter.
///
/// IL N'EST LU QUE PAR L'ACCUEIL, ET SEULEMENT AU REPOS. Le demander pendant
/// qu'une course roule ferait payer des donnees pour une question qui ne se
/// pose pas encore : une mission en cours n'est pas notable.
final avisEnAttenteProvider = FutureProvider<List<AvisEnAttente>>(
  (ref) => ref.watch(avisRepositoryProvider).enAttente(),
);

/// Les missions pour lesquelles l'ecran de notation a DEJA ete propose.
///
/// SANS ELLE, L'ACCUEIL ROUVRIRAIT L'ECRAN EN BOUCLE. Passer la notation la
/// laisse « en attente » cote service — c'est voulu, une note se pose quand on
/// veut — donc la liste la rend encore au battement suivant, et l'ecran
/// reviendrait a chaque construction de l'accueil.
///
/// ELLE NE SURVIT PAS A LA FERMETURE DE L'APPLICATION, et c'est le compromis
/// assume : passer veut dire « pas maintenant », pas « jamais ». Le client le
/// reverra au prochain demarrage, une fois, et pourra encore passer.
final avisDejaProposesProvider = StateProvider<Set<String>>((ref) => const {});
