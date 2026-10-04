import 'dart:async';

import 'package:signalr_netcore/signalr_client.dart';

import 'token_store.dart';

/// Ou en est la connexion de temps reel.
///
/// TROIS ETATS, ET LE TROISIEME EST CELUI QUI COMPTE POUR L'ECRAN. « Branche »
/// et « coupe » sont evidents ; « en train de revenir » existe parce qu'une
/// reconnexion sur un reseau beninois dure parfois dix secondes, et qu'un ecran
/// qui afficherait « hors ligne » pendant ce temps-la ferait croire a une panne
/// alors que tout est en train de se rattraper.
enum EtatDuTempsReel { coupe, enTrainDeRevenir, branche }

/// Une connexion a un hub de temps reel.
///
/// CE QUE CETTE CLASSE N'EST PAS : une source de verite. Elle apporte des
/// nouvelles plus vite que le sondage, et c'est tout. Si elle tombe, l'ecran
/// doit continuer de fonctionner en sondant — c'est pourquoi aucune de ses
/// methodes ne leve, et pourquoi son etat est lisible.
///
/// LE SONDAGE RESTE EN PLACE DANS LES DEUX APPLICATIONS, RALENTI. Le service de
/// diffusion consomme Kafka au plus recent et ne rejoue rien : un message emis
/// pendant une coupure est perdu pour toujours. Supprimer le sondage parce que le
/// temps reel marche reviendrait a echanger un ecran lent contre un ecran faux.
///
/// ELLE NE SE CONNECTE PAS TOUTE SEULE. L'appelant appelle [demarrer] quand il
/// est pret et [arreter] quand il s'en va : une connexion ouverte par un ecran
/// ferme consomme la batterie et la donnee d'un telephone qui n'affiche plus
/// rien.
class ConnexionTempsReel {
  ConnexionTempsReel({
    required String urlDuHub,
    required TokenStore jetons,
    void Function(EtatDuTempsReel etat)? surChangementDEtat,
  })  : _url = urlDuHub,
        _jetons = jetons,
        _surChangementDEtat = surChangementDEtat;

  final String _url;
  final TokenStore _jetons;
  final void Function(EtatDuTempsReel etat)? _surChangementDEtat;

  final Map<String, void Function(Map<String, dynamic> trame)> _ecouteurs = {};

  HubConnection? _connexion;
  bool _arretDemande = false;

  void Function()? _surReconnexion;

  EtatDuTempsReel _etat = EtatDuTempsReel.coupe;

  EtatDuTempsReel get etat => _etat;

  bool get estBranche => _etat == EtatDuTempsReel.branche;

  /// Ce qu'il faut refaire apres une reconnexion : se reabonner.
  ///
  /// IL FAUT LE DIRE EXPLICITEMENT parce que cette classe ne sait pas a quoi
  /// l'ecran s'est abonne. Le hub du livreur n'en a pas besoin — son inscription
  /// se fait a la connexion, cote serveur, d'apres son jeton — mais celui du
  /// client, si : c'est lui qui nomme la mission, et un groupe SignalR est attache
  /// a une CONNEXION, donc perdu des qu'elle est remplacee.
  set surReconnexion(void Function()? quoiFaire) => _surReconnexion = quoiFaire;

  /// Enregistre un ecouteur pour une methode du serveur.
  ///
  /// LES NOMS SONT CEUX DES TRAMES DU SERVEUR — « etat », « position »,
  /// « offre » — et ils font partie du contrat : les renommer la-bas casse ces
  /// ecouteurs sans qu'aucun compilateur ne s'en apercoive.
  ///
  /// A APPELER AVANT [demarrer]. Les ecouteurs sont reposes a chaque
  /// reconnexion ; en ajouter un apres coup marche aussi, mais celui-la ne sera
  /// pas repose si la connexion a deja ete construite.
  void ecouter(String methode, void Function(Map<String, dynamic> trame) quoiFaire) {
    _ecouteurs[methode] = quoiFaire;
    _brancherLEcouteur(methode, quoiFaire);
  }

