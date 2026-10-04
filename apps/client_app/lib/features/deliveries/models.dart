/// Modeles lus depuis le Client BFF.
///
/// ATTENTION AU JSON : les routes qui relaient un message protobuf renvoient
/// les int64 EN CHAINE et les enums sous leur nom complet, tandis que la route
/// /quotes, redigee a la main dans le BFF, renvoie des nombres. Les lecteurs
/// ci-dessous acceptent les deux.
library;

/// Le temps laisse au client pour payer avant que la commande ne soit
/// abandonnee.
///
/// CETTE VALEUR EST UNE COPIE, ET IL FAUT LE SAVOIR. La verite est cote
/// serveur — « UnpaidDelivery:GraceMinutes » dans la configuration de Delivery —
/// et rien ici ne la lit : le BFF ne la publie pas. Elle ne sert qu'a ECRIRE
/// une phrase juste au client. Si le reglage serveur change, cette ligne doit
/// changer avec lui ; c'est le seul endroit du telephone ou le nombre figure.
const delaiDePaiementMinutes = 15;

int _int64(Object? value) => switch (value) {
      int v => v,
      num v => v.toInt(),
      String v => int.tryParse(v) ?? 0,
      _ => 0,
    };

DateTime? _time(Object? value) =>
    value is String && value.isNotEmpty ? DateTime.tryParse(value)?.toLocal() : null;

String _text(Object? value) => value is String ? value : '';

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

  static DeliveryStatus parse(Object? raw) => switch (raw) {
        'DELIVERY_STATUS_PENDING_PAYMENT' => pendingPayment,
        'DELIVERY_STATUS_PAYMENT_FAILED' => paymentFailed,
        'DELIVERY_STATUS_PAID' => paid,
        'DELIVERY_STATUS_SEARCHING_DRIVER' => searchingDriver,
        'DELIVERY_STATUS_NO_DRIVER_FOUND' => noDriverFound,
        'DELIVERY_STATUS_DRIVER_ASSIGNED' => driverAssigned,
        'DELIVERY_STATUS_DRIVER_AT_PICKUP' => driverAtPickup,
        'DELIVERY_STATUS_PICKED_UP' => pickedUp,
        'DELIVERY_STATUS_DELIVERED' => delivered,
        'DELIVERY_STATUS_CANCELLED' => cancelled,
        'DELIVERY_STATUS_FAILED' => failed,
        _ => unknown,
      };

  String get label => switch (this) {
        pendingPayment => 'En attente de paiement',
        paymentFailed => 'Paiement échoué',
        paid => 'Payée',
        searchingDriver => "Recherche d'un livreur",
        noDriverFound => 'Aucun livreur trouvé',
        driverAssigned => 'Livreur en route',
        driverAtPickup => 'Livreur sur place',
        pickedUp => 'Colis en route',
        delivered => 'Livrée',
        cancelled => 'Annulée',
        failed => 'Échouée',
        unknown => '--',
      };

  bool get isClosed => switch (this) {
        delivered || cancelled || failed || noDriverFound || paymentFailed => true,
        _ => false,
      };

  /// Ce que l'accueil prend en charge lui-meme, plein cadre.
  ///
  /// AVANT LE LIVREUR, LE CLIENT N'A RIEN A LIRE ET RIEN A DECIDER : il attend.
  /// C'est le seul moment ou l'accueil cesse de montrer la carte des livreurs
  /// alentour — commander une seconde course pendant qu'on attend la premiere
  /// n'est pas ce que le client vient faire. Des qu'un livreur est attribue, le
  /// suivi reprend la main : il y a alors un nom, un vehicule, un code.
  bool get suiviParAccueil => switch (this) {
        pendingPayment ||
        paymentFailed ||
        paid ||
        searchingDriver ||
        noDriverFound =>
          true,
        _ => false,
      };

  /// L'annulation est autorisee jusqu'a DRIVER_ASSIGNED inclus. La politique
  /// de frais, elle, n'est pas tranchee : c'est le service qui decidera.
  bool get canCancel => switch (this) {
        pendingPayment || paid || searchingDriver || driverAssigned => true,
        _ => false,
      };
}

class Quote {
  const Quote({
    required this.id,
    required this.nature,
    required this.totalXof,
    required this.distanceMeters,
    required this.durationSeconds,
    required this.expiresAt,
  });

  final String id;

  /// La nature que PRICING a retenue, et non celle qu'on croit avoir demandee.
  ///
  /// C'EST ELLE QUI SERA VERIFIEE A LA CONSOMMATION du devis. Une application
  /// qui aurait omis le champ voit ici « Delivery » sur un devis qu'elle
  /// croyait de course, au lieu de l'apprendre en QUOTE_SERVICE_MISMATCH a la
  /// creation. Vide si le serveur ne la rend pas.
  final String nature;

