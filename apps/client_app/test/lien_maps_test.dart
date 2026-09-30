import 'package:flutter_test/flutter_test.dart';
import 'package:hba_client/features/deliveries/lien_maps.dart';

/// Ce qu'un lien Google Maps colle doit donner.
///
/// LES LIENS CI-DESSOUS SONT DE VRAIES FORMES, pas des exemples inventes.
/// Google en produit plusieurs selon l'origine du partage — application
/// mobile, navigateur, fiche de lieu, itineraire — et ils ne se ressemblent
/// pas. Un lecteur eprouve sur une seule forme casse a la premiere autre.
void main() {
  // Une tolerance, parce qu'on compare des doubles issus d'un texte.
  void attendre(dynamic point, double lat, double lng) {
    expect(point, isNotNull);
    expect(point.latitude, closeTo(lat, 0.000001));
    expect(point.longitude, closeTo(lng, 0.000001));
  }

  group('formes acceptees', () {
    test('le centre de la camera', () {
      attendre(
        LienMaps.lire('https://www.google.com/maps/@6.3703,2.3912,15z'),
        6.3703,
        2.3912,
      );
    });

    test('une fiche de lieu : le LIEU l\'emporte sur la camera', () {
      // LE PIEGE DE CE FORMAT, ET LA RAISON D'ETRE DE L'ORDRE DES MOTIFS. Les
      // deux paires sont presentes et DIFFERENTES : « @ » est la ou regardait
      // la carte, « !3d!4d » est le lieu partage. Lire « @ » poserait le point
      // a cote de ce que l'expediteur croyait envoyer.
      attendre(
        LienMaps.lire(
          'https://www.google.com/maps/place/Cotonou/@6.4000,2.4000,17z'
          '/data=!3m1!4b1!4m5!3m4!1s0x0:0x0!8m2!3d6.3703!4d2.3912',
        ),
        6.3703,
        2.3912,
      );
    });

    test('le parametre q', () {
      attendre(
        LienMaps.lire('https://maps.google.com/?q=6.3703,2.3912'),
        6.3703,
        2.3912,
      );
    });

    test('le parametre query, virgule encodee', () {
      attendre(
        LienMaps.lire(
          'https://www.google.com/maps/search/?api=1&query=6.3703%2C2.3912',
        ),
        6.3703,
        2.3912,
      );
    });

    test('une destination d\'itineraire', () {
      attendre(
        LienMaps.lire(
          'https://www.google.com/maps/dir/?api=1&destination=6.3703,2.3912',
        ),
        6.3703,
        2.3912,
      );
    });

    test('un lien geo:', () {
      attendre(LienMaps.lire('geo:6.3703,2.3912'), 6.3703, 2.3912);
    });

    test('deux nombres nus', () {
      attendre(LienMaps.lire('6.3703, 2.3912'), 6.3703, 2.3912);
    });

    test('un lien noye dans un message', () {
      // CE QUI ARRIVE VRAIMENT DU PRESSE-PAPIER. On copie rarement une URL
      // seule : on copie la phrase qui la contient.
      attendre(
        LienMaps.lire(
          'Je suis ici https://www.google.com/maps/@6.3703,2.3912,15z a 18h',
        ),
        6.3703,
        2.3912,
      );
    });

    test('des coordonnees negatives', () {
      attendre(
        LienMaps.lire('https://www.google.com/maps/@-33.8688,-151.2093,15z'),
        -33.8688,
        -151.2093,
      );
    });
  });

  group('refus', () {
    test('un texte sans coordonnees', () {
      expect(LienMaps.lire('bonjour, tu es ou ?'), isNull);
    });

    test('un lien court ne contient rien a lire', () {
      // IL N'EST PAS INVALIDE POUR AUTANT : « resoudre » le suivra. Ce test
      // fixe la frontiere entre ce qui se lit hors ligne et ce qui demande le
      // reseau.
      expect(LienMaps.lire('https://maps.app.goo.gl/x4KdQ7vT'), isNull);
    });

    test('une latitude hors du globe', () {
      expect(LienMaps.lire('https://www.google.com/maps/@91.5,2.3912,15z'), isNull);
    });

    test('une longitude hors du globe', () {
      expect(LienMaps.lire('https://www.google.com/maps/@6.3703,181.2,15z'), isNull);
    });

    test('des entiers nus ne sont pas des coordonnees', () {
      // « 15, 20 » est un numero de rue ou une heure bien plus souvent qu'un
      // point : le motif nu exige des decimales.
      expect(LienMaps.lire('rendez-vous 15, 20 minutes'), isNull);
    });
  });

  group('hote', () {
    test('un lien Google est retenu', () {
      expect(LienMaps.url('https://maps.app.goo.gl/x4KdQ7vT'), isNotNull);
    });

    test('tout autre hote est refuse', () {
      // LA LISTE BLANCHE EST LE SUJET DE CE TEST. Ce texte vient du
      // presse-papier : sans elle, l'application irait chercher l'URL que le
      // premier message venu lui souffle.
      expect(LienMaps.url('https://exemple.test/piege'), isNull);
      expect(LienMaps.url('http://127.0.0.1:8080/interne'), isNull);
      expect(LienMaps.url('https://maps.app.goo.gl.attaquant.test/x'), isNull);
    });
  });
}
