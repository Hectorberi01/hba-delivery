import 'dart:async';

// « PlatformDispatcher » VIENT DE dart:ui, ET L'IMPORT RESTE EXPLICITE.
// foundation.dart reexporte aujourd'hui une liste choisie de noms de dart:ui,
// et cette liste a deja change d'une version de Flutter a l'autre. S'en
// remettre a elle donnerait un jour une erreur de compilation qui parle d'un
// nom inconnu et pas du tout de la cause. L'analyseur juge l'import inutile
// parce qu'il regarde la version installee ; on le fait taire ici plutot que
// de supprimer la ligne.
// ignore: unnecessary_import
import 'dart:ui' show PlatformDispatcher;

import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_crashlytics/firebase_crashlytics.dart';
import 'package:flutter/foundation.dart';

/// Rapport de plantage.
///
/// POURQUOI IL FALLAIT EN METTRE UN. Quand l'application se fermera toute
/// seule chez un livreur a Cotonou, personne ne le saura : un livreur
/// n'appelle pas pour dire « ca a ferme », il rouvre et reessaie. Le defaut
/// peut durer des mois, coûter des courses tous les jours, et n'apparaitre
/// dans aucun journal — parce que le journal, ici, est dans sa poche.
///
/// TOUTE L'APPLICATION PASSE PAR CETTE CLASSE, ET JAMAIS PAR FIREBASE EN
/// DIRECT. Deux raisons. La premiere est qu'un outil de ce genre se remplace :
/// le jour ou l'on changera, il y aura un fichier a reecrire, pas cinquante
/// imports a chasser. La seconde est plus immediate — sans projet Firebase
/// configure, `FirebaseCrashlytics.instance` leve, et l'application entiere
/// tomberait pour un outil de diagnostic. Ici, tout devient silencieux.
///
/// RIEN NE PART EN DEBOGAGE. Les plantages d'un developpeur qui essaie des
/// choses noieraient ceux des livreurs, qui sont les seuls a compter.
abstract final class Plantages {
  static bool _actif = false;

  /// Vrai quand les rapports partent reellement. Sert aux ecrans qui
  /// voudraient le dire, et aux tests.
  static bool get actif => _actif;

  /// A appeler avant `runApp`, et une seule fois.
  ///
  /// ELLE NE LEVE JAMAIS. Une application qui refuse de demarrer parce que son
  /// outil de diagnostic n'est pas configure serait une plaisanterie : le
  /// diagnostic est la pour servir l'application, pas l'inverse.
  static Future<void> demarrer() async {
    try {
      await Firebase.initializeApp();

      // LA COLLECTE EST COUPEE EN DEBOGAGE, ET C'EST UN REGLAGE PERSISTANT
      // cote natif : il survit au redemarrage de l'application, d'ou l'appel a
      // chaque lancement plutot qu'une seule fois.
      await FirebaseCrashlytics.instance
          .setCrashlyticsCollectionEnabled(!kDebugMode);

      _actif = true;
    } on Object catch (erreur) {
      // Cas normal tant qu'aucun projet Firebase n'est configure : le fichier
      // google-services.json manque. On le dit dans la console de
      // developpement, et l'application continue sans rapport de plantage.
      debugPrint('Rapport de plantage indisponible : $erreur');
      _actif = false;
    }
  }

  /// Branche les deux robinets par lesquels une erreur non rattrapee sort de
  /// Flutter.
  ///
  /// IL EN FAUT BIEN DEUX, ET C'EST LA MOITIE DES RAPPORTS QUI SE JOUE LA.
  /// `FlutterError.onError` attrape ce qui casse pendant la construction ou le
  /// dessin d'un widget ; `PlatformDispatcher.onError` attrape tout le reste —
  /// une exception dans un `Future` qui n'a pas de `catch`, typiquement un
  /// appel reseau oublie. Ne brancher que le premier laisse passer la
  /// categorie la plus frequente.
  static void brancher() {
    final flutterOriginal = FlutterError.onError;

    FlutterError.onError = (details) {
      // L'AFFICHAGE EN CONSOLE EST CONSERVE : sans cela, un developpeur ne
      // verrait plus ses propres erreurs dans son terminal.
      flutterOriginal?.call(details);

      if (_actif) FirebaseCrashlytics.instance.recordFlutterFatalError(details);
    };

    PlatformDispatcher.instance.onError = (erreur, pile) {
      if (_actif) {
        unawaited(
          FirebaseCrashlytics.instance.recordError(erreur, pile, fatal: true),
        );
      }

      // VRAI VEUT DIRE « C'EST TRAITE ». Rendre faux laisserait l'erreur
      // remonter jusqu'au moteur, qui l'affiche puis, sur certaines
      // plateformes, arrete l'isolat — un plantage de plus, provoque par le
      // rapporteur de plantages.
      return true;
    };
  }

  /// Une erreur rattrapee, mais qui meritait d'etre connue.
  ///
  /// POUR CE QUI EST AVALE EN SILENCE. L'application attrape beaucoup et se
  /// tait beaucoup, souvent a raison — un battement de position rate n'a pas a
  /// s'afficher. Mais « ne pas le montrer au livreur » et « ne pas le savoir »
  /// sont deux choses differentes, et c'est la seconde qui fait qu'un defaut
  /// dure six mois.
  static void noter(Object erreur, StackTrace? pile, {String? contexte}) {
    if (!_actif) return;

    unawaited(FirebaseCrashlytics.instance.recordError(
      erreur,
      pile,
      reason: contexte,
      fatal: false,
    ));
  }

  /// Une miette de chemin, gardee avec le prochain rapport.
  ///
  /// C'EST CE QUI TRANSFORME UNE PILE D'APPELS EN HISTOIRE. Une trace nue dit
  /// ou le code a casse ; les dernieres miettes disent ce que le livreur
  /// faisait — « passe en ligne », « offre recue », « colis remis ». La
  /// difference entre un defaut reproductible et un defaut qu'on regarde
  /// pendant trois semaines.
  ///
  /// RIEN DE PERSONNEL N'Y ENTRE : pas de nom, pas de telephone, pas
  /// d'adresse, pas de code de remise. Une miette dit une ETAPE, jamais un
  /// contenu.
  static void trace(String etape) {
    if (!_actif) return;
    unawaited(FirebaseCrashlytics.instance.log(etape));
  }

  /// Rattache les rapports a venir a un livreur.
  ///
  /// L'IDENTIFIANT, PAS LE NOM NI LE TELEPHONE. Il suffit au support pour
  /// retrouver les plantages de quelqu'un qui appelle, et il ne dit rien a qui
  /// le lirait sans acces a la base. Efface a la deconnexion.
  static void livreur(String? identifiant) {
    if (!_actif) return;

    unawaited(
      FirebaseCrashlytics.instance.setUserIdentifier(identifiant ?? ''),
    );
  }
}
