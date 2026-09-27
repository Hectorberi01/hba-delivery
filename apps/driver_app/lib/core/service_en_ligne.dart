import 'package:flutter_foreground_task/flutter_foreground_task.dart';

import 'plantages.dart';

/// Ce qui maintient le livreur VIVANT POUR LE DISPATCH quand l'écran s'éteint.
///
/// LE PROBLÈME, ET CE N'EST PAS CELUI QU'ON CROIT. Un livreur en ligne qui
/// range son téléphone cesse d'exister pour le moteur au bout de deux minutes :
/// le service Driver écarte des recherches toute position plus vieille que
/// `DriverLocations:FreshnessSeconds`. On croit qu'il faut une notification
/// poussée pour l'atteindre ; il faut d'abord qu'il RESTE ÉLIGIBLE, sans quoi
/// il n'y a aucune offre à lui pousser.
///
/// CE SERVICE N'EXÉCUTE RIEN, ET C'EST TOUT SON INTÉRÊT.
///
/// Un service de premier plan donne au PROCESSUS la priorité « premier plan ».
/// Les minuteries Dart de l'isolat principal — le battement de position, le
/// sondage d'offre — vivent sur la boucle d'événements, pas sur le pipeline
/// d'images : Android cesse de demander des images quand l'écran s'éteint,
/// mais les minuteries continuent tant que le processus tient. Le service les
/// fait tenir.
///
/// POURQUOI L'ISOLAT DU SERVICE NE PARLE PAS AU RÉSEAU. Il le pourrait, et
/// c'était la première idée. Elle se heurte à un fait : le jeton d'accès dure
/// QUINZE MINUTES, et le jeton de rafraîchissement NE SERT QU'UNE FOIS — le
/// rejouer révoque toute la session. Deux isolats qui rafraîchissent chacun de
/// leur côté finissent par déconnecter le livreur au milieu d'une course, sans
/// que rien ne dise pourquoi. Tout le réseau reste donc là où il a toujours
/// été : dans l'isolat principal, derrière l'ApiClient et son rafraîchissement
/// à un seul vol.
///
/// CE QUE CELA NE COUVRE PAS, ET IL FAUT LE DIRE : si le livreur BALAIE
/// l'application hors des récentes, l'isolat principal meurt. Le service
/// s'arrête alors avec lui plutôt que de laisser une notification qui promet
/// une présence qui n'existe plus.
abstract final class ServiceEnLigne {
  static const _canal = 'hba_livreur_en_ligne';

  /// À appeler une fois au démarrage, avant tout `demarrer`.
  static void preparer() {
    FlutterForegroundTask.init(
      androidNotificationOptions: AndroidNotificationOptions(
        channelId: _canal,
        channelName: 'Livreur en ligne',
        channelDescription:
            "Indique que vous êtes en ligne et que HBA peut vous proposer "
            'des courses.',

        // DISCRÈTE, PAS SILENCIEUSE. Elle doit rester visible — c'est elle qui
        // dit au livreur qu'il est en ligne, et c'est la contrepartie honnête
        // du suivi. Mais elle ne doit ni sonner ni vibrer : le seul son qui
        // compte est celui d'une offre.
        channelImportance: NotificationChannelImportance.LOW,
        priority: NotificationPriority.LOW,
      ),
      iosNotificationOptions: const IOSNotificationOptions(),

      // SOIXANTE SECONDES, PARCE QUE L'ISOLAT N'A RIEN À FAIRE. Le réveil
      // périodique est imposé par le paquet ; le rendre rare coûte moins de
      // batterie et ne retire rien, puisque le vrai travail se fait ailleurs.
      foregroundTaskOptions: ForegroundTaskOptions(
        eventAction: ForegroundTaskEventAction.repeat(60000),
        autoRunOnBoot: false,
        allowWakeLock: true,
        allowWifiLock: false,
      ),
    );
  }