  bool get estUneLivraison =>
      nature.isEmpty || nature.toLowerCase().startsWith('deliv');

  final int totalXof;
  final int distanceMeters;
  final int durationSeconds;
  final DateTime? expiresAt;

  bool hasExpiredAt(DateTime now) =>
      expiresAt != null && !now.isBefore(expiresAt!);

  static Quote fromJson(Map<String, dynamic> json) => Quote(
        id: _text(json['quoteId']),
        nature: _text(json['nature']),
        totalXof: _int64(json['totalXof']),
        distanceMeters: _int64(json['distanceMeters']),
        durationSeconds: _int64(json['durationSeconds']),
        expiresAt: _time(json['expiresAt']),
      );
}

/// Livreur affecte. Null avant DRIVER_ASSIGNED et apres la cloture : le BFF
/// renvoie des champs vides, l'interface ne doit pas supposer leur presence.
class AssignedDriver {
  const AssignedDriver({
    required this.displayName,
    required this.phone,
    required this.vehicleType,
    required this.vehiclePlate,
  });

  final String displayName;
  final String phone;
  /// Le type de vehicule, tel que le contrat le nomme : « Motorcycle »,
  /// « Bicycle »… Vide quand le service ne le rend pas.
  final String vehicleType;

  final String vehiclePlate;

  static AssignedDriver? fromJson(Object? json) {
    if (json is! Map) return null;
    final name = _text(json['displayName']);
    if (name.isEmpty) return null;

    return AssignedDriver(
      displayName: name,
      phone: _text(json['phone']),
      vehicleType: _text(json['vehicleType']),
      vehiclePlate: _text(json['vehiclePlate']),
    );
  }
}

/// Un point de la course, tel que le service le rend.
class PointLivraison {
  const PointLivraison({required this.latitude, required this.longitude});

  final double latitude;
  final double longitude;

  /// ZERO N'EST PAS UNE COORDONNEE MANQUANTE, C'EST UNE COORDONNEE. La
  /// passerelle rend « 0 » quand le point est absent du message ; le distinguer
  /// ici evite d'afficher une carte au large du Ghana en croyant montrer une
  /// collecte a Cotonou.
  static PointLivraison? depuis(Object? brut) {
    if (brut is! Map) return null;

    final lat = (brut['latitude'] as num?)?.toDouble();
    final lng = (brut['longitude'] as num?)?.toDouble();

    if (lat == null || lng == null) return null;
    if (lat == 0 && lng == 0) return null;

    return PointLivraison(latitude: lat, longitude: lng);
  }
}

class Delivery {
  const Delivery({
    required this.id,
    required this.reference,
    required this.status,
    required this.recipientName,
    required this.recipientPhone,
    required this.pickupLandmark,
    required this.dropoffLandmark,
    required this.pickupPoint,
    required this.dropoffPoint,
    required this.totalXof,
    required this.driver,
    required this.deliveryOtp,
    required this.createdAt,
    this.refundedAt,
    this.refundPartial = false,
    this.retenueAnnulation = 0,
    this.itineraire,
    this.detailDuPrix,
    this.paidAt,
    this.completedAt,
    this.closureReason,
  });

  final String id;
  final String reference;
  final DeliveryStatus status;
  final String recipientName;
  final String recipientPhone;
  final String pickupLandmark;
  final String dropoffLandmark;

  /// LES DEUX POINTS ARRIVAIENT DEJA ET PERSONNE NE LES LISAIT. La passerelle
  /// rend « pickup.latitude » et « pickup.longitude » depuis le debut ; le
  /// modele ne gardait que le repere. L'ecran de suivi ne pouvait donc pas
  /// montrer de carte, faute de savoir ou regarder.
  ///
  /// NULS QUAND LE SERVICE NE LES DONNE PAS, plutot que zero : le point (0, 0)
  /// est dans le golfe de Guinee, a six cents kilometres de Cotonou. Une carte
  /// centree la ressemble a une carte cassee, et c'est exactement ce qu'un
  /// repli silencieux produirait.
  final PointLivraison? pickupPoint;
  final PointLivraison? dropoffPoint;
  final int totalXof;
  final AssignedDriver? driver;

  /// CODE DE REMISE. Le service ne le renseigne que pour le donneur d'ordre,
  /// et il disparait a la cloture : c'est au client de le transmettre au
  /// destinataire.
  final String deliveryOtp;

  final DateTime? createdAt;

