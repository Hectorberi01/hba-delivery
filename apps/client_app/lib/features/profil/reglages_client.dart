/// Ce qui change d'un deploiement a l'autre, et qui n'a rien a faire en dur.
///
/// PAR « --dart-define », PAS PAR UNE CONSTANTE. Un numero de support ecrit
/// dans le code se retrouve dans toutes les versions jamais publiees, y compris
/// celles d'un autre pays ou d'un autre partenaire. Ici il entre a la
/// compilation :
///
///   flutter run --dart-define=HBA_SUPPORT_TEL=+22900000000 \
///               --dart-define=HBA_SUPPORT_EMAIL=support@hba.example
///
/// VIDE, L'ENTREE N'APPARAIT PAS. C'est la regle qui compte : un bouton
/// « Appeler le support » qui ouvre un composeur vide est pire que pas de
/// bouton du tout — le client croit avoir joint quelqu'un.
///
/// MEMES NOMS QUE L'APPLICATION LIVREUR, a dessein : les deux se compilent avec
/// les memes definitions, et un nom different obligerait a s'en souvenir.
abstract final class Reglages {
  static const supportTelephone = String.fromEnvironment('HBA_SUPPORT_TEL');
  static const supportEmail = String.fromEnvironment('HBA_SUPPORT_EMAIL');

  static const conditions = String.fromEnvironment('HBA_CGU');
  static const confidentialite = String.fromEnvironment('HBA_CONFIDENTIALITE');

  static bool get aSupport =>
      supportTelephone.isNotEmpty || supportEmail.isNotEmpty;

  // « aDocuments » A ETE RETIRE, ET LA DISTINCTION QU'IL PORTAIT AVEC.
  //
  // Il servait a cacher l'entree « Conditions et confidentialite » quand aucune
  // adresse en ligne n'etait configuree. Les textes vivent desormais DANS
  // l'application : ils s'ouvrent toujours, configures ou pas. Les deux
  // adresses ci-dessus ne servent plus qu'a proposer la version publiee en plus
  // de l'ecran, jamais a sa place.
}

/// Le lien « mailto » vers le support, objet pre-rempli.
///
/// TOUT EST ENCODE, MEME CE QUI PARAIT SUR. Une reference de course ou un objet
/// contient parfois une espace ou un accent ; non encode, le lien se coupe au
/// premier caractere genant et l'objet arrive tronque — sans que personne ne
/// s'en apercoive, puisque le courriel part quand meme.
///
/// LE SUJET DIT DE QUOI IL S'AGIT AVANT QUE LE CLIENT N'ECRIVE. « Support » tout
/// court oblige a lire tout le message pour savoir quoi en faire ; une
/// reference de course se trie a l'oeil.
String courrielSupport({String? reference, String sujet = 'Support client'}) {
  final objet =
      reference == null || reference.isEmpty ? sujet : '$sujet - $reference';

  return 'mailto:${Reglages.supportEmail}?subject=${Uri.encodeComponent(objet)}';
}
