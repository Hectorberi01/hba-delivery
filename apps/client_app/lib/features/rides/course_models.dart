/// Modeles du transport de personnes, lus depuis le Client BFF.
///
/// LES ETATS ARRIVENT EN NOMS COURTS, contrairement a ceux des livraisons.
/// ClientRideEndpoints.Nommer rend « DRIVER_ASSIGNED » la ou la route des
/// livraisons relaie le protobuf brut et rend « DELIVERY_STATUS_DRIVER_ASSIGNED ».
/// Le lecteur ci-dessous accepte les deux formes : si la passerelle cessait un
/// jour de mettre en forme a la main, l'ecran continuerait de fonctionner.
library;

int _entier(Object? valeur) => switch (valeur) {
      final int v => v,
      final num v => v.toInt(),
      final String v => int.tryParse(v) ?? 0,
      _ => 0,
    };

int? _entierFacultatif(Object? valeur) =>
    valeur == null ? null : _entier(valeur);

DateTime? _date(Object? valeur) => valeur is String && valeur.isNotEmpty
    ? DateTime.tryParse(valeur)?.toLocal()
    : null;

String _texte(Object? valeur) => valeur is String ? valeur : '';

double _coordonnee(Object? point, String champ) => point is Map
    ? switch (point[champ]) {
        final num v => v.toDouble(),
        final String v => double.tryParse(v) ?? 0,
        _ => 0,
      }
    : 0;

/// Les vehicules qui transportent des personnes.
///
/// TROIS, ET PAS CINQ : le contrat de Ride ne connait ni la camionnette ni le
/// velo, parce qu'ils ne portent personne. La liste est donc plus courte que
/// celle d'un envoi de colis, et c'est voulu.
enum VehiculeDeCourse {
  moto('moto', 'Moto'),
  voiture('voiture', 'Voiture'),
  tricycle('tricycle', 'Tricycle');

  const VehiculeDeCourse(this.code, this.libelle);

  /// Le mot envoye a la passerelle. Elle accepte le francais comme l'anglais.
  final String code;

  final String libelle;

  /// Lit ce que le serveur rend, quelle que soit son orthographe.
  static VehiculeDeCourse? depuis(String brut) =>
      switch (brut.replaceAll(RegExp('[^A-Za-z]'), '').toLowerCase()) {
        'motorcycle' || 'moto' => moto,
        'car' || 'voiture' => voiture,
        'tricycle' => tricycle,
        _ => null,
      };

  static String libelleDe(String brut) => depuis(brut)?.libelle ?? '--';
}

/// L'etat d'une course, du cote du passager.
enum EtatDeCourse {
  inconnu,
  demandee,
  rechercheDuChauffeur,
  chauffeurAffecte,
  chauffeurEnApproche,
  chauffeurArrive,
  enCours,
  terminee,
  annulee,
  aucunChauffeur,
  paiementEchoue;

  static EtatDeCourse lire(Object? brut) {
    final nom = _texte(brut).toUpperCase();
    final court = nom.startsWith('RIDE_STATUS_') ? nom.substring(12) : nom;

    return switch (court) {
      'REQUESTED' => demandee,
      'SEARCHING_DRIVER' => rechercheDuChauffeur,
      'DRIVER_ASSIGNED' => chauffeurAffecte,
      'DRIVER_ARRIVING' => chauffeurEnApproche,
      'DRIVER_ARRIVED' => chauffeurArrive,
      'IN_PROGRESS' => enCours,
      'COMPLETED' => terminee,
      'CANCELLED' => annulee,
      'NO_DRIVER_AVAILABLE' => aucunChauffeur,
      'PAYMENT_FAILED' => paiementEchoue,
      _ => inconnu,
    };
  }

  String get libelle => switch (this) {
        demandee => 'En attente de paiement',
        rechercheDuChauffeur => "Recherche d'un chauffeur",
        chauffeurAffecte => 'Chauffeur en route',
        chauffeurEnApproche => 'Chauffeur en approche',
        chauffeurArrive => 'Chauffeur sur place',
        enCours => 'En route vers la destination',
        terminee => 'Terminée',
        annulee => 'Annulée',
        aucunChauffeur => 'Aucun chauffeur disponible',
        paiementEchoue => 'Paiement échoué',
        inconnu => '--',
      };

  /// Ce que le passager doit faire, maintenant. VIDE QUAND IL N'A RIEN A FAIRE :
  /// une ligne « patientez » sous chaque etat ne dit rien et se lit comme du
  /// remplissage.
  String get consigne => switch (this) {
        demandee => 'Réglez la course pour lancer la recherche.',
        rechercheDuChauffeur => 'Nous cherchons un chauffeur disponible.',
        chauffeurAffecte => 'Votre chauffeur se dirige vers le point de départ.',
        chauffeurEnApproche => 'Il arrive. Préparez-vous à monter.',
        chauffeurArrive => 'Votre chauffeur vous attend au point de départ.',
        aucunChauffeur =>
          "Aucun chauffeur n'a pu être trouvé. Votre paiement vous sera remboursé.",
        paiementEchoue => "Le paiement n'a pas abouti. Reprenez une course.",
        _ => '',
      };

