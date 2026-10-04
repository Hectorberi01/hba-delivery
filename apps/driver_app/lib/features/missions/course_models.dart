/// La course de personnes, vue du chauffeur.
///
/// UN FICHIER A PART, ET NON UNE ADDITION A models.dart. Les deux metiers ne
/// partagent AUCUN champ utile : une livraison porte un destinataire, un code de
/// remise, un colis ; une course porte deux points et un passager a bord. Les
/// melanger dans un modele commun aurait produit une classe dont la moitie des
/// champs est nulle selon le cas — exactement ce qu'on relit trois fois sans
/// jamais savoir lequel vaut quoi.
library;

int _entier(Object? valeur) => switch (valeur) {
      final int v => v,
      final num v => v.toInt(),
      final String v => int.tryParse(v) ?? 0,
      _ => 0,
    };

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

/// L'etat d'une course, et ce que le chauffeur peut en faire.
enum EtatDeCourse {
  inconnu,
  demandee,
  rechercheDuChauffeur,
  affectee,
  enApproche,
  surPlace,
  enCours,
  terminee,
  annulee,
  aucunChauffeur,
  paiementEchoue;

  /// LES NOMS SONT COURTS : la passerelle met cette route en forme a la main et
  /// rend « DRIVER_ASSIGNED », non « RIDE_STATUS_DRIVER_ASSIGNED ». La forme
  /// longue est acceptee quand meme, pour qu'un relais brut ne casse pas l'ecran.
  static EtatDeCourse lire(Object? brut) {
    final nom = _texte(brut).toUpperCase();
    final court = nom.startsWith('RIDE_STATUS_') ? nom.substring(12) : nom;

    return switch (court) {
      'REQUESTED' => demandee,
      'SEARCHING_DRIVER' => rechercheDuChauffeur,
      'DRIVER_ASSIGNED' => affectee,
      'DRIVER_ARRIVING' => enApproche,
      'DRIVER_ARRIVED' => surPlace,
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
        rechercheDuChauffeur => 'Recherche en cours',
        affectee => 'À vous',
        enApproche => 'En route vers le passager',
        surPlace => 'Sur place',
        enCours => 'Passager à bord',
        terminee => 'Terminée',
        annulee => 'Annulée',
        aucunChauffeur => 'Sans chauffeur',
        paiementEchoue => 'Paiement échoué',
        inconnu => '--',
      };

  /// La course est-elle encore a conduire ?
  bool get estOuverte => switch (this) {
        affectee || enApproche || surPlace || enCours => true,
        _ => false,
      };

  /// L'etape suivante, du point de vue du chauffeur. NULL QUAND IL N'Y A PLUS
  /// RIEN A FAIRE : l'ecran n'affiche alors aucun bouton d'avancement, plutot
  /// qu'un bouton qui serait refuse.
  EtapeDeCourse? get etapeSuivante => switch (this) {
        affectee => EtapeDeCourse.enRoute,
        enApproche => EtapeDeCourse.arrive,
        surPlace => EtapeDeCourse.demarrer,
        enCours => EtapeDeCourse.terminer,
        _ => null,
      };

  /// L'annulation reste possible tant que le passager n'est pas monte. Une fois
  /// a bord, la course se fait — l'interrompre releve de l'incident, pas de
  /// l'annulation, et aucune liste d'incidents n'est tranchee pour les courses.
  bool get peutEtreAnnulee => switch (this) {
        affectee || enApproche || surPlace => true,
        _ => false,
      };
}

/// Ce que le chauffeur declare, et dans quel ordre.
enum EtapeDeCourse {
  enRoute('on-the-way', 'Je suis en route', 'Prévenir le passager que vous partez.'),
  arrive('arrived', 'Je suis arrivé', 'À faire une fois sur le point de départ.'),
  demarrer('start', 'Démarrer la course', 'Le passager est monté.'),
  terminer('complete', 'Terminer la course', 'Le passager est arrivé à destination.');

  const EtapeDeCourse(this.verbe, this.libelle, this.explication);

  /// Le dernier segment de la route REST.
  final String verbe;

  final String libelle;
  final String explication;
}

/// Une course affectee a ce chauffeur.
///
/// AUCUN IDENTIFIANT DE CLIENT. La passerelle le filtre : le chauffeur a besoin
/// des deux points et de l'etat, pas de quoi designer un compte dans une autre
/// requete.
class CourseLivreur {
  const CourseLivreur({
    required this.id,
    required this.etat,
    required this.vehicule,
    required this.departLatitude,
    required this.departLongitude,
    required this.destinationLatitude,
    required this.destinationLongitude,
    required this.prixPassagerXof,
    required this.distanceEstimeeMetres,
    required this.dureeEstimeeSecondes,
    required this.affecteeLe,
    required this.demarreeLe,
    required this.termineeLe,
    required this.motifDAnnulation,
  });

  final String id;
  final EtatDeCourse etat;

  /// Le vehicule demande, tel que le serveur le rend.
  final String vehicule;

  final double departLatitude;
  final double departLongitude;
  final double destinationLatitude;
  final double destinationLongitude;

  /// CE N'EST PAS LE GAIN DU CHAUFFEUR, et il faut le savoir avant d'en faire un
  /// ecran. C'est le prix paye par le passager ; la part du chauffeur est
  /// calculee par Pricing puis par Billing, et Ride ne la porte pas. L'afficher
  /// comme « votre gain » serait un mensonge, donc l'ecran ne l'affiche pas du
  /// tout : les gains se lisent dans l'onglet Gains, qui dit la verite.
  final int prixPassagerXof;

  final int distanceEstimeeMetres;
  final int dureeEstimeeSecondes;

  final DateTime? affecteeLe;
  final DateTime? demarreeLe;
  final DateTime? termineeLe;
  final String? motifDAnnulation;

  bool get aUnDepart => departLatitude != 0 || departLongitude != 0;
  bool get aUneDestination =>
      destinationLatitude != 0 || destinationLongitude != 0;

  static CourseLivreur lire(Map<String, dynamic> json) => CourseLivreur(
        id: _texte(json['id']),
        etat: EtatDeCourse.lire(json['status']),
        vehicule: _texte(json['vehicleType']),
        departLatitude: _coordonnee(json['pickup'], 'latitude'),
        departLongitude: _coordonnee(json['pickup'], 'longitude'),
        destinationLatitude: _coordonnee(json['destination'], 'latitude'),
        destinationLongitude: _coordonnee(json['destination'], 'longitude'),
        prixPassagerXof: _entier(json['estimatedPriceXof']),
        distanceEstimeeMetres: _entier(json['estimatedDistanceMeters']),
        dureeEstimeeSecondes: _entier(json['estimatedDurationSeconds']),
        affecteeLe: _date(json['driverAssignedAt']),
        demarreeLe: _date(json['startedAt']),
        termineeLe: _date(json['completedAt']),
        motifDAnnulation: switch (json['cancellationReason']) {
          final String v when v.isNotEmpty => v,
          _ => null,
        },
      );
}
