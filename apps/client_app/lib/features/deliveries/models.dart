/// Modeles lus depuis le Client BFF.
///
/// ATTENTION AU JSON : les routes qui relaient un message protobuf renvoient
/// les int64 EN CHAINE et les enums sous leur nom complet, tandis que la route
/// /quotes, redigee a la main dans le BFF, renvoie des nombres. Les lecteurs
/// ci-dessous acceptent les deux.
library;

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
        paymentFailed => 'Paiement echoue',
        paid => 'Payee',
        searchingDriver => "Recherche d'un livreur",
        noDriverFound => 'Aucun livreur trouve',
        driverAssigned => 'Livreur en route',
        driverAtPickup => 'Livreur sur place',
        pickedUp => 'Colis en route',
        delivered => 'Livree',
        cancelled => 'Annulee',
        failed => 'Echouee',
        unknown => '--',
      };

  bool get isClosed => switch (this) {
        delivered || cancelled || failed || noDriverFound || paymentFailed => true,
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
    required this.totalXof,
    required this.distanceMeters,
    required this.durationSeconds,
    required this.expiresAt,
  });

  final String id;
  final int totalXof;
  final int distanceMeters;
  final int durationSeconds;
  final DateTime? expiresAt;

  bool hasExpiredAt(DateTime now) =>
      expiresAt != null && !now.isBefore(expiresAt!);

  static Quote fromJson(Map<String, dynamic> json) => Quote(
        id: _text(json['quoteId']),
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
    required this.vehiclePlate,
  });

  final String displayName;
  final String phone;
  final String vehiclePlate;

  static AssignedDriver? fromJson(Object? json) {
    if (json is! Map) return null;
    final name = _text(json['displayName']);
    if (name.isEmpty) return null;

    return AssignedDriver(
      displayName: name,
      phone: _text(json['phone']),
      vehiclePlate: _text(json['vehiclePlate']),
    );
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
    required this.totalXof,
    required this.driver,
    required this.deliveryOtp,
    required this.createdAt,
  });

  final String id;
  final String reference;
  final DeliveryStatus status;
  final String recipientName;
  final String recipientPhone;
  final String pickupLandmark;
  final String dropoffLandmark;
  final int totalXof;
  final AssignedDriver? driver;

  /// CODE DE REMISE. Le service ne le renseigne que pour le donneur d'ordre,
  /// et il disparait a la cloture : c'est au client de le transmettre au
  /// destinataire.
  final String deliveryOtp;

  final DateTime? createdAt;

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
      totalXof: pricing is Map ? _int64((pricing['total'] as Map?)?['amount']) : 0,
      driver: AssignedDriver.fromJson(json['driver']),
      deliveryOtp: _text(json['deliveryOtp']),
      createdAt: _time(json['createdAt']),
    );
  }
}
