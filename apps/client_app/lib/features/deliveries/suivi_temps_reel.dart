import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_core/hba_core.dart';

import '../../core/providers.dart';

/// Ce que le temps reel apporte a un ecran de suivi.
///
/// DEUX CHOSES, ET PAS UNE DE PLUS : ou se trouve le livreur, et si la connexion
/// tient. L'ETAT DE LA MISSION N'EST PAS LA — le serveur l'envoie, mais ce
/// notificateur se contente de relire la mission par la route REST, qui est le
/// seul endroit ou la matrice de visibilite est appliquee. Voir le commentaire de
/// « Trames » cote serveur : deux trames sur trois sont des signaux, pas des
/// donnees.
class SuiviEnDirect {
  const SuiviEnDirect({
    this.position,
    this.vuA,
    this.etat = EtatDuTempsReel.coupe,
  });

  /// Derniere position connue du livreur. Nulle avant le premier battement.
  final LatLng? position;

  /// Quand le serveur a recu ce point. CE N'EST PAS « MAINTENANT » : sur un
  /// reseau qui va et vient, un point peut avoir trente secondes, et l'ecran doit
  /// pouvoir le dire au lieu de laisser croire a une pastille vivante.
  final DateTimeOffsetLisible? vuA;

  final EtatDuTempsReel etat;

  bool get aUnPoint => position != null;

  SuiviEnDirect copier({
    LatLng? position,
    DateTimeOffsetLisible? vuA,
    EtatDuTempsReel? etat,
  }) =>
      SuiviEnDirect(
        position: position ?? this.position,
        vuA: vuA ?? this.vuA,
        etat: etat ?? this.etat,
      );
}

/// Un instant, et depuis combien de temps il dure.
///
/// POURQUOI PAS UN DateTime TOUT SEUL. Parce que l'ecran ne veut pas afficher une
/// heure, il veut dire « il y a 12 s ». Garder l'instant permet de recalculer cet
/// age a chaque reconstruction, au lieu de figer un texte qui vieillit en silence.
class DateTimeOffsetLisible {
  const DateTimeOffsetLisible(this.instant);

  final DateTime instant;

  Duration get age => DateTime.now().toUtc().difference(instant.toUtc());
}

/// Le notificateur d'un ecran de suivi.
///
/// UN PAR MISSION, ET IL S'EN VA AVEC L'ECRAN. Riverpod detruit la famille quand
/// plus personne ne l'ecoute, ce qui ferme la connexion : une socket ouverte par
/// un ecran ferme consomme la batterie et la donnee d'un telephone qui n'affiche
/// plus rien.
class SuiviTempsReel extends StateNotifier<SuiviEnDirect> {
  SuiviTempsReel({required TokenStore jetons, required this.nature, required this.missionId})
      : super(const SuiviEnDirect()) {
    _connexion = ConnexionTempsReel(
      urlDuHub: urlDuHub(apiBaseUrl, '/hubs/client'),
      jetons: jetons,
      surChangementDEtat: (etat) {
        if (mounted) {
          state = state.copier(etat: etat);
        }
      },
    );

    _connexion
      ..ecouter('position', _recevoirUnePosition)
      ..ecouter('etat', _recevoirUnEtat)
      // APRES UNE RECONNEXION, IL FAUT REDEMANDER A SUIVRE. Un groupe SignalR est
      // attache a une CONNEXION : la nouvelle repart sans aucun groupe, et un
      // ecran qui ne se reabonne pas reste branche et muet — pire qu'une coupure,
      // puisque l'indicateur affiche « en direct ».
      ..surReconnexion = _sAbonner;

    unawaited(_demarrer());
  }

  /// « course » ou « livraison ». Le hub refuse tout autre mot : un groupe mal
  /// nomme enverrait la position d'un livreur a l'ecran d'un inconnu, donc le
  /// serveur ne devine pas.
  final String nature;

  final String missionId;

  late final ConnexionTempsReel _connexion;

  /// Ce qu'il faut faire quand l'etat de la mission a change.
  ///
  /// LE NOTIFICATEUR NE RELIT PAS LA MISSION LUI-MEME : il ne connait pas le
  /// depot, et surtout il n'a pas a decider quand un ecran se rafraichit. Il
  /// signale, l'ecran relit.
  void Function()? surChangementDEtat;

  Future<void> _demarrer() async {
    await _connexion.demarrer();
    await _sAbonner();
  }