  /// Instant ou le remboursement a ete CONSTATE. Nul tant qu'il n'y en a pas.
  ///
  /// FEDAPAY N'A PAS D'API DE REMBOURSEMENT : c'est HBA qui rend l'argent depuis
  /// le tableau de bord du fournisseur, et le service l'apprend par le webhook.
  /// Ce champ est donc la seule chose qui permette a l'ecran de passer de « HBA
  /// revient vers vous » a « le montant vous a ete rendu » — sans lui, on ne
  /// pouvait que promettre.
  final DateTime? refundedAt;

  /// Vrai quand le remboursement constate est partiel. Le fournisseur ne dit pas
  /// combien a ete rendu dans ce cas : l'ecran ne doit donc annoncer aucun
  /// montant.
  final bool refundPartial;

  /// Ce qui serait retenu si le client annulait MAINTENANT, en francs CFA.
  ///
  /// LE SERVICE L'A CALCULE, L'APPLICATION NE LE REJOUE PAS. La regle — rien tant
  /// qu'aucun livreur n'est affecte, le montant fige par le devis ensuite — vit
  /// dans l'agregat Delivery, et c'est le meme appel qui remplit l'evenement
  /// d'annulation. Le chiffre affiche est donc exactement celui qui sera preleve.
  ///
  /// ZERO VEUT DIRE « GRATUIT », PAS « INCONNU ». C'est le cas le plus frequent :
  /// aucun livreur affecte, ou une zone dont la grille ne prevoit pas de retenue.
  final int retenueAnnulation;

  /// L'itineraire qui reste au livreur, ou nul quand on ne sait pas.
  ///
  /// LA CIBLE CHANGE AVEC L'ETAT, et c'est le service qui la choisit : le point
  /// de collecte avant l'enlevement, le destinataire apres. L'application n'a
  /// pas a rejouer cette regle.
  ///
  /// NUL N'EST PAS UN CAS RARE : aucun livreur affecte, aucune position encore
  /// remontee, moteur d'itineraire injoignable, mission close. L'ecran retombe
  /// alors sur la distance a vol d'oiseau, qu'il sait calculer seul.
  final ItineraireActif? itineraire;

  /// La duree a afficher, ou nulle s'il n'y a rien d'honnete a dire.
  ///
  /// C'EST ICI QUE LA REGLE DU REPLI S'APPLIQUE, EN UN SEUL ENDROIT. Trois
  /// endroits montrent cette duree — le bandeau de l'accueil, la feuille de
  /// suivi, l'ecran plein — et une regle recopiee trois fois est une regle qui
  /// finit par diverger. Voir le point 43.
  Duration? get arriveeDans => itineraire?.duree;

  /// Le detail du prix, pour le recu. Nul tant que le service ne l'a pas rendu.
  final DetailDuPrix? detailDuPrix;

  /// Instant ou le paiement a ete CONSTATE par le webhook signe, et non celui
  /// ou le client est revenu de la page du fournisseur : les deux different, et
  /// c'est le premier qui fait foi (ADR 0017).
  final DateTime? paidAt;

  /// Instant de la remise. Nul pour une course annulee ou echouee.
  final DateTime? completedAt;

  /// Motif d'une cloture qui n'est pas une remise. Nul quand tout s'est bien
  /// passe — et c'est ce « nul » qui distingue, sur le recu, une livraison
  /// faite d'une livraison interrompue.
  final String? closureReason;

  bool get estRemboursee => refundedAt != null;

  /// UN RECU N'A DE SENS QUE SUR UNE COURSE PAYEE. Une commande abandonnee
  /// avant paiement n'a rien a justifier : la proposer afficherait un papier
  /// vide.
  bool get aUnRecu => paidAt != null && status.isClosed;

  bool get showsOtp => deliveryOtp.isNotEmpty && !status.isClosed;

  static Delivery fromJson(Map<String, dynamic> json) {
    final pricing = json['pricing'];
    final pickup = json['pickup'];
    final dropoff = json['dropoff'];

    return Delivery(
      id: _text(json['id']),
      reference: _text(json['reference']),
      status: DeliveryStatus.parse(json['status']),
      recipientName: _text(json['recipientName']),
      recipientPhone: _text(json['recipientPhone']),
      pickupLandmark: pickup is Map ? _text(pickup['landmark']) : '',
      dropoffLandmark: dropoff is Map ? _text(dropoff['landmark']) : '',
      pickupPoint: PointLivraison.depuis(pickup),
      dropoffPoint: PointLivraison.depuis(dropoff),
      totalXof: pricing is Map ? _int64((pricing['total'] as Map?)?['amount']) : 0,
      driver: AssignedDriver.fromJson(json['driver']),
      deliveryOtp: _text(json['deliveryOtp']),
      createdAt: _time(json['createdAt']),
      refundedAt: _time(json['refundedAt']),
      refundPartial: json['refundPartial'] == true,
      retenueAnnulation: _int64(json['retenueAnnulation']),

      itineraire: ItineraireActif.depuis(json['itineraire']),
      detailDuPrix: DetailDuPrix.depuis(pricing),
      paidAt: _time(json['paidAt']),
      completedAt: _time(json['completedAt']),
      closureReason: json['closureReason'] is String && (json['closureReason'] as String).isNotEmpty
          ? json['closureReason'] as String
          : null,
    );
  }
}

