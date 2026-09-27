/// Modeles lus depuis le Driver BFF.
///
/// LE BFF RENVOIE DES MESSAGES PROTOBUF, MAIS PAS EN JSON PROTOBUF. La
/// passerelle les serialise avec System.Text.Json, ce qui donne une forme
/// hybride : les champs sont en camelCase comme attendu, mais un enum sort en
/// ENTIER et non sous son nom, et un champ absent vaut sa valeur par defaut
/// plutot que null.
///
/// Les lectures ci-dessous acceptent LES DEUX FORMES. Ce n'est pas de la
/// complaisance : le contrat de serialisation n'est pas tranche, et une
/// lecture qui ne sait lire qu'une forme casse silencieusement le jour ou la
/// passerelle change d'avis — un statut inconnu ne leve rien, il efface juste
/// le bouton d'action.
library;

int _int64(Object? value) => switch (value) {
      int v => v,
      num v => v.toInt(),
      String v => int.tryParse(v) ?? 0,
      _ => 0,
    };

/// Un instant, dans l'une ou l'autre forme.
///
/// EN JSON PROTOBUF un Timestamp est une chaine ISO ; serialise par
/// System.Text.Json, c'est un objet { seconds, nanos }. La passerelle rend
/// aujourd'hui la seconde forme. Lire les deux evite une colonne de dates
/// vides le jour ou elle rendra la premiere.
DateTime? _time(Object? value) {
  if (value is String) {
    return value.isEmpty ? null : DateTime.tryParse(value)?.toLocal();
  }

  if (value is Map) {
    final seconds = (value['seconds'] as num?)?.toInt() ??
        int.tryParse('${value['seconds']}');
    if (seconds == null) return null;

    return DateTime.fromMillisecondsSinceEpoch(seconds * 1000, isUtc: true)
        .toLocal();
  }

  return null;
}

String _text(Object? value) => value is String ? value : '';

/// Une coordonnee d'un hba.common.v1.GeoPoint imbrique, ou zero.
double _coordonnee(Object? point, String champ) =>
    point is Map ? (point[champ] as num?)?.toDouble() ?? 0 : 0;

enum DeliveryStatus {
  unknown,
  pendingPayment,
  paymentFailed,
  paid,
  searchingDriver,
  noDriverFound,
  driverAssigned,
  driverAtPickup,
  pickedUp,
  delivered,
  cancelled,
  failed;

  /// Les entiers sont ceux de delivery_service.proto. Ils ne se choisissent
  /// pas : changer l'ordre du contrat casserait aussi les bases.
  static final _parIndice = <int, DeliveryStatus>{
    1: pendingPayment,
    2: paymentFailed,
    3: paid,
    4: searchingDriver,
    5: noDriverFound,
    6: driverAssigned,
    7: driverAtPickup,
    8: pickedUp,
    9: delivered,
    10: cancelled,
    11: failed,
  };

  static final _parNom = <String, DeliveryStatus>{
    'DELIVERY_STATUS_PENDING_PAYMENT': pendingPayment,
    'DELIVERY_STATUS_PAYMENT_FAILED': paymentFailed,
    'DELIVERY_STATUS_PAID': paid,
    'DELIVERY_STATUS_SEARCHING_DRIVER': searchingDriver,
    'DELIVERY_STATUS_NO_DRIVER_FOUND': noDriverFound,
    'DELIVERY_STATUS_DRIVER_ASSIGNED': driverAssigned,
    'DELIVERY_STATUS_DRIVER_AT_PICKUP': driverAtPickup,
    'DELIVERY_STATUS_PICKED_UP': pickedUp,
    'DELIVERY_STATUS_DELIVERED': delivered,
    'DELIVERY_STATUS_CANCELLED': cancelled,
    'DELIVERY_STATUS_FAILED': failed,
  };

