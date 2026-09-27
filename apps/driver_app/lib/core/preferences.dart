import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'plantages.dart';
import 'providers.dart';

/// Les reglages qui vivent sur le TELEPHONE, et nulle part ailleurs.
///
/// POURQUOI PAS COTE SERVICE. Un reglage de confort range dans le profil
/// livreur couterait un attribut, une route de mise a jour et une migration —
/// et le referentiel des acteurs ne declare aujourd'hui aucun attribut de ce
/// genre sur le livreur. Le prix est un livreur qui change de telephone et
/// retrouve la valeur par defaut. Pour un son, c'est un prix qu'on paie.
///
/// LE COFFRE PLUTOT QU'UN FICHIER DE PREFERENCES, faute d'autre chose : il est
/// deja la pour la file d'actions et les jetons, et ajouter shared_preferences
/// pour un booleen serait une dependance de plus a suivre. Ce n'est pas un
/// secret ; le coffre ne coute rien de plus.
class SonDOffre extends Notifier<bool> {
  static const _cle = 'hba.driver.son_offre';

  /// LE DEFAUT EST « ACTIF », ET IL EST RENDU AVANT LA LECTURE DU COFFRE.
  ///
  /// La lecture est asynchrone ; l'etat doit exister tout de suite. Se tromper
  /// dans un sens fait entendre un son a un livreur qui l'avait coupe, se
  /// tromper dans l'autre fait manquer une course. On se trompe donc du cote
  /// bruyant.
  ///
  /// LA FENETRE EST FERMEE AILLEURS : l'ecran d'accueil demande ce provider a
  /// son initState, bien avant qu'une offre puisse arriver. Le defaut ne sert
  /// qu'aux quelques millisecondes du demarrage.
  @override
  bool build() {
    unawaited(_charger());
    return true;
  }

  Future<void> _charger() async {
    try {
      final brut = await ref.read(secureStorageProvider).read(key: _cle);
      if (brut != null) state = brut == '1';
    } on Object catch (erreur, pile) {
      // Coffre illisible : on reste sur le defaut bruyant. Une preference
      // perdue ne doit pas empecher un livreur d'entendre une offre.
      Plantages.noter(erreur, pile, contexte: 'lecture du reglage son d\'offre');
    }
  }

  /// L'ETAT CHANGE AVANT L'ECRITURE, PAS APRES. L'interrupteur doit suivre le
  /// doigt ; attendre le coffre le ferait sautiller sur un telephone lent.
  Future<void> definir({required bool actif}) async {
    if (state == actif) return;
    state = actif;

    try {
      await ref
          .read(secureStorageProvider)
          .write(key: _cle, value: actif ? '1' : '0');
    } on Object catch (erreur, pile) {
      // L'ecriture a echoue : le reglage vaut pour cette session et sera
      // oublie au prochain lancement. On ne revient PAS en arriere a l'ecran —
      // ce que le livreur vient de demander est respecte maintenant.
      Plantages.noter(erreur, pile, contexte: 'ecriture du reglage son d\'offre');
    }
  }
}

/// Le son joue-t-il a l'arrivee d'une offre.
///
/// LA VIBRATION N'EST PAS CONCERNEE, ET C'EST LE COEUR DU REGLAGE. Couper les
/// deux reviendrait a se rendre injoignable sans le savoir : un livreur en
/// ligne qui ne recoit plus rien conclut que l'application est cassee, pas
/// qu'il a eteint son propre signal. L'interrupteur coupe donc le bruit, pas
/// l'avertissement.
final sonDOffreProvider = NotifierProvider<SonDOffre, bool>(SonDOffre.new);
