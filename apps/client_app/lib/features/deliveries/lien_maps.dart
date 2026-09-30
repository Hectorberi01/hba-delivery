import 'package:dio/dio.dart';

import 'point_field.dart';

/// Ce qu'on sait tirer d'un lien Google Maps colle par le client.
///
/// POURQUOI CETTE PORTE EXISTE. Placer une epingle a la main suppose de savoir
/// ou l'on va. Un client qui commande pour quelqu'un d'autre, lui, a recu une
/// adresse par WhatsApp — et a Cotonou, ce qui circule n'est presque jamais une
/// rue et un numero : c'est un lien Google Maps. Sans cette porte, il doit lire
/// le lien dans une application, retenir le quartier, revenir ici et retrouver
/// l'endroit au doigt. C'est la ou les points se posent a cent metres de la
/// bonne cour.
///
/// LE LIEN NE REMPLACE PAS LE REPERE ECRIT, ET NE LE REMPLACERA JAMAIS. Il pose
/// deux nombres, rien de plus ; c'est le repere saisi a cote qui dit au livreur
/// « portail vert, apres la pharmacie ». Les deux repondent a deux questions
/// differentes, et l'adressage beninois a besoin des deux.
///
/// AUCUNE CLE D'API, ET AUCUN GEOCODAGE. On ne demande jamais a Google « ou est
/// cette adresse » : on lit des coordonnees deja presentes dans le lien, ou on
/// suit une redirection. C'est gratuit, ca ne depend d'aucun quota, et ca ne
/// fait sortir aucune donnee du client.
abstract final class LienMaps {
  /// Bornes d'un point terrestre. Un lien tronque au collage produit volontiers
  /// un nombre plausible mais hors limites — c'est le filtre le moins cher.
  static bool _valide(double lat, double lng) =>
      lat >= -90 && lat <= 90 && lng >= -180 && lng <= 180;

  /// AUCUNE BORNE GEOGRAPHIQUE AU-DELA DE CELLES DU GLOBE, ET C'EST VOULU.
  /// Restreindre au Benin paraitrait prudent et casserait le premier essai fait
  /// depuis la France, sans rien dire d'utile. Le perimetre de service se
  /// decide au devis, pas dans un lecteur de texte.

  /// Les coordonnees contenues dans [texte], ou null.
  ///
  /// [texte] N'A PAS BESOIN D'ETRE UNE URL PROPRE. Ce qui arrive du
  /// presse-papier est souvent un morceau de message : « Je suis ici
  /// https://maps.app.goo.gl/x4Kd a partir de 18h ». On cherche donc un motif
  /// DANS la chaine au lieu d'exiger qu'elle en soit un.
  static PickedPoint? lire(String texte) {
    final brut = texte.replaceAll('%2C', ',').replaceAll('%2c', ',');

    // LE DECODAGE PEUT ECHOUER, ET SUR UN TEXTE DE PRESSE-PAPIER IL ECHOUERA.
    // « decodeFull » leve une FormatException sur un pourcentage isole : il
    // suffit d'avoir copie « 100% sur, c'est ici <lien> » pour que la lecture
    // parte en exception au lieu de rendre un point. On garde alors le texte
    // tel quel — les motifs ci-dessous s'en accommodent.
    String t;
    try {
      t = Uri.decodeFull(brut);
    } on FormatException {
      t = brut;
    }

    // L'ORDRE DES TENTATIVES EST L'ORDRE DE PRECISION, ET IL COMPTE.
    //
    // Un lien de lieu porte DEUX paires : « !3d…!4d… », qui est le lieu, et
    // « @… », qui est le centre de la camera au moment du partage. Les deux
    // different de plusieurs dizaines de metres des que l'utilisateur a fait
    // glisser la carte avant de partager. Lire « @ » en premier poserait donc
    // le point a cote de ce que l'expediteur croyait envoyer.
    for (final motif in _motifs) {
      final m = motif.firstMatch(t);
      if (m == null) continue;

      final lat = double.tryParse(m.group(1)!);
      final lng = double.tryParse(m.group(2)!);

      if (lat != null && lng != null && _valide(lat, lng)) {
        return PickedPoint(latitude: lat, longitude: lng);
      }
    }

    return null;
  }

