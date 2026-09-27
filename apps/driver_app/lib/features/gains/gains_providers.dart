import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
import 'gains_repository.dart';
import 'models.dart';

final gainsRepositoryProvider = Provider<GainsRepository>(
  (ref) => GainsRepository(ref.watch(apiClientProvider)),
);

/// Le releve du livreur.
///
/// DEUX LECTURES SEPAREES, ET C'EST VOLONTAIRE. Le releve porte l'argent, les
/// demandes portent leur suivi : les fondre dans un seul appel obligerait a
/// tout relire pour rafraichir l'un des deux, et surtout ferait disparaitre le
/// solde de l'ecran pendant qu'on recharge une liste de demandes.
final releveProvider = FutureProvider.autoDispose<Releve>(
  (ref) => ref.watch(gainsRepositoryProvider).releve(),
);

final demandesProvider = FutureProvider.autoDispose<List<DemandeVersement>>(
  (ref) => ref.watch(gainsRepositoryProvider).demandes(),
);
