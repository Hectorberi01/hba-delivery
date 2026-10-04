import 'package:flutter/foundation.dart';

/// Ce qu'on affiche quand un appel n'a pas abouti faute de réseau.
///
/// POURQUOI CE FICHIER EXISTE. « Pas de réseau. Réessayez. » est le message que
/// toutes les applications affichaient sur une [OfflineException]. Il est juste
/// quand le téléphone est hors couverture, et FAUX dans le cas le plus fréquent
/// en développement : le Wi-Fi marche, la barre d'état le montre, et c'est le
/// serveur qui ne répond pas à l'adresse demandée. On cherche alors du côté du
/// réseau pendant que la cause est dans un « --dart-define ».
///
/// DIO NE SAIT PAS DISTINGUER LES DEUX, et personne ne le peut vraiment : une
/// socket refusée, un DNS muet et un téléphone hors couverture arrivent tous en
/// « connectionError ». Ce qu'on PEUT faire, c'est nommer l'adresse qu'on a
/// essayée — et laisser la personne conclure.
///
/// EN PRODUCTION, LE MESSAGE NE CHANGE PAS. Un client n'a que faire d'un nom
/// d'hôte : il veut savoir s'il doit réessayer. Le diagnostic n'apparaît qu'en
/// debug, où il s'adresse à celui qui développe.
String messageHorsLigne(String baseUrl) {
  if (!kDebugMode) return 'Pas de réseau. Réessayez.';

  final hote = Uri.tryParse(baseUrl)?.host ?? '';

  if (hote.isEmpty) {
    return 'Serveur injoignable. Vérifiez HBA_API_BASE : « $baseUrl ».';
  }

  // LOCALHOST SUR UN TELEPHONE DESIGNE LE TELEPHONE, et c'est l'erreur qui
  // coûte le plus de temps parce qu'elle ressemble à une panne de réseau. Sur
  // un appareil physique, aucune passerelle n'écoute là : il faut l'adresse de
  // la machine sur le réseau local.
  //
  // LE SIMULATEUR iOS, LUI, PARTAGE LA PILE RESEAU DU MAC et joint bien
  // localhost. Le message ne dit donc pas « c'est faux », il dit ce que
  // localhost désigne — vrai dans les deux cas.
  final estLocal = hote == 'localhost' || hote == '127.0.0.1';

  if (estLocal) {
    return 'Serveur injoignable. « $hote » désigne le téléphone lui-même : sur '
        'un appareil physique, relancez avec --dart-define=HBA_API_BASE='
        'http://ADRESSE-DU-MAC:5100/api/client/v1';
  }

  return 'Serveur injoignable à « $hote ». Vérifiez que la passerelle tourne '
      'et qu\'elle écoute sur une adresse joignable depuis le téléphone '
      '(GATEWAY_BIND=0.0.0.0).';
}
