import 'package:hba_core/hba_core.dart';

import '../../core/file_actions.dart';
import 'course_models.dart';
import 'models.dart';

/// Ce que devient une offre qu'on accepte.
///
/// DEUX « RIEN » QUI N'ONT RIEN A VOIR, ET LES CONFONDRE COUTAIT UNE COURSE.
/// « Un autre livreur a ete plus rapide » est un refus du serveur — un 409
/// avec son code. « La course n'est pas encore visible » est un decalage de
/// propagation entre Dispatch et Delivery. Un seul « null » pour les deux
/// faisait annoncer une defaite au livreur qui venait de gagner.
class Acceptation {
  const Acceptation({this.mission, this.course, this.perdue = false});

  /// La livraison, quand elle est deja lisible. Null pendant le decalage.
  final Mission? mission;

  /// La course de personnes, quand c'en etait une. L'UN OU L'AUTRE, JAMAIS LES
  /// DEUX : une offre porte une nature, et c'est elle qui dit lequel des deux
  /// champs a ete rempli.
  final CourseLivreur? course;

  /// Vrai seulement si le serveur a refuse : quelqu'un d'autre l'a prise.
  final bool perdue;

  /// La mission est-elle lisible, quelle que soit sa nature ?
  bool get lisible => mission != null || course != null;
}

/// Ce qu'une action de COURSE renvoie. Meme forme qu'[Issue], autre modele.
class IssueDeCourse {
  const IssueDeCourse({this.course, this.miseEnFile = false});

  /// La course telle que le serveur la rend. Nulle quand l'action est en file.
  final CourseLivreur? course;

  /// L'action attend le reseau. Elle partira toute seule.
  final bool miseEnFile;
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

  /// Envoie un point au service Location.
  ///
  /// LE CAP, LA VITESSE ET LA PRECISION SONT FACULTATIFS, ET LE NUL N'EST PAS
  /// ZERO. Un telephone a l'arret ne rend aucun cap ; zero degre, lui, veut dire
  /// « plein nord ». On n'envoie donc QUE ce que le GPS a donne : un champ absent
  /// du corps JSON dit au serveur « le telephone n'a rien dit », et c'est la
  /// difference qui empeche toutes les motos immobiles de pointer vers le nord sur
  /// la carte de l'exploitation.
  ///
  /// GEOLOCATOR LES DONNAIT DEJA. Les trois etaient dans l'objet Position depuis
  /// toujours, et cette methode n'en transmettait aucun : deux d'entre eux
  /// existaient meme au contrat gRPC de Driver sans que personne ne les remplisse.
  /// C'est pour cela qu'aucune carte n'a jamais pu orienter une fleche.
  Future<void> pushPosition({
    required double latitude,
    required double longitude,
    required DateTime capturedAt,
    double? headingDegrees,
    double? speedMps,
    double? accuracyMeters,
  }) =>
      _api.post(
        '/position',
        body: {
          'latitude': latitude,
          'longitude': longitude,
          'capturedAt': capturedAt.toUtc().toIso8601String(),
          if (headingDegrees != null) 'headingDegrees': headingDegrees,
          if (speedMps != null) 'speedMps': speedMps,
          if (accuracyMeters != null) 'accuracyMeters': accuracyMeters,
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
  ///
  /// CE QU'ON ATTEND DEPEND DE LA NATURE DE L'OFFRE, et le confondre coutait
  /// quatre secondes et un ecran vide : « GET /missions » ne rendra JAMAIS une
  /// course de personnes, donc attendre la dedans apres avoir accepte un passager
  /// revenait a attendre pour rien avant d'annoncer qu'on ne trouve rien.
  Future<Acceptation> acceptOffer(String offerId, {bool course = false}) async {
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
      if (course) {
        final acceptee = await courseActive();
        if (acceptee != null) return Acceptation(course: acceptee);
      } else {
        final mission = await currentMission();
        if (mission != null) return Acceptation(mission: mission);
      }

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

  /// La course de personnes en cours, s'il y en a une.
  ///
  /// 200 AVEC null, PAS 404 : « aucune course » est l'etat ordinaire d'un
  /// chauffeur en ligne, et la passerelle le dit ainsi. UNE SEULE, AU
  /// SINGULIER — il ne conduit qu'un vehicule.
  Future<CourseLivreur?> courseActive() async {
    final data = await _api.get('/rides/active');
    final brut = data['ride'];

    if (brut is! Map) return null;

    final course = CourseLivreur.lire(brut.cast<String, dynamic>());
    return course.etat.estOuverte ? course : null;
  }

  /// Declare l'etape suivante d'une course.
  Future<IssueDeCourse> avancerLaCourse(String courseId, EtapeDeCourse etape) =>
      _actionDeCourse(courseId, etape.verbe, const <String, Object?>{});

  /// Renonce a une course acceptee.
  Future<IssueDeCourse> annulerLaCourse(String courseId, {String motif = ''}) =>
      _actionDeCourse(courseId, 'cancel', {if (motif.isNotEmpty) 'reason': motif});

  /// Une action de course, mise en file si le reseau manque.
  ///
  /// AUCUN « occurredAt » ICI, CONTRAIREMENT AUX LIVRAISONS, et ce n'est pas un
  /// oubli : le contrat de Ride ne porte pas d'horodatage client — ses routes
  /// n'acceptent meme aucun corps. Une etape mise en file et partie vingt minutes
  /// plus tard sera donc datee de l'instant ou le serveur la recoit, pas de celui
  /// ou le chauffeur a appuye. Pour une livraison, le service borne et accepte un
  /// horodatage du telephone ; ici il faudrait l'ajouter au contrat.
  ///
  /// LE REJEU PEUT ETRE REFUSE, ET C'EST ACCEPTABLE. Ride ne lit pas la cle
  /// d'idempotence : un « demarrer » rejoue sur une course deja demarree est une
  /// transition invalide, que le service refuse franchement. Un refus visible vaut
  /// mieux qu'une seconde transition silencieuse.
  Future<IssueDeCourse> _actionDeCourse(
    String courseId,
    String verbe,
    Map<String, Object?> corps,
  ) async {
    final chemin = '/rides/$courseId/$verbe';

    // Une action est identifiee par la course et l'etape : la rejouer ne peut
    // pas en declarer deux.
    final cle = 'course-$verbe:$courseId';

    try {
      final data = await _api.post(chemin, body: corps, idempotencyKey: cle);
      return IssueDeCourse(course: CourseLivreur.lire(data));
    } on OfflineException {
      await _file.pousser(ActionEnFile(
        chemin: chemin,
        corps: corps,
        cleIdempotence: cle,
        creeeLe: DateTime.now(),
      ));

      return const IssueDeCourse(miseEnFile: true);
    }
  }

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