  /// Ouvre la connexion. NE LEVE JAMAIS.
  ///
  /// UN ECHEC DE CONNEXION N'EST PAS UNE ERREUR POUR L'APPELANT : l'ecran
  /// continue en sondant. Le signaler par une exception obligerait chaque ecran a
  /// l'attraper pour ne rien en faire, et le premier qui oublierait planterait sur
  /// un reseau coupe.
  Future<void> demarrer() async {
    if (_connexion != null) {
      return;
    }

    _arretDemande = false;

    final connexion = HubConnectionBuilder()
        .withUrl(
          _url,
          options: HttpConnectionOptions(
            // LE JETON EST RELU A CHAQUE (RE)CONNEXION, et c'est necessaire : une
            // reconnexion peut arriver une heure plus tard, avec un jeton d'acces
            // deja renouvele par le client HTTP. Capturer la valeur une fois
            // ferait echouer toutes les reconnexions d'une longue session.
            accessTokenFactory: () async => _jetons.accessToken ?? '',
          ),
        )
        // LA RECONNEXION AUTOMATIQUE, AVEC DES DELAIS CROISSANTS. Sur un reseau
        // mobile qui va et vient, reessayer immediatement et sans fin reviendrait
        // a vider la batterie pour rien.
        .withAutomaticReconnect(retryDelays: [0, 2000, 5000, 10000, 20000, 30000])
        .build();

    connexion.onclose(({Exception? error}) {
      // « FERME » NE VEUT PAS DIRE « FINI » : la reconnexion automatique ne
      // couvre pas tous les cas, et une fermeture definitive se distingue d'un
      // arret demande par l'ecran.
      _changer(_arretDemande ? EtatDuTempsReel.coupe : EtatDuTempsReel.enTrainDeRevenir);
    });

    connexion.onreconnecting(({Exception? error}) {
      _changer(EtatDuTempsReel.enTrainDeRevenir);
    });

    connexion.onreconnected(({String? connectionId}) {
      _changer(EtatDuTempsReel.branche);

      // APRES UNE RECONNEXION, LES ABONNEMENTS DU SERVEUR SONT PERDUS.
      //
      // Les groupes SignalR sont attaches a une CONNEXION, pas a un utilisateur :
      // une nouvelle connexion, meme automatique, repart sans aucun groupe. Un
      // ecran qui suivait une mission doit donc redemander a la suivre, sinon il
      // reste branche et muet — le pire des deux mondes, puisque l'indicateur
      // affiche « en direct ».
      _surReconnexion?.call();
    });

    _connexion = connexion;

    for (final entree in _ecouteurs.entries) {
      _brancherLEcouteur(entree.key, entree.value);
    }

    _changer(EtatDuTempsReel.enTrainDeRevenir);

    try {
      await connexion.start();
      _changer(EtatDuTempsReel.branche);
    } on Exception {
      _changer(EtatDuTempsReel.coupe);
    }
  }

  /// Appelle une methode du hub. NE LEVE JAMAIS, et rend faux en cas d'echec.
  ///
  /// L'APPELANT DOIT REGARDER LE RESULTAT quand l'appel conditionne ce qu'il
  /// affiche : un abonnement refuse — mission qui n'est pas la sienne — rend faux,
  /// et l'ecran doit alors se contenter du sondage au lieu d'afficher un
  /// indicateur « en direct » mensonger.
  ///
  /// « List<Object> » ET NON « List<Object?> », parce que c'est ce que demande
  /// signalr_netcore. La nuance n'est pas cosmetique : un argument nul dans un appel
  /// de hub n'a pas de representation utile — le serveur recevrait une chaine vide ou
  /// un zero selon le type du parametre, et refuserait pour une raison illisible.
  /// Mieux vaut que le compilateur l'interdise.
  Future<bool> appeler(String methode, List<Object> arguments) async {
    final connexion = _connexion;

    if (connexion == null || connexion.state != HubConnectionState.Connected) {
      return false;
    }

    try {
      await connexion.invoke(methode, args: arguments);
      return true;
    } on Exception {
      return false;
    }
  }

  /// Ferme la connexion. A appeler quand l'ecran disparait.
  Future<void> arreter() async {
    _arretDemande = true;

    final connexion = _connexion;
    _connexion = null;

    if (connexion == null) {
      return;
    }

    try {
      await connexion.stop();
    } on Exception {
      // UNE FERMETURE QUI ECHOUE N'A AUCUNE CONSEQUENCE : l'objet est abandonne,
      // le systeme libere la socket. Lever ici ferait planter un dispose().
    }

    _changer(EtatDuTempsReel.coupe);
  }

  void _brancherLEcouteur(String methode, void Function(Map<String, dynamic> trame) quoiFaire) {
    final connexion = _connexion;

    if (connexion == null) {
      return;
    }

    connexion.on(methode, (arguments) {
      // UNE TRAME MAL FORMEE EST IGNOREE, PAS PROPAGEE. Le serveur envoie un seul
      // objet par appel ; si ce n'en est pas un, c'est que les deux cotes ne
      // parlent plus de la meme chose, et faire planter l'ecran du client n'y
      // changerait rien.
      if (arguments == null || arguments.isEmpty) {
        return;
      }

      final premier = arguments.first;

      if (premier is Map) {
        quoiFaire(Map<String, dynamic>.from(premier));
      }
    });
  }

  void _changer(EtatDuTempsReel nouvel) {
    if (_etat == nouvel) {
      return;
    }

    _etat = nouvel;
    _surChangementDEtat?.call(nouvel);
  }
}

/// L'adresse d'un hub, deduite de celle de l'API.
///
/// LES HUBS SONT A LA RACINE DE LA PASSERELLE, pas sous le prefixe de l'API :
/// « /hubs/client », et non « /api/client/v1/hubs/client ». Cette fonction
/// retire donc le chemin de l'adresse de base et pose celui du hub.
///
/// POURQUOI DEDUIRE PLUTOT QUE DE CONFIGURER UNE SECONDE ADRESSE. Parce qu'une
/// seconde variable de lancement se desynchronise : on change l'une pour une
/// recette, on oublie l'autre, et l'application parle a deux environnements a la
/// fois — les donnees de l'un, le temps reel de l'autre. Ici il n'y a qu'une
/// adresse a se tromper.
String urlDuHub(String baseApi, String chemin) {
  final uri = Uri.parse(baseApi);

  return uri.replace(path: chemin, query: null, fragment: null).toString();
}
