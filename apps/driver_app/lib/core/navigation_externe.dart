import 'dart:io';

import 'package:url_launcher/url_launcher.dart';

/// Ouvre l'itineraire dans l'application de navigation du telephone.
///
/// PAS DE CARTE INTEGREE, ET C'EST UN CHOIX. Un motard a Cotonou navigue avec
/// l'application qu'il connait deja — Google Maps, Waze, ou celle qu'il a
/// installee. Lui en imposer une autre, moins bonne, dans un ecran qu'il doit
/// quitter pour telephoner, ne l'aiderait pas. Cela evite en prime une cle
/// d'API, un compte de facturation et un cout par chargement de carte.
///
/// TROIS TENTATIVES, DE LA PLUS OUVERTE A LA PLUS SURE :
///
///   1. « geo: » sur Android laisse le systeme proposer TOUTES les
///      applications installees. C'est la seule forme qui n'impose pas
///      Google Maps a un livreur qui prefere Waze.
///   2. « maps: » sur iOS ouvre Plans, qui est toujours present.
///   3. L'adresse https de Google Maps fonctionne partout : l'application si
///      elle est installee, le navigateur sinon. Elle ne peut pas echouer
///      faute d'application, seulement faute de reseau.
///
/// Rend false quand aucune n'a abouti. L'APPELANT DOIT LE DIRE : un bouton
/// « Y aller » qui ne fait rien laisse le livreur a l'arret sans comprendre.
Future<bool> ouvrirItineraire({
  required double latitude,
  required double longitude,
  String? etiquette,
}) async {
  // Les coordonnees partent en point decimal quel que soit le telephone :
  // une locale a virgule produirait « 6,3654 » et une adresse invalide.
  final point = '${latitude.toStringAsFixed(6)},${longitude.toStringAsFixed(6)}';
  final nom = Uri.encodeComponent(etiquette ?? '');

  final candidates = <String>[
    if (Platform.isAndroid)
      nom.isEmpty ? 'geo:$point?q=$point' : 'geo:$point?q=$point($nom)',
    if (Platform.isIOS) 'maps://?daddr=$point&dirflg=d',
    'https://www.google.com/maps/dir/?api=1&destination=$point',
  ];

  for (final brut in candidates) {
    final cible = Uri.tryParse(brut);
    if (cible == null) continue;

    try {
      // externalApplication et non la vue integree : le livreur doit garder
      // la navigation ouverte pendant qu'il roule, et revenir a HBA sans la
      // perdre.
      if (await launchUrl(cible, mode: LaunchMode.externalApplication)) {
        return true;
      }
    } on Object {
      // Schema inconnu du telephone : on essaie le suivant.
    }
  }

  return false;
}

/// Ouvre le composeur du telephone sur ce numero.
///
/// « tel: » OUVRE LE COMPOSEUR, IL N'APPELLE PAS. Android et iOS interdisent a
/// une application de declencher un appel elle-meme, et c'est tant mieux : le
/// livreur voit le numero avant que la ligne ne parte. Le libelle du bouton
/// dit donc « Appeler » au sens ou l'on tend un combine, pas au sens ou l'on
/// compose a la place du livreur.
///
/// LE NUMERO EST NETTOYE AVANT D'ENTRER DANS L'ADRESSE. Le BFF rend ce que le
/// client a saisi : « +229 01 97 75 53 41 », parfois avec des points ou des
/// tirets. Les espaces cassent l'adresse sur certains telephones ; on ne garde
/// que les chiffres et un « + » de tete. Le reste — parentheses d'indicatif,
/// separateurs — ne sert qu'a l'oeil humain et disparait ici seulement, jamais
/// de l'affichage.
///
/// Rend false quand le numero est inexploitable ou qu'aucune application ne
/// repond. L'APPELANT DOIT LE DIRE : un bouton « Appeler » qui ne fait rien
/// laisse le livreur devant une porte fermee sans savoir quoi faire.
Future<bool> appeler(String numero) async {
  final plus = numero.trimLeft().startsWith('+');
  final chiffres = numero.replaceAll(RegExp(r'[^0-9]'), '');

  // Moins de quatre chiffres n'est pas un numero : c'est un champ a demi
  // rempli. Ouvrir le composeur dessus ne menerait nulle part.
  if (chiffres.length < 4) return false;

  final cible = Uri.tryParse('tel:${plus ? '+' : ''}$chiffres');
  if (cible == null) return false;

  try {
    return await launchUrl(cible);
  } on Object {
    // Telephone sans application d'appel — une tablette, par exemple.
    return false;
  }
}