  /// « isRunningService » INTERROGE LE SYSTEME, il ne lit pas un drapeau local
  /// — d'ou le Future. C'est la bonne reponse : le service peut avoir ete tue
  /// par Android ou arrete par le livreur depuis la notification, sans que
  /// l'application en sache rien. Un booleen garde en memoire aurait menti.
  static Future<bool> get demarre => FlutterForegroundTask.isRunningService;

  /// Demande l'autorisation de notifier, exigée depuis Android 13.
  ///
  /// UN REFUS N'EMPÊCHE PAS DE TRAVAILLER, et c'est voulu : le livreur
  /// continue en mode « application ouverte », comme avant. On ne le bloque
  /// pas sur un réglage système qu'il peut changer plus tard.
  static Future<bool> autorisationNotification() async {
    try {
      final etat = await FlutterForegroundTask.checkNotificationPermission();
      if (etat == NotificationPermission.granted) return true;

      return await FlutterForegroundTask.requestNotificationPermission() ==
          NotificationPermission.granted;
    } on Object catch (erreur, pile) {
      Plantages.noter(erreur, pile, contexte: 'autorisation de notification');
      return false;
    }
  }

  /// Au passage en ligne.
  static Future<void> demarrer() async {
    if (await demarre) return;

    try {
      await FlutterForegroundTask.startService(
        notificationTitle: 'Vous êtes en ligne',
        notificationText: 'HBA peut vous proposer des courses.',
        callback: demarrerLaTacheDePresence,
      );
      Plantages.trace('service de presence demarre');
    } on Object catch (erreur, pile) {
      // L'APPLICATION CONTINUE SANS LUI. Le livreur retombe sur le
      // comportement d'avant — en ligne tant que l'écran est ouvert —, ce qui
      // est moins bon mais reste utilisable. Le faire échouer à passer en
      // ligne serait pire.
      Plantages.noter(erreur, pile, contexte: 'demarrage du service de presence');
    }
  }

  /// Au passage hors ligne.
  static Future<void> arreter() async {
    if (!await demarre) return;

    try {
      await FlutterForegroundTask.stopService();
      Plantages.trace('service de presence arrete');
    } on Object catch (erreur, pile) {
      Plantages.noter(erreur, pile, contexte: 'arret du service de presence');
    }
  }

  /// Change le texte sous le titre. Sert à dire la vérité quand elle change —
  /// « hors de portée », par exemple, quand la position ne part plus.
  static Future<void> dire(String texte) async {
    if (!await demarre) return;

    try {
      await FlutterForegroundTask.updateService(notificationText: texte);
    } on Object {
      // Un texte de notification qui ne change pas ne mérite pas un rapport.
    }
  }
}

/// Le point d'entrée de l'isolat du service.
///
/// « vm:entry-point » EST OBLIGATOIRE : sans cette annotation, la compilation
/// en mode release supprime cette fonction — elle n'est appelée par personne
/// dans le code Dart, c'est Android qui la réclame — et le service démarre sur
/// un point d'entrée introuvable.
@pragma('vm:entry-point')
void demarrerLaTacheDePresence() {
  FlutterForegroundTask.setTaskHandler(_TacheDePresence());
}

/// Elle ne fait rien, et c'est écrit exprès.
///
/// Voir l'en-tête de [ServiceEnLigne] : ce service existe pour la priorité du
/// processus, pas pour exécuter du travail. Y mettre le battement de position
/// dupliquerait le réseau dans un second isolat, avec un jeton qui expire en
/// quinze minutes et un rafraîchissement à usage unique.
///
/// LES TROIS SIGNATURES CI-DESSOUS SONT CELLES DE LA 8.17, VÉRIFIÉES PAR
/// L'ANALYSEUR. Je les avais d'abord écrites de mémoire, et « onDestroy »
/// prenait un paramètre de trop. C'est précisément parce qu'elles sont vides
/// que la correction a coûté une ligne : un jour où le paquet changera de
/// majeure, elle en coûtera autant.
class _TacheDePresence extends TaskHandler {
  @override
  Future<void> onStart(DateTime timestamp, TaskStarter starter) async {}

  @override
  void onRepeatEvent(DateTime timestamp) {}

  @override
  Future<void> onDestroy(DateTime timestamp) async {}
}
