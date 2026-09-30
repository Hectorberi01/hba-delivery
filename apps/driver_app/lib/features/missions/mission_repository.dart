import 'package:hba_core/hba_core.dart';

import '../../core/file_actions.dart';
import 'models.dart';

/// Ce que devient une offre qu'on accepte.
///
/// DEUX « RIEN » QUI N'ONT RIEN A VOIR, ET LES CONFONDRE COUTAIT UNE COURSE.
/// « Un autre livreur a ete plus rapide » est un refus du serveur — un 409
/// avec son code. « La course n'est pas encore visible » est un decalage de
/// propagation entre Dispatch et Delivery. Un seul « null » pour les deux
/// faisait annoncer une defaite au livreur qui venait de gagner.
class Acceptation {
  const Acceptation({this.mission, this.perdue = false});

  /// La course, quand elle est deja lisible. Null pendant le decalage.
  final Mission? mission;

  /// Vrai seulement si le serveur a refuse : quelqu'un d'autre l'a prise.
  final bool perdue;
}

/// Acces aux offres et aux courses.
///
/// Chaque action mutante porte sa propre cle d'idempotence et son horodatage
/// client. C'est ce qui rend le rejeu d'une action mise en file inoffensif :
/// deux envois du meme « collecte » ne produisent qu'un seul effet.
/// Ce qu'une action de course renvoie.
///
/// DEUX ISSUES, ET LES CONFONDRE SERAIT MENTIR AU LIVREUR. Soit le serveur a
/// repondu et rend la course a jour ; soit il n'a pas repondu et l'action est
/// en file. Dans le second cas il n'y a PAS de course a jour — on ne sait pas
/// ce que le serveur en pense —, et le rendre quand meme obligerait a inventer
/// un etat.
class Issue {
  const Issue({this.mission, this.miseEnFile = false});

  /// La course telle que le serveur la rend. Nulle quand l'action est en file.
  final Mission? mission;

  /// L'action attend le reseau. Elle partira toute seule.
  final bool miseEnFile;
}

class MissionRepository {
  MissionRepository(this._api, this._file);

  final ApiClient _api;
  final FileDActions _file;

  Future<void> goOnline({required double latitude, required double longitude}) =>
      _api.post(
        '/presence/online',
        body: {'latitude': latitude, 'longitude': longitude},
        idempotencyKey: ApiClient.newIdempotencyKey(),
      );

  Future<void> goOffline() => _api.post(
        '/presence/offline',
        idempotencyKey: ApiClient.newIdempotencyKey(),
      );

  Future<void> pushPosition({
    required double latitude,
    required double longitude,
    required DateTime capturedAt,
  }) =>
      _api.post(
        '/position',
        body: {
          'latitude': latitude,
          'longitude': longitude,
          'capturedAt': capturedAt.toUtc().toIso8601String(),
        },
        idempotencyKey: ApiClient.newIdempotencyKey(),
      );

  /// Offre courante. Appelee au retour de l'application : un livreur ne porte
  /// qu'une offre a la fois, et c'est le serveur qui fait foi.
  Future<Offer?> currentOffer() async {
    final data = await _api.get('/offers/current');
    return Offer.fromJson(data);
  }

  /// Accepte une offre.
  ///
  /// SEUL LE 409 SIGNIFIE « PERDUE ». C'est le serveur qui tranche, et il le
  /// dit par un code. Tout le reste — y compris une course encore invisible —
  /// veut dire que l'offre est gagnee.
  Future<Acceptation> acceptOffer(String offerId) async {
    try {
      await _api.post(
        '/offers/$offerId/accept',
        idempotencyKey: 'accept:$offerId',
        body: const <String, Object?>{},
      );
    } on ApiException catch (error) {
      if (error.isConflict) return const Acceptation(perdue: true);
      rethrow;
    }

    // LA COURSE N'EST PAS VISIBLE A LA SECONDE OU ELLE EST GAGNEE.
    //
    // L'acceptation est tranchee par Dispatch, qui publie « offre acceptee » ;
    // Delivery l'apprend par Kafka et n'affecte le livreur qu'ensuite. Entre
    // les deux, « GET /missions » ne rend rien — quelques centaines de
    // millisecondes en general, davantage si le consommateur a pris du retard.
    //
    // ON PATIENTE, ON NE CONCLUT PAS. C'est cette attente qui manquait : une
    // premiere lecture vide etait lue comme « un autre livreur a ete plus
    // rapide », et le livreur s'entendait dire qu'il venait de perdre la
    // course qu'il avait sous les yeux.
    for (var essai = 0; essai < 6; essai += 1) {
      final mission = await currentMission();
      if (mission != null) return Acceptation(mission: mission);

      await Future<void>.delayed(const Duration(milliseconds: 700));
    }

    // Gagnee malgre tout : l'ecran le dira sans pretendre connaitre la course.
    return const Acceptation();
  }

