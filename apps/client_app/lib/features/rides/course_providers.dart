import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
import 'course_models.dart';
import 'course_repository.dart';

final courseRepositoryProvider = Provider<CourseRepository>(
  (ref) => CourseRepository(ref.watch(apiClientProvider)),
);

/// Les courses en cours du passager.
final coursesActivesProvider = FutureProvider<List<Course>>(
  (ref) => ref.watch(courseRepositoryProvider).actives(),
);

/// L'historique des courses de personnes du passager.
///
/// IL N'EXISTAIT NULLE PART AVANT LE 2 OCTOBRE 2026, et c'est ce trou qui a
/// emporte la decision du point 39. Une course active s'affichait sur l'accueil
/// et DISPARAISSAIT en se terminant : le passager n'avait aucun moyen de
/// retrouver un trajet de la veille, ni son prix, ni le nom du chauffeur.
///
/// LA ROUTE EXISTAIT POURTANT DEJA cote passerelle — « GET /client/v1/rides » —
/// et le depot ne l'appelait depuis aucun ecran.
///
/// LE TOTAL EST JETE ICI. La pagination n'est pas branchee : la premiere page
/// suffit au pilote, et un compteur qu'aucun ecran ne montre serait une promesse
/// de defilement infini qui n'existe pas.
final historiqueDesCoursesProvider = FutureProvider<List<Course>>(
  (ref) async => (await ref.watch(courseRepositoryProvider).historique()).$1,
);

/// Une course suivie. Rafraichie a la demande par l'ecran de suivi.
final courseProvider = FutureProvider.family<Course, String>(
  (ref, id) => ref.watch(courseRepositoryProvider).lire(id),
);

/// La course que le passager vient de payer, et dont il attend la confirmation.
///
/// MEME RAISON QUE POUR LES LIVRAISONS, et le meme laps de temps a couvrir :
/// entre le retour de la page de paiement et l'arrivee du webhook signe, la
/// course n'est pas encore partie en recherche. Sans ce marqueur, le passager
/// revenant de sa page de paiement ne verrait aucune trace de sa commande.
///
/// IL NE SURVIT PAS A LA FERMETURE DE L'APPLICATION, et ce n'est pas grave : au
/// prochain demarrage, soit le paiement est passe et la course est dans les
/// actives, soit il ne l'est pas et elle a ete abandonnee.
final courseEnAttenteDePaiementProvider = StateProvider<String?>((ref) => null);
