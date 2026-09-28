import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_core/hba_core.dart';

import '../../core/providers.dart';

/// Les livreurs autour du client, EN COORDONNEES SEULES.
///
/// CE QUE CE FICHIER NE TRANSPORTE PAS, ET CE N'EST PAS UN OUBLI : ni
/// identifiant, ni nom, ni plaque, ni type de vehicule. La matrice de
/// visibilite du referentiel n'accorde au client l'identite du livreur qu'a
/// partir de DRIVER_ASSIGNED, et le point 24 des points a trancher — qui
/// ouvre les positions au client — n'a PAS leve cette ligne-la.
///
/// UN IDENTIFIANT STABLE SUFFIRAIT A ANNULER LA REGLE. Meme anonyme en
/// apparence, un identifiant permettrait de reconnaitre le meme livreur d'un
/// jour sur l'autre, de suivre ses horaires et ses trajets. C'est exactement ce
/// que la matrice refuse. Une liste de points n'a pas cette propriete, et c'est
/// la seule raison pour laquelle cette classe est un simple LatLng.
typedef LivreurProche = LatLng;

class LivreursProchesRepository {
  LivreursProchesRepository(this._api);

  final ApiClient _api;

  Future<List<LivreurProche>> autour(LatLng centre) async {
    final data = await _api.get('/drivers/nearby', query: {
      'lat': centre.latitude.toString(),
      'lng': centre.longitude.toString(),
    });

    final brut = data['positions'];
    if (brut is! List) return const [];

    final points = <LivreurProche>[];
    for (final item in brut) {
      if (item is! Map) continue;
      final lat = (item['latitude'] as num?)?.toDouble();
      final lng = (item['longitude'] as num?)?.toDouble();
      if (lat == null || lng == null) continue;
      points.add(LatLng(lat, lng));
    }

    return points;
  }
}

final livreursProchesRepositoryProvider = Provider<LivreursProchesRepository>(
  (ref) => LivreursProchesRepository(ref.watch(apiClientProvider)),
);

/// Les livreurs autour d'un point donne.
///
/// FAMILY SUR LE CENTRE, ET PAS UN PROVIDER GLOBAL : le client deplace la
/// carte, et chaque centre est une question differente. Riverpod garde alors
/// le resultat de chacun au lieu de tout jeter a chaque pixel.
///
/// AUCUNE MINUTERIE ICI. Rafraichir tout seul, c'est une requete par client et
/// par intervalle, indefiniment, y compris pour un client qui a laisse
/// l'application ouverte dans sa poche. La cadence est un des points restes
/// ouverts au point 24 ; tant qu'elle n'est pas tranchee, c'est l'ecran qui
/// demande, quand il a une raison de le faire.
///
/// AUTODISPOSE, ET CE N'EST PAS UN DETAIL DE STYLE. Une famille ordinaire
/// garde en memoire le resultat de CHAQUE centre jamais demande, pour toute la
/// duree de l'application. Un client qui promene la carte pendant dix minutes
/// en accumule des dizaines, dont aucun ne resservira. Avec autoDispose, celui
/// qu'aucun ecran ne regarde disparait.
final livreursProchesProvider =
    FutureProvider.autoDispose.family<List<LivreurProche>, LatLng>((ref, centre) {
  return ref.watch(livreursProchesRepositoryProvider).autour(centre);
});
