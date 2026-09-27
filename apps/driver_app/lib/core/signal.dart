import 'dart:async';

import 'package:audioplayers/audioplayers.dart';
import 'package:flutter/services.dart';
import 'package:vibration/vibration.dart';

import 'plantages.dart';

/// Ce qui attire l'attention du livreur.
///
/// UNE OFFRE QUI EXPIRE EN SILENCE EST UNE COURSE PERDUE. Le livreur qui
/// attend garde l'application ouverte mais ne la regarde pas : il parle a
/// quelqu'un, il mange, il surveille sa moto. Rien, jusqu'ici, ne le
/// prevenait — la feuille d'offre s'ouvrait, le compte a rebours tournait, et
/// elle se refermait toute seule.
///
/// DEUX CANAUX, PARCE QU'AUCUN NE SUFFIT SEUL. Le son ne passe pas dans une
/// rue de Cotonou a midi, ni quand le telephone est en silencieux ; la
/// vibration ne se sent pas quand le telephone est pose sur une table. Les
/// deux ensemble couvrent les deux situations, et chacun tombe sans emporter
/// l'autre.
///
/// CE N'EST PAS UN REMPLACANT DE LA NOTIFICATION POUSSEE, et il ne faut pas le
/// croire. Tout ceci ne joue que si l'application est au PREMIER PLAN : c'est
/// une minuterie de l'ecran d'accueil qui va chercher les offres. Un livreur
/// dont le telephone est verrouille ne recoit rien du tout — ni son, ni offre.
/// Voir le point 1.1 de l'audit de l'application livreur.
abstract final class Signal {
  /// UN SEUL LECTEUR, REUTILISE. En creer un par offre laisse derriere lui un
  /// canal audio natif qui n'est jamais libere ; au bout d'une journee de
  /// travail, le son cesse de partir sans que rien ne le signale.
  static AudioPlayer? _lecteur;

  /// Vrai tant qu'une offre reclame l'attention du livreur.
  static bool _actif = false;

  /// LE RYTHME D'UN TELEPHONE QUI SONNE, PAS D'UNE ALARME. Le motif de
  /// vibration dure 1,9 s et le son 1,16 s ; trois secondes laissent donc une
  /// seconde de calme entre deux appels. Sans elle, le signal devient un
  /// bourdonnement continu dont on cesse d'entendre le debut — et c'est le
  /// debut qui fait lever les yeux.
  static const _cadence = Duration(seconds: 3);

  /// Plafond absolu de l'insistance.
  ///
  /// GARDE-FOU, PAS REGLAGE. La duree qui compte est celle de l'offre, et
  /// c'est la feuille qui arrete le signal en appelant [silence]. Ce plafond
  /// n'existe que pour le jour ou cet appel manquera — une exception avalee,
  /// un ecran detruit autrement que prevu. Un telephone qui vibre sans fin
  /// dans la poche d'un livreur est un defaut qu'on ne rattrape pas a
  /// distance, et qui lui coute sa batterie et sa journee.
  static const _plafond = Duration(seconds: 120);

  /// La sonnerie d'une offre, REPETEE JUSQU'A CE QU'ON L'ARRETE.
  ///
  /// UN SEUL COUP NE SUFFISAIT PAS, ET C'EST LE DEFAUT QU'ON CORRIGE ICI. Le
  /// signal partait une fois, a la seconde ou l'offre arrivait. Un livreur qui
  /// parlait a quelqu'un, qui avait la tete dans son coffre ou qui venait de
  /// poser son telephone ne l'entendait pas — et rien ensuite ne le rappelait
  /// pendant que le compte a rebours tournait.
  ///
  /// IL SE TAIT DE TROIS FACONS : [silence] appele a l'acceptation ou au
  /// refus, [silence] appele quand le compte a rebours atteint zero, et le
  /// plafond ci-dessus si aucun des deux n'arrive.
  ///
  /// « avecSon » VIENT DU REGLAGE DU LIVREUR, ET IL NE PORTE QUE SUR LE SON.
  /// La vibration part dans tous les cas : un livreur qui coupe le bruit veut
  /// le silence, pas se rendre injoignable. Voir le point 18 des points a
  /// trancher pour le raisonnement complet.
  static Future<void> offre({bool avecSon = true}) async {
    // DEUX BOUCLES EN PARALLELE SERAIENT DEUX FOIS LE SIGNAL, decalees d'un
    // temps quelconque. Une offre qui arrive pendant qu'une autre insiste ne
    // relance donc rien.
    if (_actif) return;

    _actif = true;
    final limite = DateTime.now().add(_plafond);

    while (_actif && DateTime.now().isBefore(limite)) {
      unawaited(_vibrer());
      if (avecSon) unawaited(_jouer('sons/offre.wav'));

      await Future<void>.delayed(_cadence);
    }

    // Sorti par le plafond et non par [silence] : on coupe quand meme.
    if (_actif) await silence();
  }

