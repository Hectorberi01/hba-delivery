import 'package:flutter_test/flutter_test.dart';
import 'package:hba_core/hba_core.dart';

/// Ce qui distingue un refus d'une panne.
///
/// POURQUOI CES TESTS EXISTENT. La file d'actions hors ligne du livreur
/// abandonnait toute ApiException comme un refus definitif. Or le client HTTP y
/// range aussi les 500, 502, 503, 504 et 429 : au retour du reseau, une
/// passerelle en cours de redeploiement repondait 502, et la remise DEJA FAITE
/// disparaissait de la file. Le client avait son colis, la course restait
/// ouverte, et le livreur lisait « une erreur inattendue ».
///
/// LA FRONTIERE EST DONC « QUI A DECIDE », pas « y a-t-il eu une erreur ». Ces
/// tests la tiennent, et ils tiennent surtout le cote prudent : dans le doute,
/// on ne jette pas une preuve de livraison.
void main() {
  ApiException erreur(int? statut) => ApiException(
        code: 'PEU_IMPORTE',
        message: 'peu importe',
        statusCode: statut,
      );

  group('Le serveur a juge : on abandonne l\'action', () {
    test('les refus metier ordinaires', () {
      // 409 : la course avait deja avance, l'action etait donc deja faite.
      // 400, 403, 404, 422 : le serveur a regarde et a dit non.
      for (final statut in [400, 403, 404, 409, 422]) {
        expect(
          erreur(statut).estUnRefusDuServeur,
          isTrue,
          reason: 'HTTP $statut est un jugement',
        );
      }
    });
  });

  group('Le serveur n\'a pas juge : on garde l\'action', () {
    test('les pannes de service', () {
      // LE CAS QUI PERDAIT LA REMISE.
      for (final statut in [500, 502, 503, 504]) {
        expect(
          erreur(statut).estUnRefusDuServeur,
          isFalse,
          reason: 'HTTP $statut ne dit rien de l\'action',
        );
      }
    });

    test('429 et 408 disent « pas maintenant », jamais « non »', () {
      expect(erreur(429).estUnRefusDuServeur, isFalse);
      expect(erreur(408).estUnRefusDuServeur, isFalse);
    });

    test('401 ne porte aucun jugement sur l\'action', () {
      // Il ne devrait pas arriver jusqu'a la file : ApiClient rafraichit, et
      // une session reellement perdue la purge. S'il arrive quand meme, ce
      // n'est pas une raison de detruire une remise.
      expect(erreur(401).estUnRefusDuServeur, isFalse);
    });

    test('un statut absent laisse l\'action en file', () {
      // On ne jette pas une preuve de livraison sur une incertitude.
      expect(erreur(null).estUnRefusDuServeur, isFalse);
    });

    test('un statut inattendu ne condamne rien', () {
      for (final statut in [0, 100, 200, 204, 301, 599]) {
        expect(
          erreur(statut).estUnRefusDuServeur,
          isFalse,
          reason: 'HTTP $statut',
        );
      }
    });
  });

  test('les deux familles ne se recouvrent pas', () {
    // Balayage exhaustif : tout statut est soit un jugement, soit une panne,
    // et la frontiere est exactement celle qu'on croit.
    for (var statut = 100; statut < 600; statut++) {
      final juge = erreur(statut).estUnRefusDuServeur;
      final attendu = statut >= 400 &&
          statut < 500 &&
          statut != 401 &&
          statut != 408 &&
          statut != 429;

      expect(juge, attendu, reason: 'HTTP $statut');
    }
  });
}
