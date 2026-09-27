import 'package:flutter_test/flutter_test.dart';
import 'package:hba_driver/features/missions/models.dart';

void main() {
  group('Lecture du JSON protobuf', () {
    test('un int64 arrive en chaine et doit etre lu comme un nombre', () {
      // C'est le piege : protobuf-JSON serialise les int64 en CHAINE. Lu
      // naivement, driverEarningXof vaudrait zero sans que rien ne plante.
      final offer = Offer.fromJson({
        'id': 'offer-1',
        'deliveryId': 'delivery-1',
        'expiresAt': '2026-09-23T10:00:00Z',
        'pickupLandmark': 'Carre 442',
        'distanceToPickupMeters': 800,
        'tripDistanceMeters': 3400,
        'driverEarningXof': '1100',
      });

      expect(offer, isNotNull);
      expect(offer!.driverEarningXof, 1100);
      expect(offer.tripDistanceMeters, 3400);
    });

    test('un enum arrive sous son nom complet', () {
      expect(
        DeliveryStatus.parse('DELIVERY_STATUS_PICKED_UP'),
        DeliveryStatus.pickedUp,
      );
      expect(DeliveryStatus.parse('AUTRE_CHOSE'), DeliveryStatus.unknown);
      expect(DeliveryStatus.parse(null), DeliveryStatus.unknown);
    });

    test('une offre sans identifiant est nulle plutot que vide', () {
      // GetCurrentOffer renvoie un message vide quand il n'y a pas d'offre :
      // les champs valent leur valeur par defaut, pas null.
      expect(Offer.fromJson(const {}), isNull);
    });

    test('la remuneration se lit dans le detail tarifaire de la course', () {
      final mission = Mission.fromJson({
        'id': 'd-1',
        'reference': 'HBA-ABC123',
        'status': 'DELIVERY_STATUS_DRIVER_ASSIGNED',
        'pricing': {
          'driverEarning': {'amount': '1100', 'currency': 'XOF'},
        },
        'pickup': {
          'point': {'latitude': 6.37, 'longitude': 2.39},
          'landmark': 'Carre 442',
          'contactName': 'Expediteur',
          'phone': '+22997000001',
        },
      });

      expect(mission.driverEarningXof, 1100);
      expect(mission.pickup?.landmark, 'Carre 442');
      expect(mission.pickup?.contactName, 'Expediteur');
      // Avant acceptation, la destination n'est pas renseignee.
      expect(mission.dropoff, isNull);
      expect(mission.isOpen, isTrue);
    });
  });

  group('Etapes du livreur', () {
    test('chaque etat ouvert propose une action, les etats clos non', () {
      expect(DeliveryStatus.driverAssigned.nextDriverAction, isNotNull);
      expect(DeliveryStatus.driverAtPickup.nextDriverAction, isNotNull);
      expect(DeliveryStatus.pickedUp.nextDriverAction, isNotNull);
      expect(DeliveryStatus.delivered.nextDriverAction, isNull);
      expect(DeliveryStatus.cancelled.nextDriverAction, isNull);
    });
  });
}