  /// Arrete le signal. Appelable a tout moment, meme si rien ne joue.
  static Future<void> silence() async {
    _actif = false;

    try {
      await Vibration.cancel();
    } on Object {
      // Greffon absent : il n'y avait alors rien a annuler.
    }

    try {
      await _lecteur?.stop();
    } on Object {
      // Lecteur deja libere.
    }
  }

  /// La vibration, EN TROIS COUPS ESPACES.
  ///
  /// Un coup unique se confond avec le retour tactile d'un bouton. Trois coups
  /// se lisent comme une sollicitation, et c'est le meme motif que celui des
  /// appels entrants — un code que tout le monde connait deja.
  ///
  /// « HapticFeedback » N'ETAIT PAS UNE VIBRATION, ET C'EST LE DEFAUT QU'ON
  /// CORRIGE ICI. Ce que la classe de Flutter declenche n'est pas le moteur de
  /// vibration : c'est le RETOUR TACTILE DE L'INTERFACE, celui d'un appui long
  /// sur une touche. Android le fait passer par « performHapticFeedback », qui
  /// obeit au reglage systeme « vibration au toucher » — desactive par defaut
  /// sur une bonne partie des Samsung. Sur ces telephones l'appel partait, ne
  /// levait aucune erreur, et ne produisait rien. Un signal qu'on ne peut pas
  /// distinguer d'une panne silencieuse n'est pas un signal.
  ///
  /// Le moteur de vibration proprement dit ignore ce reglage et fonctionne
  /// telephone en sourdine — c'est exactement ce qu'il faut ici.
  static Future<void> _vibrer() async {
    try {
      // « == true » ET NON UNE SIMPLE CONDITION : selon la version du greffon
      // cette methode rend « bool » ou « bool? ». La comparaison explicite
      // traverse les deux sans rien casser.
      if (await Vibration.hasVibrator() == true) {
        // Trois coups fermes de 400 ms, le dernier plus long pour fermer la
        // phrase. Les creux de 250 ms les separent assez pour qu'on les
        // compte au poignet.
        await Vibration.vibrate(pattern: const [0, 400, 250, 400, 250, 600]);
        return;
      }
    } on Object {
      // Greffon absent de la version installee sur ce telephone, ou refus du
      // systeme : on retombe sur le retour tactile ci-dessous, qui vaut mieux
      // que rien la ou il est actif.
    }

    for (var i = 0; i < 3; i++) {
      try {
        await HapticFeedback.heavyImpact();
      } on Object {
        return;
      }

      await Future<void>.delayed(const Duration(milliseconds: 280));
    }
  }

  static Future<void> _jouer(String actif) async {
    try {
      final lecteur = _lecteur ??= AudioPlayer();

      // LE CANAL DES ALARMES, ET LE COMMENTAIRE DISAIT DEJA CE QUE LE CODE NE
      // FAISAIT PAS. Il annoncait « alarme plutot que media » et la ligne
      // suivante demandait « notification » — or le volume des notifications
      // est celui que le telephone coupe en premier. Un livreur qui met son
      // telephone en sourdine coupait donc, sans le savoir, le seul signal qui
      // l'avertit d'une course. C'etait le cas sur l'appareil de test.
      //
      // Le canal des alarmes est le seul qui survive a la sourdine, et le seul
      // qui ne se mette pas en pause quand le livreur ecoute la radio ou un
      // guidage vocal — c'est-a-dire exactement pendant qu'il roule.
      //
      // IL N'Y A PAS ENCORE DE REGLAGE POUR L'ETEINDRE, et c'est a trancher :
      // se mettre en ligne vaut demande a etre derange, mais un livreur en
      // ligne qui veut le silence n'a aujourd'hui aucun moyen de l'obtenir
      // sans se mettre hors ligne.
      await lecteur.setAudioContext(
        AudioContext(
          android: const AudioContextAndroid(
            contentType: AndroidContentType.sonification,
            usageType: AndroidUsageType.alarm,
            audioFocus: AndroidAudioFocus.gainTransientMayDuck,
          ),
          iOS: AudioContextIOS(
            category: AVAudioSessionCategory.playback,
            options: const {AVAudioSessionOptions.mixWithOthers},
          ),
        ),
      );

      await lecteur.play(AssetSource(actif));
    } on Object catch (erreur, pile) {
      // LE SON EST UN ORNEMENT, LA VIBRATION EST LE SIGNAL. S'il tombe —
      // fichier absent, canal audio occupe, greffon non installe —, l'offre
      // s'affiche quand meme et le telephone a deja vibre.
      Plantages.noter(erreur, pile, contexte: 'lecture du son d\'offre');
    }
  }

  /// A appeler quand l'application se ferme. Sans cela, le canal audio natif
  /// reste ouvert.
  static Future<void> liberer() async {
    await silence();
    await _lecteur?.dispose();
    _lecteur = null;
  }
}
