import 'package:google_maps_flutter/google_maps_flutter.dart';

/// Decode un trace au format polyline encodee de Google.
///
/// POURQUOI A LA MAIN ET NON PAR UN PAQUET. L'algorithme tient en trente lignes
/// et n'a pas bouge depuis quinze ans ; ajouter une dependance pour cela ferait
/// un paquet de plus a suivre, a auditer et a mettre a jour sur les deux
/// applications, pour un format fige.
///
/// D'OU VIENT CE FORMAT. OsrmRouteProvider demande « geometries=polyline » a
/// OSRM ; Delivery le relaie tel quel dans ActiveRoute.encoded_polyline. C'est
/// exactement l'encodage que google_maps_flutter attend pour une Polyline — rien
/// n'est converti en chemin, ni cote serveur ni ici.
///
/// CINQ DECIMALES, PAS SIX. « polyline » est le profil a 1e5 ; « polyline6 »
/// existe et divise par un million. Les melanger place le trace a cent
/// kilometres de la route, ce qui se voit — mais seulement une fois sur la
/// carte, jamais a la compilation.
///
/// UN TRACE ILLISIBLE REND UNE LISTE VIDE. Il vient du reseau : une chaine
/// tronquee ne doit pas lever au milieu d'un build et emporter l'ecran de suivi
/// pour une ligne qu'on pouvait ne pas dessiner.
List<LatLng> decoderLaPolyligne(String encode) {
  if (encode.isEmpty) return const [];

  final points = <LatLng>[];
  var index = 0;
  var latitude = 0;
  var longitude = 0;

  try {
    while (index < encode.length) {
      latitude += _valeurSuivante(encode, index, (i) => index = i);
      longitude += _valeurSuivante(encode, index, (i) => index = i);

      points.add(LatLng(latitude / 1e5, longitude / 1e5));
    }
  } on Object {
    return const [];
  }

  return points;
}

/// Lit un entier en complement a deux, cinq bits par caractere.
int _valeurSuivante(String encode, int depart, void Function(int) poser) {
  var index = depart;
  var decalage = 0;
  var accumulateur = 0;
  int octet;

  do {
    // LE DECALAGE DE 63 EST DANS LE FORMAT : les valeurs sont rendues
    // imprimables en leur ajoutant 63, pour tenir dans une chaine ASCII.
    octet = encode.codeUnitAt(index++) - 63;
    accumulateur |= (octet & 0x1f) << decalage;
    decalage += 5;
  } while (octet >= 0x20);

  poser(index);

  // LE BIT DE POIDS FAIBLE PORTE LE SIGNE, et la valeur est decalee d'un cran.
  return (accumulateur & 1) != 0 ? ~(accumulateur >> 1) : accumulateur >> 1;
}
