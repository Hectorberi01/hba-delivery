import 'package:flutter_test/flutter_test.dart';
import 'package:hba_client/features/deliveries/models.dart';

void main() {
  group('Livraison vue du client', () {
    test('le code de remise est visible tant que la course est ouverte', () {
      final delivery = Delivery.fromJson({
        'id': 'd-1',
        'reference': 'HBA-ABC123',
        'status': 'DELIVERY_STATUS_DRIVER_ASSIGNED',
        'deliveryOtp': '482913',
      });

      expect(delivery.showsOtp, isTrue);
    });

    test('il disparait des que la course est close', () {
      final delivery = Delivery.fromJson({
        'id': 'd-1',
        'status': 'DELIVERY_STATUS_DELIVERED',
        'deliveryOtp': '482913',
      });

      expect(delivery.status.isClosed, isTrue);
      expect(delivery.showsOtp, isFalse);
    });

    test('le livreur est nul avant affectation', () {
      // Le BFF renvoie un message vide, pas null : displayName vaut "".
      final delivery = Delivery.fromJson({
        'id': 'd-1',
        'status': 'DELIVERY_STATUS_SEARCHING_DRIVER',
        'driver': {'displayName': '', 'phone': ''},
      });

      expect(delivery.driver, isNull);
    });

    test('annulation permise jusqu a DRIVER_ASSIGNED inclus', () {
      expect(DeliveryStatus.paid.canCancel, isTrue);
      expect(DeliveryStatus.driverAssigned.canCancel, isTrue);
      expect(DeliveryStatus.pickedUp.canCancel, isFalse);
      expect(DeliveryStatus.delivered.canCancel, isFalse);
    });

    test('le prix vient du détail tarifaire, int64 en chaîne', () {
      final delivery = Delivery.fromJson({
        'id': 'd-1',
        'status': 'DELIVERY_STATUS_PAID',
        'pricing': {
          'total': {'amount': '1500', 'currency': 'XOF'},
        },
      });

      expect(delivery.totalXof, 1500);
    });
  });
}
