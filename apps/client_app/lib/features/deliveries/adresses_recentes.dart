import 'dart:convert';
import 'dart:math' as maths;

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'point_field.dart';

/// Un endroit ou le client a deja envoye ou fait venir quelque chose.
class AdresseRecente {
  const AdresseRecente({
    required this.latitude,
    required this.longitude,
    required this.repere,
    required this.utiliseeLe,
  });

  final double latitude;
  final double longitude;

  /// Ce que le client avait ecrit — « en face de la pharmacie ». Vide quand il
  /// n'avait rien ecrit : la liste montre alors les coordonnees, qui prouvent au
  /// moins qu'un point a ete pose.
  final String repere;

  final DateTime utiliseeLe;

  PickedPoint get point =>
      PickedPoint(latitude: latitude, longitude: longitude);

  Map<String, Object?> versJson() => {
        'lat': latitude,
        'lng': longitude,
        'repere': repere,
        'le': utiliseeLe.toIso8601String(),
      };

  static AdresseRecente? depuis(Object? brut) {
    if (brut is! Map) return null;

    final lat = brut['lat'];
    final lng = brut['lng'];
    final le = DateTime.tryParse('${brut['le']}');

    if (lat is! num || lng is! num || le == null) return null;

    return AdresseRecente(
      latitude: lat.toDouble(),
      longitude: lng.toDouble(),
      repere: brut['repere'] is String ? brut['repere'] as String : '',
      utiliseeLe: le,
    );
  }
}

/// Les derniers endroits utilises, gardes sur l'appareil.
///
/// POURQUOI SUR L'APPAREIL ET NON CHEZ LE SERVICE. Ce n'est pas une donnee de
/// la plateforme : c'est un confort de saisie, propre a ce telephone. Lui faire
/// traverser le reseau demanderait une route, une regle de visibilite, une
/// retention — pour une liste de huit lignes qui ne vaut rien a personne
/// d'autre.
///
/// DANS LE STOCKAGE CHIFFRE, COMME LES PAIEMENTS EN ATTENTE. Ce n'est pas un
/// secret au sens d'un jeton, mais la liste des endroits ou quelqu'un envoie ses
/// colis dit beaucoup de lui : ou il habite, ou il travaille, qui il connait. Un
/// telephone perdu ne doit pas la rendre lisible.
///
/// POURQUOI ELLE EXISTE. Dans une application de livraison, la plupart des
/// adresses sont des REPETITIONS. Avant ce jour, le client devait rouvrir la
/// carte et retrainer une epingle au meme endroit a chaque commande — le geste
/// le plus couteux du parcours, refait pour rien. Repondre en un appui vaut plus
/// que n'importe quelle autocompletion, et ne coute pas un octet de reseau.
class AdressesRecentes {
  AdressesRecentes([FlutterSecureStorage? stockage])
      : _stockage = stockage ?? const FlutterSecureStorage();

  static const _cleDeStockage = 'hba.adresses_recentes';

  /// HUIT, ET PAS VINGT. La liste s'affiche sans defilement sous les adresses
  /// enregistrees ; au-dela, elle devient un deuxieme ecran a parcourir, c'est-
  /// a-dire exactement ce qu'elle devait eviter.
  static const maximum = 8;

  /// Deux points a moins de cinquante metres sont le MEME endroit.
  ///
  /// SANS CE SEUIL, LA LISTE SE REMPLIT D'UNE SEULE ADRESSE. Une epingle posee a
  /// la main ne retombe jamais au metre pres : huit commandes vers la maison
  /// donneraient huit lignes « maison » a deux pas l'une de l'autre, et la liste
  /// ne servirait plus a rien.
  static const seuilDeDoublonMetres = 50.0;

  final FlutterSecureStorage _stockage;

  /// La plus recente d'abord.
  Future<List<AdresseRecente>> lire() async {
    final brut = await _stockage.read(key: _cleDeStockage);
    if (brut == null || brut.isEmpty) return const [];

    final List<dynamic> liste;
    try {
      liste = jsonDecode(brut) as List<dynamic>;
    } on FormatException {
      // UN CONTENU ILLISIBLE S'EFFACE AU LIEU DE REVENIR A CHAQUE DEMARRAGE.
      // C'est du confort local : rien n'est perdu qui compte.
      await _stockage.delete(key: _cleDeStockage);
      return const [];
    }

    return [
      for (final e in liste)
        if (AdresseRecente.depuis(e) case final AdresseRecente a) a,
    ]..sort((a, b) => b.utiliseeLe.compareTo(a.utiliseeLe));
  }

  /// Retient un endroit qui vient de servir.
  Future<void> retenir({
    required PickedPoint point,
    required String repere,
  }) async {
    final anciennes = await lire();

    final nouvelle = AdresseRecente(
      latitude: point.latitude,
      longitude: point.longitude,
      repere: repere.trim(),
      utiliseeLe: DateTime.now().toUtc(),
    );

    // LE REPERE LE PLUS RECENT GAGNE, ET LE POINT AUSSI. Quelqu'un qui corrige
    // « portail bleu » en « portail bleu, deuxieme etage » veut la version
    // corrigee ; garder l'ancienne ligne lui redonnerait sa faute a chaque
    // commande.
    final autres = anciennes.where(
      (a) => _metres(a.latitude, a.longitude, point.latitude, point.longitude) >
          seuilDeDoublonMetres,
    );

    await _ecrire([nouvelle, ...autres].take(maximum).toList());
  }

  Future<void> oublierTout() => _stockage.delete(key: _cleDeStockage);

  Future<void> _ecrire(List<AdresseRecente> liste) => _stockage.write(
        key: _cleDeStockage,
        value: jsonEncode([for (final a in liste) a.versJson()]),
      );

  /// Distance approchee, suffisante pour decider d'un doublon.
  ///
  /// LE FACTEUR DE LONGITUDE SE CALCULE. Une constante choisie pour Cotonou
  /// ferait fusionner a Paris deux adresses distantes de quatre-vingts metres,
  /// et l'inverse ailleurs — le banc d'essai est en France, le terrain au Benin.
  static double _metres(double aLat, double aLng, double bLat, double bLng) {
    const metresParDegre = 111320.0;
    const versRadians = maths.pi / 180;

    final dLat = (aLat - bLat) * metresParDegre;
    final dLng = (aLng - bLng) *
        metresParDegre *
        maths.cos((aLat + bLat) / 2 * versRadians).abs();

    return maths.sqrt(dLat * dLat + dLng * dLng);
  }
}

final adressesRecentesStoreProvider =
    Provider<AdressesRecentes>((ref) => AdressesRecentes());

final adressesRecentesProvider = FutureProvider<List<AdresseRecente>>(
  (ref) => ref.watch(adressesRecentesStoreProvider).lire(),
);