  static DeliveryStatus parse(Object? raw) => switch (raw) {
        int v => _parIndice[v] ?? unknown,
        // Une chaine peut etre le nom du contrat, ou un entier rendu en
        // chaine — les int64 du JSON protobuf arrivent ainsi.
        String v => _parNom[v] ?? _parIndice[int.tryParse(v) ?? -1] ?? unknown,
        _ => unknown,
      };

  /// Libelle pour l'historique. Il dit ce qui EST ARRIVE A LA COURSE, du
  /// point de vue du livreur, et non l'etat interne du systeme.
  String get libelle => switch (this) {
        pendingPayment => 'En attente de paiement',
        paymentFailed => 'Paiement échoué',
        paid => 'Payée',
        searchingDriver => 'Recherche d\'un livreur',
        noDriverFound => 'Aucun livreur trouvé',
        driverAssigned => 'A collecter',
        driverAtPickup => 'Au point de collecte',
        pickedUp => 'Colis en main',
        delivered => 'Livrée',
        cancelled => 'Annulée',
        failed => 'Échouée',
        unknown => 'Statut inconnu',
      };

  /// La course est close : plus rien ne s'y passera.
  bool get estClose => switch (this) {
        paymentFailed || noDriverFound || delivered || cancelled || failed => true,
        _ => false,
      };

  /// Le livreur a-t-il ete paye pour cette course ? Seule « livree » compte.
  bool get estReussie => this == delivered;

  /// Etape suivante attendue du livreur. Null quand il n'a rien a faire.
  String? get nextDriverAction => switch (this) {
        driverAssigned => 'Je suis au point de collecte',
        driverAtPickup => "J'ai récupéré le colis",
        pickedUp => 'Confirmer la remise',
        _ => null,
      };
}

/// Adresse utilisable au Benin : un point, un repere, un telephone.
class Place {
  const Place({
    required this.latitude,
    required this.longitude,
    required this.landmark,
    required this.contactName,
    required this.phone,
    required this.notes,
  });

  final double latitude;
  final double longitude;
  final String landmark;
  final String contactName;
  final String phone;
  final String notes;

  static Place? fromJson(Object? json) {
    if (json is! Map) return null;

    final point = json['point'];
    return Place(
      latitude: point is Map ? (point['latitude'] as num?)?.toDouble() ?? 0 : 0,
      longitude: point is Map ? (point['longitude'] as num?)?.toDouble() ?? 0 : 0,
      landmark: _text(json['landmark']),
      contactName: _text(json['contactName']),
      phone: _text(json['phone']),
      notes: _text(json['notes']),
    );
  }
}

/// Offre de course.
///
/// ELLE NE CONTIENT PAS L'ADRESSE DE DESTINATION, et ne contiendra jamais le
/// prix paye par le client. Le contrat s'arrete au repere de collecte, aux
/// distances et a la remuneration du livreur.
class Offer {
  const Offer({
    required this.id,
    required this.deliveryId,
    required this.expiresAt,
    required this.pickupLandmark,
    required this.distanceToPickupMeters,
    required this.tripDistanceMeters,
    required this.driverEarningXof,
    required this.pickupLatitude,
    required this.pickupLongitude,
  });

  final String id;
  final String deliveryId;
  final DateTime expiresAt;
  final String pickupLandmark;
  final int distanceToPickupMeters;
  final int tripDistanceMeters;
  final int driverEarningXof;

  /// Point de collecte. ZERO SIGNALE UNE ABSENCE, pas un point au large du
  /// golfe de Guinee : une passerelle qui n'aurait pas le champ rendrait la
  /// valeur par defaut de protobuf, et une carte centree sur 0,0 montrerait
  /// l'ocean. Les ecrans testent donc avant d'afficher.
  final double pickupLatitude;
  final double pickupLongitude;

  bool get aUnPointDeCollecte => pickupLatitude != 0 || pickupLongitude != 0;

  Duration remainingFrom(DateTime now) {
    final left = expiresAt.difference(now);
    return left.isNegative ? Duration.zero : left;
  }

