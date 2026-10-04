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

/// La course que le client vient de payer, et dont il attend la confirmation.
///
/// POURQUOI L'ACCUEIL NE PEUT PAS LA TROUVER SEUL. Une course non payee ne
/// figure plus dans la liste rendue au client — le service la retire du
/// perimetre « customer » tant qu'elle est en PENDING_PAYMENT. C'est voulu :
/// une course que personne n'a payee n'est pas une course. Mais entre le retour
/// de la page de paiement et l'arrivee du webhook, il s'ecoule quelques
/// secondes pendant lesquelles le client ne verrait RIEN — ni sa commande, ni
/// la recherche. Ce marqueur porte l'identifiant pendant ce laps de temps ; la
/// fiche, elle, reste lisible par son proprietaire quel que soit son statut.
///
/// IL NE SURVIT PAS A LA FERMETURE DE L'APPLICATION, et ce n'est pas grave : au
/// prochain demarrage, soit le paiement est passe et la course est dans la
/// liste, soit il ne l'est pas et elle a ete abandonnee.
final courseEnAttenteProvider = StateProvider<String?>((ref) => null);

/// Une livraison suivie. Rafraichie a la demande par l'ecran de suivi.
final deliveryProvider = FutureProvider.family<Delivery, String>(
  (ref, id) => ref.watch(deliveryRepositoryProvider).byId(id),
);

/// La meme livraison, trace de l'itineraire compris.
///
/// UNE FAMILLE A PART ET NON UN PARAMETRE DE LA PREMIERE. Riverpod indexe une
/// famille par son argument : ajouter un drapeau au meme fournisseur ferait deux
/// entrees de cache pour une seule livraison, et l'ecran qui dessine la route
/// invaliderait celle du bandeau sans jamais la rafraichir. Deux familles
/// distinctes disent ce qu'elles sont.
///
/// IL N'EST REGARDE QUE PAR CE QUI DESSINE. Partout ailleurs, deliveryProvider
/// suffit et ne transporte aucune geometrie.
final livraisonAvecItineraireProvider = FutureProvider.family<Delivery, String>(
  (ref, id) =>
      ref.watch(deliveryRepositoryProvider).byId(id, avecItineraire: true),
);
