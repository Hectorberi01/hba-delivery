import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/providers.dart';
import 'delivery_repository.dart';
import 'models.dart';

final deliveryRepositoryProvider = Provider<DeliveryRepository>(
  (ref) => DeliveryRepository(ref.watch(apiClientProvider)),
);

/// Liste des livraisons du client.
final deliveriesProvider = FutureProvider<List<Delivery>>(
  (ref) => ref.watch(deliveryRepositoryProvider).list(),
);

/// Une livraison suivie. Rafraichie a la demande par l'ecran de suivi.
final deliveryProvider = FutureProvider.family<Delivery, String>(
  (ref, id) => ref.watch(deliveryRepositoryProvider).byId(id),
);