  static final List<RegExp> _motifs = [
    // Le lieu lui-meme, dans le « data » d'un lien de fiche.
    RegExp(r'!3d(-?\d+\.?\d*)!4d(-?\d+\.?\d*)'),

    // Les parametres nommes : ?q=, &query=, &destination=, &center=.
    RegExp(
      r'[?&](?:q|query|destination|center)=(-?\d+\.?\d*)\s*,\s*(-?\d+\.?\d*)',
      caseSensitive: false,
    ),

    // Le centre de la camera. « ,15z » ou « ,3a,75y » suivent, on les ignore.
    RegExp(r'@(-?\d+\.?\d*),(-?\d+\.?\d*)'),

    // Un lien « geo: », que partagent certaines applications Android.
    RegExp(r'geo:(-?\d+\.?\d*),(-?\d+\.?\d*)'),

    // DEUX NOMBRES NUS, EN DERNIER RECOURS. Des gens collent « 6.3703, 2.3912 »
    // sans lien du tout. Ce motif est le plus permissif, donc le dernier
    // essaye : place plus haut, il attraperait les fragments d'une URL.
    RegExp(r'(?:^|[\s(])(-?\d{1,3}\.\d+)\s*,\s*(-?\d{1,3}\.\d+)'),
  ];

  /// Les hotes dont on accepte de suivre la redirection.
  ///
  /// UNE LISTE BLANCHE, ET NON UN FILTRE DE CE QU'ON REFUSE. Ce texte vient du
  /// presse-papier, donc de n'importe ou : sans liste blanche, l'application
  /// irait chercher l'URL que le premier message venu lui souffle. Ici elle ne
  /// parle qu'a Google, et elle verifie ENCORE a l'arrivee.
  static bool _hoteConnu(Uri uri) {
    final h = uri.host.toLowerCase();
    return h == 'maps.app.goo.gl' ||
        h == 'goo.gl' ||
        h == 'maps.google.com' ||
        h == 'www.google.com' ||
        h == 'google.com' ||
        h.endsWith('.google.com');
  }

  /// La premiere URL de [texte] dont l'hote est connu, ou null.
  static Uri? url(String texte) {
    final m = RegExp(r'https?://[^\s<>"]+').firstMatch(texte);
    if (m == null) return null;

    final uri = Uri.tryParse(m.group(0)!);
    return uri != null && _hoteConnu(uri) ? uri : null;
  }

  /// Les coordonnees de [texte], en suivant la redirection s'il le faut.
  ///
  /// POURQUOI UN APPEL RESEAU EST INEVITABLE ICI. Le lien que partage
  /// l'application Google Maps — « maps.app.goo.gl/… » — NE CONTIENT AUCUNE
  /// COORDONNEE. Ce n'est pas une omission : c'est un identifiant opaque, et
  /// seul le serveur de Google sait vers quoi il pointe. Le lire sans le suivre
  /// est impossible, et refuser ces liens reviendrait a refuser le cas le plus
  /// courant.
  ///
  /// [client] N'EST PAS L'ApiClient DE L'APPLICATION, ET NE DOIT JAMAIS L'ETRE.
  /// Celui-ci porte le jeton du client HBA dans chacun de ses en-tetes : s'en
  /// servir ici enverrait ce jeton a Google. On construit donc un Dio nu.
  static Future<PickedPoint?> resoudre(String texte, {Dio? client}) async {
    // ON LIT AVANT DE DEMANDER. Un lien long porte deja ses coordonnees ; le
    // suivre serait un aller-retour reseau pour une reponse qu'on a.
    final direct = lire(texte);
    if (direct != null) return direct;

    final cible = url(texte);
    if (cible == null) return null;

    final dio = client ??
        Dio(BaseOptions(
          connectTimeout: const Duration(seconds: 6),
          receiveTimeout: const Duration(seconds: 6),

          // ON NE SUIT PAS LA REDIRECTION, ON LA LIT. « followRedirects » ferait
          // telecharger la page de Google entiere — plusieurs centaines de
          // kilo-octets sur une connexion que le client paie — alors que tout ce
          // qu'on veut est l'adresse de l'en-tete « location ».
          followRedirects: false,

          // Sans cela, Dio leve sur un 3xx comme sur une panne.
          validateStatus: (code) => code != null && code < 400,
        ));

    try {
      // TROIS SAUTS AU PLUS. Un lien court passe parfois par un intermediaire ;
      // une boucle, elle, tournerait sans fin.
      var uri = cible;
      for (var saut = 0; saut < 3; saut++) {
        final reponse = await dio.getUri<void>(uri);

        final suivante = reponse.headers.value('location');
        if (suivante == null) break;

        final resolue = uri.resolve(suivante);

        // ON REVERIFIE L'HOTE A CHAQUE SAUT. Une redirection peut pointer
        // n'importe ou, et la liste blanche du depart ne dit rien de l'arrivee.
        if (!_hoteConnu(resolue)) return null;

        final trouve = lire(resolue.toString());
        if (trouve != null) return trouve;

        uri = resolue;
      }
    } on Object {
      // HORS LIGNE, OU GOOGLE QUI REFUSE : ON NE SAIT PAS, ET ON LE DIT EN
      // RENDANT NULL. L'appelant propose alors de placer le point a la main,
      // ce qui marche toujours. Faire remonter une exception ferait d'une
      // commodite une panne.
      return null;
    }

    return null;
  }
}