  static Offer? fromJson(Map<String, dynamic> json) {
    final id = _text(json['id']);
    if (id.isEmpty) return null;

    return Offer(
      id: id,
      deliveryId: _text(json['deliveryId']),
      // Le compte a rebours vient du serveur : la duree d'une vague n'est pas
      // une constante de l'application.
      expiresAt: _time(json['expiresAt']) ?? DateTime.now(),
      pickupLandmark: _text(json['pickupLandmark']),
      distanceToPickupMeters: _int64(json['distanceToPickupMeters']),
      tripDistanceMeters: _int64(json['tripDistanceMeters']),
      driverEarningXof: _int64(json['driverEarningXof']),
      pickupLatitude: _coordonnee(json['pickup'], 'latitude'),
      pickupLongitude: _coordonnee(json['pickup'], 'longitude'),
    );
  }
}

/// Course acceptee, vue du livreur.
class Mission {
  const Mission({
    required this.id,
    required this.reference,
    required this.status,
    required this.pickup,
    required this.dropoff,
    required this.recipientName,
    required this.recipientPhone,
    required this.packageDescription,
    required this.driverEarningXof,
    required this.createdAt,
    required this.completedAt,
    required this.closureReason,
  });

  final String id;
  final String reference;
  final DeliveryStatus status;
  final Place? pickup;

  /// Renseignee seulement apres acceptation.
  final Place? dropoff;

  final String recipientName;
  final String recipientPhone;
  final String packageDescription;

  /// Sa remuneration. Le prix client n'est pas dans la reponse.
  final int driverEarningXof;

  final DateTime? createdAt;
  final DateTime? completedAt;

  /// Motif d'annulation ou d'echec. Vide sur une course livree.
  final String closureReason;

  bool get isOpen => status.nextDriverAction != null;

  /// Date a afficher dans l'historique : la cloture si elle existe, la
  /// creation sinon. Une course en cours n'a pas encore de fin.
  DateTime? get dateAffichee => completedAt ?? createdAt;

  /// La meme course, a une autre etape.
  ///
  /// POUR L'AVANCE OPTIMISTE, ET POUR ELLE SEULE. Quand une etape part en file
  /// faute de reseau, l'ecran doit laisser le livreur continuer : bloquer sur
  /// un bouton jusqu'au retour de la connexion l'empecherait de finir sa
  /// course dans un parking souterrain. Le serveur reste la source de verite —
  /// la prochaine lecture de /missions ecrase cet etat local.
  ///
  /// ELLE NE SERT PAS A LA REMISE. Le serveur y valide un code, donc il peut
  /// contredire ; avancer serait promettre une livraison peut-etre refusee.
  Mission avecStatut(DeliveryStatus nouveau) => Mission(
        id: id,
        reference: reference,
        status: nouveau,
        pickup: pickup,
        dropoff: dropoff,
        recipientName: recipientName,
        recipientPhone: recipientPhone,
        packageDescription: packageDescription,
        driverEarningXof: driverEarningXof,
        createdAt: createdAt,
        completedAt: completedAt,
        closureReason: closureReason,
      );

  static Mission fromJson(Map<String, dynamic> json) {
    final pricing = json['pricing'];

    return Mission(
      id: _text(json['id']),
      reference: _text(json['reference']),
      status: DeliveryStatus.parse(json['status']),
      pickup: Place.fromJson(json['pickup']),
      dropoff: Place.fromJson(json['dropoff']),
      recipientName: _text(json['recipientName']),
      recipientPhone: _text(json['recipientPhone']),
      packageDescription: _text(json['packageDescription']),
      driverEarningXof:
          pricing is Map ? _int64((pricing['driverEarning'] as Map?)?['amount']) : 0,
      createdAt: _time(json['createdAt']),
      completedAt: _time(json['completedAt']),
      closureReason: _text(json['closureReason']),
    );
  }
}