  Future<void> declineOffer(String offerId, {String reason = ''}) => _api.post(
        '/offers/$offerId/decline',
        body: {'reason': reason},
        idempotencyKey: 'decline:$offerId',
      );

  /// Toutes les courses du livreur, la plus recente d'abord.
  ///
  /// LE SERVICE EN REND VINGT-CINQ ET LE BFF N'EXPOSE PAS LA SUITE. Ce n'est
  /// pas un oubli de cet appel : GET /missions appelle ListDeliveries avec
  /// PageSize = 25 et ne transmet aucun jeton de page. L'ecran le dit plutot
  /// que de laisser croire a un historique complet.
  Future<List<Mission>> allMissions() async {
    final data = await _api.get('/missions');
    final items = data['deliveries'];
    if (items is! List) return const [];

    final missions = <Mission>[
      for (final item in items)
        if (item is Map<String, dynamic>) Mission.fromJson(item),
    ];

    // Le tri est fait ici parce que le contrat ne promet aucun ordre. S'y
    // fier marcherait aujourd'hui et se verrait mal le jour ou il change.
    missions.sort((a, b) {
      final da = a.dateAffichee;
      final db = b.dateAffichee;
      if (da == null && db == null) return 0;
      if (da == null) return 1;
      if (db == null) return -1;
      return db.compareTo(da);
    });

    return missions;
  }

  /// Course en cours, s'il y en a une.
  Future<Mission?> currentMission() async {
    final data = await _api.get('/missions');
    final items = data['deliveries'];
    if (items is! List) return null;

    for (final item in items) {
      if (item is Map<String, dynamic>) {
        final mission = Mission.fromJson(item);
        if (mission.isOpen) return mission;
      }
    }

    return null;
  }

  Future<Issue> markArrived(String missionId) =>
      _action(missionId, 'arrived', const <String, Object?>{});

  Future<Issue> markPickedUp(String missionId) =>
      _action(missionId, 'picked-up', const <String, Object?>{});

  /// Confirme la remise.
  ///
  /// LE CODE VIENT DU DESTINATAIRE, dicte au livreur. Il n'est ni affiche ni
  /// journalise. Il est en revanche CONSERVE tant que l'action attend le
  /// reseau — dans le coffre chiffre du telephone, et efface des qu'elle part.
  /// Sans cela, une remise faite dans un sous-sol serait perdue.
  Future<Issue> confirmDelivery(String missionId, String otp) =>
      _action(missionId, 'deliver', {'otp': otp});

  /// Declare que la course ne peut pas aboutir.
  ///
  /// LE MOTIF EST LIBRE, ET L'ECRAN PROPOSE DES FORMULATIONS. Aucune liste
  /// d'incidents n'est tranchee cote service : la figer ici la figerait aussi
  /// dans le contrat et dans la base, avant que qui que ce soit l'ait decidee.
  ///
  /// LA CLE PORTE LA COURSE ET RIEN D'AUTRE : un incident par course, et un
  /// rejeu ne peut pas en declarer deux.
  Future<Issue> declareIncident(String missionId, String reason) =>
      _action(missionId, 'incident', {'reason': reason});

  /// Rejoue ce qui attend. Rend ce qui est parti ET ce que le serveur a refuse.
  Future<BilanDeFile> viderLaFile() => _file.vider(_api);

  Future<int> get actionsEnAttente => _file.nombre;

  Future<Issue> _action(
    String missionId,
    String verb,
    Map<String, Object?> extra,
  ) async {
    final chemin = '/missions/$missionId/$verb';

    // Une action est identifiee par la course et l'etape : rejouer « collecte »
    // sur la meme course est sans effet, quel que soit le nombre d'envois.
    final cle = '$verb:$missionId';

    final corps = {
      ...extra,
      // HORODATAGE PRIS MAINTENANT, PAS AU REJEU. C'est l'instant ou le livreur
      // a appuye qui compte : une collecte faite a 14 h et envoyee a 15 h reste
      // une collecte de 14 h. Le service le borne (jamais dans le futur, pas
      // plus de 24 h dans le passe), donc un telephone mal regle ne reecrit pas
      // l'histoire de la course.
      'occurredAt': DateTime.now().toUtc().toIso8601String(),
    };

    try {
      final data = await _api.post(chemin, body: corps, idempotencyKey: cle);
      return Issue(mission: Mission.fromJson(data));
    } on OfflineException {
      await _file.pousser(ActionEnFile(
        chemin: chemin,
        corps: corps,
        cleIdempotence: cle,
        creeeLe: DateTime.now(),
      ));

      return const Issue(miseEnFile: true);
    }
  }
}
