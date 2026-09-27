import 'package:flutter_riverpod/flutter_riverpod.dart';

/// Relit un FutureProvider pour un tirer-pour-rafraichir.
///
/// LE « await ref.read(p.future) » NU EST UN PIEGE. Quand la lecture echoue —
/// service arrete, reseau coupe —, le future rend l'erreur, personne ne
/// l'attrape, et elle remonte en « Unhandled Exception » jusqu'a la console.
/// L'ecran, lui, l'affiche proprement par son « error: » ; l'exception
/// parallele n'informe personne et noie les journaux au point d'y masquer
/// les vraies.
///
/// ON AVALE ICI, ET SEULEMENT ICI : l'etat d'erreur du provider reste intact,
/// c'est lui qui parle a l'utilisateur. Cette fonction ne sert qu'a dire au
/// RefreshIndicator « c'est fini, tu peux remonter ».
///
/// ELLE PREND « provider.future », PAS LE PROVIDER. Un
/// AutoDisposeFutureProvider n'est pas un sous-type de FutureProvider : une
/// signature ecrite sur l'un refuse l'autre, et il aurait fallu deux
/// fonctions. « .future » est un Refreshable dans les deux cas, et
/// « ref.refresh » invalide ET relit d'un seul geste.
Future<void> rafraichirDepuis<T>(WidgetRef ref, Refreshable<Future<T>> futur) async {
  try {
    // « refresh » EST ANNOTE @useResult. La valeur ne nous sert a rien — c'est
    // l'ecran qui lit l'etat du provider —, mais l'analyseur signale un
    // resultat jete comme un oubli probable. On le consomme dans un caractere
    // de rebut plutot que de desactiver la regle : ailleurs, elle a raison.
    final _ = await ref.refresh(futur);
  } on Object {
    // L'ecran lit deja l'erreur dans l'etat du provider.
  }
}