  bool get estClose => switch (this) {
        terminee || annulee || aucunChauffeur || paiementEchoue => true,
        _ => false,
      };

  /// L'annulation reste ouverte jusqu'a ce que la course DEMARRE.
  ///
  /// LA POLITIQUE DE FRAIS N'EST PAS TRANCHEE (point 3), et cet ecran ne la
  /// decide pas : il propose le geste, le service refuse s'il doit refuser.
  /// Une fois le passager a bord, annuler n'a plus de sens — la course est en
  /// train de se faire.
  bool get peutEtreAnnulee => switch (this) {
        demandee ||
        rechercheDuChauffeur ||
        chauffeurAffecte ||
        chauffeurEnApproche ||
        chauffeurArrive =>
          true,
        _ => false,
      };

  /// Y a-t-il un chauffeur a suivre sur la carte ?
  ///
  /// RIEN A SUIVRE AVANT L'AFFECTATION : le hub n'enverrait aucune position, et
  /// une carte vide se lit comme une panne.
  bool get aUnChauffeurASuivre => switch (this) {
        chauffeurAffecte || chauffeurEnApproche || chauffeurArrive || enCours =>
          true,
        _ => false,
      };
}

/// Le chauffeur, tel que le passager le voit.
///
/// AUCUN IDENTIFIANT. La passerelle filtre le driverId : le passager recoit de
/// quoi reconnaitre et appeler, pas de quoi designer dans une autre requete.
class ChauffeurDeCourse {
  const ChauffeurDeCourse({
    required this.nom,
    required this.telephone,
    required this.vehicule,
    required this.plaque,
    required this.libelleDuVehicule,
  });

  final String nom;
  final String telephone;

  /// Le type brut rendu par le serveur — « Motorcycle », ou son numero.
  final String vehicule;

  final String plaque;

  /// « La voiture blanche », compose par le domaine. VIDE SI LE CHAUFFEUR N'A
  /// RIEN DECLARE : le domaine seul sait quoi faire de ses trois champs
  /// facultatifs, et l'ecran se rabat alors sur le type et la plaque.
  final String libelleDuVehicule;

  static ChauffeurDeCourse? lire(Object? brut) {
    if (brut is! Map) return null;

    final nom = _texte(brut['displayName']);
    if (nom.isEmpty) return null;

    return ChauffeurDeCourse(
      nom: nom,
      telephone: _texte(brut['phone']),
      vehicule: _texte(brut['vehicleType']),
      plaque: _texte(brut['vehiclePlate']),
      libelleDuVehicule: _texte(brut['vehicleLabel']),
    );
  }

  /// Ce qu'on ecrit sous le nom. Le libelle du domaine d'abord, et a defaut le
  /// type suivi de la plaque — jamais une plaque seule, qui ne se lit pas a
  /// trente metres.
  String get description {
    if (libelleDuVehicule.isNotEmpty) {
      return plaque.isEmpty ? libelleDuVehicule : '$libelleDuVehicule — $plaque';
    }

    final type = VehiculeDeCourse.libelleDe(vehicule);
    return plaque.isEmpty ? type : '$type — $plaque';
  }
}

/// Une course de personnes.
class Course {
  const Course({
    required this.id,
    required this.etat,
    required this.vehicule,
    required this.departLatitude,
    required this.departLongitude,
    required this.destinationLatitude,
    required this.destinationLongitude,
    required this.prixEstimeXof,
    required this.prixFinalXof,
    required this.distanceEstimeeMetres,
    required this.dureeEstimeeSecondes,
    required this.chauffeur,
    required this.demandeeLe,
    required this.chauffeurAffecteLe,
    required this.chauffeurArriveLe,
    required this.demarreeLe,
    required this.termineeLe,
    required this.annuleeLe,
    required this.motifDAnnulation,
    required this.paiementIntentId,
    this.retenueAnnulation = 0,
  });

  final String id;
  final EtatDeCourse etat;

  /// Le vehicule demande, tel que le serveur le rend.
  final String vehicule;

  final double departLatitude;
  final double departLongitude;
  final double destinationLatitude;
  final double destinationLongitude;

  /// LE PRIX ESTIME EST LE PRIX DU (point 35). Les deux champs existent quand
  /// meme : l'ecran montre l'estimation avant, le montant final apres, et le
  /// jour ou les deux divergeront il n'aura pas a changer.
  final int prixEstimeXof;
  final int? prixFinalXof;

