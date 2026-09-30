import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import '../../core/providers.dart';

/// Les types deja signales, pour n'avertir qu'une fois par valeur.
///
/// LE RELEVE TOURNE EN BOUCLE : sans cela, un type inconnu remplirait la
/// console a chaque battement et noierait le message qu'il porte.
final _typesSignales = <String>{};

/// Un livreur autour du client : ou il est, et avec quoi il roule.
///
/// LE TYPE DE VEHICULE EST ARRIVE LE 30 SEPTEMBRE 2026, par une revision du
/// point 24. Il est le SEUL attribut du livreur qui sorte avant l'affectation,
/// et il sort sans son identifiant.
///
/// CE QUE CE FICHIER NE TRANSPORTE TOUJOURS PAS, ET CE N'EST PAS UN OUBLI : ni
/// identifiant, ni nom, ni plaque, ni telephone. Un identifiant stable, meme
/// anonyme en apparence, permettrait de reconnaitre le meme livreur d'un jour
/// sur l'autre, donc de suivre ses horaires et ses trajets. C'est exactement ce
/// que la matrice refuse, et c'est ce qui borne le reste : deux livreurs a moto
/// restent indistinguables, et rien ne relie une position du jour a celle de la
/// veille.
///
/// LE TYPE DE VEHICULE EST SEMI-IDENTIFIANT, et le point 24 le dit : quatre de
/// ses cinq valeurs sont rares a Cotonou, donc un tricycle au milieu des motos
/// se repere. Le prix a ete pese avant d'etre paye ; ne pas l'elargir sans
/// rouvrir le point.
class LivreurProche {
  const LivreurProche({required this.position, required this.vehicule});

  final LatLng position;

  /// Le nom du contrat, tel que la passerelle le rend : « Motorcycle »,
  /// « Bicycle »... Vide quand un service plus ancien ne le donne pas.
  final String vehicule;
}

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
      final vehicule = item['vehicleType']?.toString() ?? '';

      // UN TYPE QUE LA CARTE NE SAIT PAS DESSINER SE SIGNALE, EN DEBOGAGE.
      // Sans cela, l'echec est MUET : l'epingle retombe sur le disque vert et
      // l'ecran a l'air normal. C'est ce qui est arrive le 30 septembre 2026 —
      // la passerelle tournait encore sur un binaire d'avant le champ, elle ne
      // rendait rien, et rien ne le disait. Un « assert » ne coute rien en
      // production : son contenu n'y est meme pas compile.
      assert(() {
        if (vehicule.isNotEmpty && Epingles.fichierDuVehicule(vehicule) != null) {
          return true;
        }
        if (_typesSignales.add(vehicule)) {
          debugPrint(
            vehicule.isEmpty
                ? '[livreurs] AUCUN vehicleType dans la reponse : la passerelle '
                    'est-elle reconstruite depuis l\'ajout du champ ?'
                : '[livreurs] vehicleType inconnu de la carte : « $vehicule »',
          );
        }
        return true;
      }());

      points.add(LivreurProche(position: LatLng(lat, lng), vehicule: vehicule));
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
