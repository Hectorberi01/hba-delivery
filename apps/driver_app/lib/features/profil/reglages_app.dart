/// Reglages fournis au lancement, plutot qu'ecrits dans le code.
///
/// CES VALEURS N'EXISTENT PAS DANS LE REFERENTIEL. Ni numero de support, ni
/// adresse electronique, ni adresse des conditions generales n'y figurent :
/// les inventer donnerait un bouton qui appelle dans le vide. Vides par
/// defaut, les ecrans n'affichent alors pas l'entree du tout — ce qui est
/// preferable a une entree morte.
///
/// WHATSAPP A DISPARU DES CONTACTS DU SUPPORT (27 septembre 2026). Le canal
/// ecrit du support est l'adresse electronique : une conversation WhatsApp
/// vit dans le telephone d'une personne, ne se transmet pas a un collegue, ne
/// se retrouve pas six mois plus tard, et melange le support avec le
/// personnel. WhatsApp reste par ailleurs le canal des codes a usage unique,
/// ce qui est un autre sujet et un autre service.
///
///   flutter run \
///     --dart-define=HBA_SUPPORT_TEL=+22901... \
///     --dart-define=HBA_SUPPORT_EMAIL=support@hbatechettrade.com \
///     --dart-define=HBA_CGU=https://hbatechettrade.com/cgu \
///     --dart-define=HBA_CONFIDENTIALITE=https://hbatechettrade.com/confidentialite
abstract final class Reglages {
  static const supportTelephone = String.fromEnvironment('HBA_SUPPORT_TEL');
  static const supportEmail = String.fromEnvironment('HBA_SUPPORT_EMAIL');

  /// Adresses des versions PUBLIEES des deux documents.
  ///
  /// LE TEXTE N'EN DEPEND PLUS : conditions et confidentialite s'affichent
  /// desormais dans l'application, hors ligne, et ces adresses ne servent qu'a
  /// proposer la version en ligne en complement. Vides, les deux ecrans
  /// fonctionnent quand meme — c'est ce qui a change.
  static const conditions = String.fromEnvironment('HBA_CGU');
  static const confidentialite = String.fromEnvironment('HBA_CONFIDENTIALITE');

  static bool get aSupport =>
      supportTelephone.isNotEmpty || supportEmail.isNotEmpty;
}

/// Le lien « mailto » vers le support, objet pre-rempli.
///
/// TOUT EST ENCODE, MEME CE QUI PARAIT SUR. Un identifiant de livreur ou un
/// sujet contient parfois une espace ou un accent ; non encode, le lien se
/// coupe au premier caractere genant et l'objet arrive tronque — sans que
/// personne ne s'en apercoive, puisque le courriel part quand meme.
///
/// LE SUJET DIT DE QUOI IL S'AGIT AVANT QUE LE LIVREUR N'ECRIVE. « Support
/// livreur » oblige le support a lire tout le message pour savoir quoi en
/// faire ; « Changement de vehicule - <id> » se trie a l'oeil.
String courrielSupport({String? livreurId, String sujet = 'Support livreur'}) {
  final objet = livreurId == null || livreurId.isEmpty
      ? sujet
      : '$sujet - $livreurId';

  return 'mailto:${Reglages.supportEmail}?subject=${Uri.encodeComponent(objet)}';
}