  final int distanceEstimeeMetres;
  final int dureeEstimeeSecondes;

  /// Null avant l'affectation et apres la cloture : la passerelle rend alors un
  /// objet absent, et l'interface ne doit pas supposer sa presence.
  final ChauffeurDeCourse? chauffeur;

  final DateTime? demandeeLe;
  final DateTime? chauffeurAffecteLe;
  final DateTime? chauffeurArriveLe;
  final DateTime? demarreeLe;
  final DateTime? termineeLe;
  final DateTime? annuleeLe;
  final String? motifDAnnulation;
  final String? paiementIntentId;

  /// Ce qui serait retenu si le passager annulait MAINTENANT, en francs CFA.
  ///
  /// LE SERVICE L'A CALCULE, L'APPLICATION NE LE REJOUE PAS. La regle — rien tant
  /// qu'aucun chauffeur n'est affecte, le montant fige par le devis ensuite — vit
  /// dans l'agregat Ride, et c'est le meme appel qui remplit l'evenement
  /// d'annulation. Le chiffre affiche est donc exactement celui qui sera preleve.
  ///
  /// ZERO VEUT DIRE « GRATUIT », PAS « INCONNU ».
  final int retenueAnnulation;

  /// Le prix a afficher : le final s'il existe, l'estimation sinon.
  int get prixXof => prixFinalXof ?? prixEstimeXof;

  static Course lire(Map<String, dynamic> json) => Course(
        id: _texte(json['id']),
        etat: EtatDeCourse.lire(json['status']),
        vehicule: _texte(json['vehicleType']),
        departLatitude: _coordonnee(json['pickup'], 'latitude'),
        departLongitude: _coordonnee(json['pickup'], 'longitude'),
        destinationLatitude: _coordonnee(json['destination'], 'latitude'),
        destinationLongitude: _coordonnee(json['destination'], 'longitude'),
        prixEstimeXof: _entier(json['estimatedPriceXof']),
        prixFinalXof: _entierFacultatif(json['finalPriceXof']),
        distanceEstimeeMetres: _entier(json['estimatedDistanceMeters']),
        dureeEstimeeSecondes: _entier(json['estimatedDurationSeconds']),
        chauffeur: ChauffeurDeCourse.lire(json['driver']),
        demandeeLe: _date(json['requestedAt']),
        chauffeurAffecteLe: _date(json['driverAssignedAt']),
        chauffeurArriveLe: _date(json['driverArrivedAt']),
        demarreeLe: _date(json['startedAt']),
        termineeLe: _date(json['completedAt']),
        annuleeLe: _date(json['cancelledAt']),
        motifDAnnulation: switch (json['cancellationReason']) {
          final String v when v.isNotEmpty => v,
          _ => null,
        },
        paiementIntentId: switch (json['paymentIntentId']) {
          final String v when v.isNotEmpty => v,
          _ => null,
        },
        retenueAnnulation: _entier(json['retenueAnnulation']),
      );
}

/// Ce que rend la creation d'une course : la course, et ou la payer.
///
/// L'IDENTIFIANT PEUT ETRE VIDE, et l'application doit le dire plutot que de
/// laisser le passager attendre : le prix est encaisse AVANT la recherche d'un
/// chauffeur (point 35), donc sans paiement la course ne part jamais. Vide, cela
/// veut dire qu'aucune demande n'est partie sur son telephone.
///
/// L'ADRESSE EST REVENUE LE 3 OCTOBRE 2026, le jour meme ou elle avait disparu :
/// la demande poussee directement sur le telephone du passager est refusee par
/// le fournisseur sur notre compte, et la page hebergee a repris sa place.
class CourseCreee {
  const CourseCreee({
    required this.course,
    required this.intentionDePaiement,
    required this.adresseDePaiement,
  });

  final Course course;

  /// Nul quand l'ouverture du paiement a echoue.
  final String? intentionDePaiement;

  /// La page a ouvrir. Nulle quand l'ouverture du paiement a echoue.
  final String? adresseDePaiement;

  bool get paiementOuvrable => (adresseDePaiement ?? '').isNotEmpty;

  static CourseCreee lire(Map<String, dynamic> json) {
    final paiement = json['payment'];

    return CourseCreee(
      course: Course.lire(
        (json['ride'] as Map?)?.cast<String, dynamic>() ?? const {},
      ),
      intentionDePaiement: switch (paiement is Map ? paiement['intentId'] : null) {
        final String v when v.isNotEmpty => v,
        _ => null,
      },
      adresseDePaiement: switch (paiement is Map ? paiement['redirectUrl'] : null) {
        final String v when v.isNotEmpty => v,
        _ => null,
      },
    );
  }
}