  Future<void> _sAbonner() async {
    final accorde = await _connexion.appeler('SuivreLaMission', [nature, missionId]);

    if (!accorde && mounted) {
      // L'ABONNEMENT A ETE REFUSE, OU LA CONNEXION N'ETAIT PAS PRETE. Dans les
      // deux cas l'ecran ne doit PAS afficher « en direct » : le sondage reste la
      // seule source, et un indicateur vert mentirait sur ce que l'ecran sait.
      state = state.copier(etat: EtatDuTempsReel.coupe);
    }
  }

  void _recevoirUnePosition(Map<String, dynamic> trame) {
    // LA TRAME NE PORTE QUE LA POSITION — ni cap, ni vitesse, ni precision. C'est
    // le point 37 : « ou est-il » suffit a decider d'attendre, et la vitesse d'un
    // homme au travail est une donnee de surveillance. Si ces champs
    // apparaissaient ici un jour, c'est que la decision aurait change.
    final latitude = trame['latitude'];
    final longitude = trame['longitude'];

    if (latitude is! num || longitude is! num) {
      return;
    }

    // ON VERIFIE QUE LA TRAME PARLE BIEN DE CETTE MISSION. Le serveur n'envoie
    // qu'aux abonnes du bon groupe, donc cela ne devrait jamais mordre ; mais un
    // ecran qui afficherait la position d'une autre course serait un defaut
    // invisible, et le controle coute une comparaison de chaines.
    final mission = trame['missionId'];

    if (mission is String && mission.toLowerCase() != missionId.toLowerCase()) {
      return;
    }

    if (!mounted) {
      return;
    }

    state = state.copier(
      position: LatLng(latitude.toDouble(), longitude.toDouble()),
      vuA: DateTimeOffsetLisible(_instant(trame['vuA'])),
    );
  }

  void _recevoirUnEtat(Map<String, dynamic> trame) {
    final mission = trame['missionId'];

    if (mission is String && mission.toLowerCase() != missionId.toLowerCase()) {
      return;
    }

    // ON NE LIT PAS L'ETAT DE LA TRAME, ET C'EST VOULU. Elle dit « ceci a
    // change » ; la verite est dans la mission, que l'ecran relit par la route
    // REST — le seul endroit ou la matrice de visibilite est appliquee. Ecrire
    // l'etat d'apres la trame ferait exister deux sources, dont celle-ci ne porte
    // ni le nom du livreur, ni son telephone, ni le code de remise.
    surChangementDEtat?.call();
  }

  static DateTime _instant(Object? valeur) {
    if (valeur is String) {
      final lu = DateTime.tryParse(valeur);
      if (lu != null) return lu;
    }

    // SANS HORODATAGE LISIBLE, ON PREND MAINTENANT. Le point vient d'arriver :
    // c'est faux de quelques centaines de millisecondes, alors qu'une date nulle
    // obligerait tout l'affichage a traiter un cas qui ne se produit pas.
    return DateTime.now().toUtc();
  }

  @override
  void dispose() {
    unawaited(_connexion.arreter());
    super.dispose();
  }
}

/// Le suivi en direct d'une LIVRAISON.
///
/// UNE FAMILLE PAR IDENTIFIANT : deux ecrans de suivi ouverts en meme temps —
/// c'est possible, le point 35 autorise plusieurs missions a la fois — ont chacun
/// leur connexion et leur position.
final suiviLivraisonProvider =
    StateNotifierProvider.family<SuiviTempsReel, SuiviEnDirect, String>(
  (ref, deliveryId) => SuiviTempsReel(
    jetons: ref.watch(tokenStoreProvider),
    nature: 'livraison',
    missionId: deliveryId,
  ),
);

/// Le suivi en direct d'une COURSE de personnes.
///
/// AUCUN ECRAN NE L'UTILISE ENCORE, et ce n'est pas un oubli : cette application
/// n'a pas de suivi de course — le transport de personnes existe cote serveur
/// depuis le 1er octobre 2026, ses ecrans clients non. Le fournisseur est ecrit
/// maintenant parce que la SEULE difference avec la livraison est le mot
/// « course » passe au hub, et que le decouvrir plus tard aurait invite a
/// recopier le notificateur au lieu de le reutiliser.
final suiviCourseProvider =
    StateNotifierProvider.family<SuiviTempsReel, SuiviEnDirect, String>(
  (ref, rideId) => SuiviTempsReel(
    jetons: ref.watch(tokenStoreProvider),
    nature: 'course',
    missionId: rideId,
  ),
);
