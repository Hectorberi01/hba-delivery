import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// Une commande envoyée dont le paiement n'est pas confirmé.
///
/// POURQUOI LE CORPS COMPLET EST GARDE, ET PAS SEULEMENT L'IDENTIFIANT. Reprendre
/// un paiement, c'est REJOUER la creation avec la meme cle d'idempotence : le
/// service reconnait alors sa propre requete et rend la MEME commande avec la
/// MEME adresse de paiement, sans rien creer ni consommer de second devis
/// (CreateDeliveryHandler et RequestRideHandler, chemin de rejeu explicite).
///
/// Il n'existe pas de route « rouvre le paiement de la commande X » : l'adresse
/// n'est deliberement pas rendue par les lectures, parce qu'elle expire et qu'un
/// bouton « payer » menant sur une page morte est pire que pas de bouton. Le
/// rejeu est donc le seul chemin, et un rejeu a besoin de son corps.
///
/// LE TELEPHONE DU DESTINATAIRE EST DEDANS, d'ou le stockage chiffre. Ce n'est
/// pas un secret au sens d'un jeton, mais c'est la donnee personnelle d'un TIERS
/// qui n'a jamais rien demande a HBA.
class CommandeAPayer {
  const CommandeAPayer({
    required this.nature,
    required this.id,
    required this.cle,
    required this.corps,
    required this.envoyeeLe,
  });

  /// « colis » ou « course ». Elle decide de la route a rejouer et de l'ecran
  /// vers lequel renvoyer ensuite.
  final String nature;

  final String id;
  final String cle;
  final Map<String, Object?> corps;
  final DateTime envoyeeLe;

  bool get estUneCourse => nature == 'course';

  Map<String, Object?> versJson() => {
        'nature': nature,
        'id': id,
        'cle': cle,
        'corps': corps,
        'envoyeeLe': envoyeeLe.toIso8601String(),
      };

  static CommandeAPayer? depuis(Object? brut) {
    if (brut is! Map) return null;

    final nature = brut['nature'];
    final id = brut['id'];
    final cle = brut['cle'];
    final corps = brut['corps'];
    final envoyee = DateTime.tryParse('${brut['envoyeeLe']}');

    if (nature is! String || id is! String || cle is! String) return null;
    if (corps is! Map || envoyee == null) return null;
    if (nature != 'colis' && nature != 'course') return null;

    return CommandeAPayer(
      nature: nature,
      id: id,
      cle: cle,
      corps: corps.cast<String, Object?>(),
      envoyeeLe: envoyee,
    );
  }
}

/// Ce que l'application garde sur l'appareil des commandes a payer.
///
/// LE TROU QU'IL COMBLE. La cle d'idempotence vivait dans un champ de l'ecran de
/// commande. Le client partait sur la page du fournisseur, le systeme tuait
/// l'application en arriere-plan, et la cle disparaissait avec elle : au retour,
/// plus rien a rejouer. La commande restait invisible — les impayees sont
/// retirees des listes du client, a dessein — puis le balayage la fermait au bout
/// d'un quart d'heure. Le client voyait son argent non preleve et sa commande
/// nulle part.
///
/// LE SERVICE SAVAIT DEJA REPRENDRE : c'est le point 30, tranche le 29 septembre
/// 2026. Ce qui manquait etait entierement de ce cote-ci.
class PaiementsEnAttente {
  PaiementsEnAttente([FlutterSecureStorage? storage])
      : _storage = storage ?? const FlutterSecureStorage();

  static const _cleDeStockage = 'hba.paiements_en_attente';

  /// LE MEME QUART D'HEURE QUE LE SERVEUR, ET IL FAUT QUE CE SOIT LE MEME. Au-dela,
  /// le balayage a ferme la commande et le devis a expire : proposer une reprise
  /// enverrait le client sur un refus. Si le delai change cote serveur
  /// (UnpaidDelivery:GraceMinutes), celui-ci doit suivre.
  static const Duration duree = Duration(minutes: 15);

  final FlutterSecureStorage _storage;

  /// Les commandes encore reprenables, la plus recente d'abord.
  ///
  /// LA PURGE SE FAIT A LA LECTURE, pas par un minuteur : une entree perimee
  /// n'est jamais rendue, et le nettoyage arrive quand on en a besoin. Un
  /// minuteur devrait survivre a l'application, ce qu'il ne peut pas.
  Future<List<CommandeAPayer>> lire() async {
    final brut = await _storage.read(key: _cleDeStockage);
    if (brut == null || brut.isEmpty) return const [];

    final limite = DateTime.now().toUtc().subtract(duree);

    final List<dynamic> liste;
    try {
      liste = jsonDecode(brut) as List<dynamic>;
    } on FormatException {
      // UN CONTENU ILLISIBLE S'EFFACE AU LIEU DE REVENIR A CHAQUE DEMARRAGE.
      // C'est du confort local, jamais une source de verite : rien n'est perdu
      // qui ne soit deja chez le serveur.
      await _storage.delete(key: _cleDeStockage);
      return const [];
    }

    final vivantes = [
      for (final e in liste)
        if (CommandeAPayer.depuis(e) case final CommandeAPayer c)
          if (c.envoyeeLe.isAfter(limite)) c,
    ]..sort((a, b) => b.envoyeeLe.compareTo(a.envoyeeLe));

    if (vivantes.length != liste.length) {
      await _ecrire(vivantes);
    }

    return vivantes;
  }

  /// Retient une commande qui attend son paiement.
  Future<void> poser(CommandeAPayer commande) async {
    final autres = (await lire()).where((c) => c.id != commande.id);
    await _ecrire([commande, ...autres]);
  }

  /// Oublie une commande : payee, annulee, ou morte.
  Future<void> oublier(String id) async {
    final restantes = (await lire()).where((c) => c.id != id).toList();
    await _ecrire(restantes);
  }

  Future<void> _ecrire(List<CommandeAPayer> commandes) async {
    if (commandes.isEmpty) {
      await _storage.delete(key: _cleDeStockage);
      return;
    }

    // TROIS AU PLUS, ET C'EST DEJA GENEREUX : on ne paie qu'une commande a la
    // fois. Le plafond protege d'une accumulation si un oubli echouait.
    final gardees = commandes.take(3).map((c) => c.versJson()).toList();

    await _storage.write(key: _cleDeStockage, value: jsonEncode(gardees));
  }
}