/// Le detail d'un prix, tel que le devis l'a fige.
///
/// IL NE CONTIENT PAS LA PART DU LIVREUR, et ce n'est pas un oubli : le contrat
/// la reserve au livreur. Ce que HBA garde sur une course ne regarde pas le
/// client, et l'afficher ferait de chaque recu une negociation.
///
/// LA SOMME DES LIGNES PEUT NE PAS FAIRE LE TOTAL, parce que le total est
/// arrondi a la dizaine de francs par le service. Le recu affiche donc le total
/// tel qu'il est, et non une addition refaite ici — refaire le calcul cote
/// application serait le meilleur moyen d'afficher un franc d'ecart.
class DetailDuPrix {
  const DetailDuPrix({
    required this.priseEnChargeXof,
    required this.distanceXof,
    required this.majorationXof,
    required this.metres,
    required this.secondes,
  });

  final int priseEnChargeXof;
  final int distanceXof;
  final int majorationXof;
  final int metres;
  final int secondes;

  bool get aUneMajoration => majorationXof > 0;

  static DetailDuPrix? depuis(Object? brut) {
    if (brut is! Map) return null;

    return DetailDuPrix(
      priseEnChargeXof: _int64(brut['baseFareXof']),
      distanceXof: _int64(brut['distanceFareXof']),
      majorationXof: _int64(brut['surgeFareXof']),
      metres: _int64(brut['distanceMeters']),
      secondes: _int64(brut['durationSeconds']),
    );
  }
}

/// Ce qui reste a parcourir, tel que le service le mesure.
///
/// IL N'EST JAMAIS CONSERVE (point 43). L'ecran REMPLACE celui qu'il affiche a
/// chaque lecture et n'en empile aucun : une suite d'itineraires gardes serait
/// la piste de deplacement que le point 37 refuse.
class ItineraireActif {
  const ItineraireActif({
    required this.distanceMetres,
    required this.dureeSecondes,
    required this.duRepli,
    required this.calculeA,
    required this.trace,
  });

  final int distanceMetres;
  final int dureeSecondes;

  /// Vrai quand la mesure vient du repli a vol d'oiseau du service de routage.
  ///
  /// ON N'AFFICHE ALORS AUCUNE DUREE, et aucun trace n'arrive non plus : une
  /// mesure geometrique ne dessine aucune rue. Elle est sous-estimee d'environ
  /// un tiers en ville, davantage des que le trajet contourne la lagune :
  /// l'annoncer en minutes serait une promesse que la course ne tiendra pas.
  final bool duRepli;

  final DateTime? calculeA;

  /// Le trace encode, vide quand l'ecran ne l'a pas demande.
  final String trace;

  /// La duree, ou nulle quand elle ne vaut pas la peine d'etre dite.
  Duration? get duree =>
      duRepli || dureeSecondes <= 0 ? null : Duration(seconds: dureeSecondes);

  /// L'heure d'arrivee, deduite de l'instant du calcul.
  ///
  /// DEPUIS L'INSTANT DU CALCUL ET NON DEPUIS MAINTENANT : la mesure peut avoir
  /// une demi-minute, et repartir de l'heure courante la rallongerait d'autant a
  /// chaque lecture, sans que rien n'ait bouge sur la route.
  DateTime? get arriveeA {
    final duree = this.duree;
    final calcul = calculeA;
    return duree == null || calcul == null ? null : calcul.add(duree);
  }

  static ItineraireActif? depuis(Object? brut) {
    if (brut is! Map) return null;

    final duree = brut['dureeSecondes'];
    if (duree is! num) return null;

    return ItineraireActif(
      distanceMetres: brut['distanceMetres'] is num
          ? (brut['distanceMetres'] as num).toInt()
          : 0,
      dureeSecondes: duree.toInt(),
      duRepli: brut['duRepli'] == true,
      calculeA: DateTime.tryParse('${brut['calculeA']}'),
      trace: brut['trace'] is String ? brut['trace'] as String : '',
    );
  }
}
