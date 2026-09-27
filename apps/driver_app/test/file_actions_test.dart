import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:hba_driver/core/coffre.dart';
import 'package:hba_driver/core/file_actions.dart';

/// Un coffre en memoire.
///
/// IL PEUT AUSSI TOMBER, et c'est la moitie de son interet : le coffre d'un
/// telephone leve quand le keystore a ete invalide, et la file doit survivre a
/// cela sans emporter le demarrage.
class CoffreFactice implements Coffre {
  CoffreFactice({this.enPanne = false});

  final Map<String, String> valeurs = {};
  bool enPanne;

  @override
  Future<String?> read({required String key}) async {
    if (enPanne) throw StateError('coffre indisponible');
    return valeurs[key];
  }

  @override
  Future<void> write({required String key, required String value}) async {
    if (enPanne) throw StateError('coffre indisponible');
    valeurs[key] = value;
  }

  @override
  Future<void> delete({required String key}) async {
    if (enPanne) throw StateError('coffre indisponible');
    valeurs.remove(key);
  }
}

/// LA CLE EST ECRITE ICI EN TOUTES LETTRES, ET C'EST VOULU. La renommer
/// viderait la file de tous les livreurs deja equipes, silencieusement, au
/// moment de la mise a jour. Ce test echouera d'abord.
const cleFile = 'hba.driver.file_actions';
const cleProprietaire = 'hba.driver.proprietaire';

ActionEnFile action(
  String chemin, {
  String cle = 'k',
  String proprietaire = '',
  Map<String, Object?> corps = const {},
}) =>
    ActionEnFile(
      chemin: chemin,
      corps: corps,
      cleIdempotence: cle,
      creeeLe: DateTime.utc(2026, 9, 27, 8),
      proprietaire: proprietaire,
    );

List<Map<String, Object?>> lireLeCoffre(CoffreFactice coffre) =>
    (jsonDecode(coffre.valeurs[cleFile]!) as List).cast<Map<String, Object?>>();

void main() {
  group('trier : a qui appartient ce qui attend', () {
    test('garde les miennes et celles qui ne sont estampillees par personne', () {
      final tri = FileDActions.trier(
        [
          action('/a', cle: '1', proprietaire: 'moi'),
          action('/b', cle: '2'),
          action('/c', cle: '3', proprietaire: 'un-autre'),
        ],
        'moi',
      );

      expect(tri.miennes.map((e) => e.chemin), ['/a', '/b']);
      expect(tri.autres.map((e) => e.chemin), ['/c']);
    });

    test("un proprietaire inconnu ne jette rien", () {
      // SI JE NE SAIS PAS QUI JE SUIS, je ne suis pas en position de declarer
      // qu'une action appartient a quelqu'un d'autre.
      final tri = FileDActions.trier(
        [action('/a', cle: '1', proprietaire: 'un-autre')],
        '',
      );

      expect(tri.miennes, hasLength(1));
      expect(tri.autres, isEmpty);
    });

    test("l'ordre des miennes est preserve", () {
      // LES TROIS ETAPES D'UNE COURSE FORMENT UNE MACHINE A ETATS : envoyer
      // « remise » avant « collecte » se ferait refuser.
      final tri = FileDActions.trier(
        [
          action('/1/arrived', cle: 'a', proprietaire: 'moi'),
          action('/9/deliver', cle: 'x', proprietaire: 'autre'),
          action('/1/picked-up', cle: 'b', proprietaire: 'moi'),
          action('/1/deliver', cle: 'c', proprietaire: 'moi'),
        ],
        'moi',
      );

      expect(
        tri.miennes.map((e) => e.chemin),
        ['/1/arrived', '/1/picked-up', '/1/deliver'],
      );
    });
  });

  group('estampille', () {
    test('une action prend le proprietaire courant a la mise en file', () async {
      final coffre = CoffreFactice();
      final file = FileDActions(coffre);

      await file.noterLeProprietaire('livreur-1');
      await file.pousser(action('/missions/1/deliver', cle: 'k1'));

      expect(lireLeCoffre(coffre).single['proprietaire'], 'livreur-1');
    });

    test('elle ne change pas quand un autre livreur se connecte', () async {
      // C'EST LE DEFAUT QU'ON EMPECHE : une remise mise en file par A ne doit
      // jamais partir sous le nom de B. L'estampille est posee au geste, et
      // relue telle quelle.
      final coffre = CoffreFactice();

      final chezA = FileDActions(coffre);
      await chezA.noterLeProprietaire('A');
      await chezA.pousser(action('/missions/1/deliver', cle: 'k1'));

      final chezB = FileDActions(coffre);
      await chezB.noterLeProprietaire('B');

      final entrees = lireLeCoffre(coffre)
          .map((e) => ActionEnFile.fromJson(e.cast<String, dynamic>()))
          .toList();

      expect(FileDActions.trier(entrees, 'B').autres, hasLength(1));
    });
  });

  group('mise en file', () {
    test('la meme cle ne s\'empile pas deux fois', () async {
      // Un livreur sans reseau appuie plusieurs fois sur le meme bouton.
      final coffre = CoffreFactice();
      final file = FileDActions(coffre);

      await file.pousser(action('/x', cle: 'meme', corps: {'essai': 1}));
      await file.pousser(action('/x', cle: 'meme', corps: {'essai': 2}));

      expect(await file.nombre, 1);
      expect(lireLeCoffre(coffre).single['corps'], {'essai': 2});
    });

    test('la file est plafonnee et perd les plus anciennes', () async {
      final file = FileDActions(CoffreFactice());

      for (var i = 0; i < 55; i++) {
        await file.pousser(action('/x/$i', cle: 'cle-$i'));
      }

      expect(await file.nombre, 50);
    });

    test('ce qui est ecrit se relit a l\'identique', () async {
      final coffre = CoffreFactice();

      await FileDActions(coffre).pousser(
        action('/missions/7/deliver', cle: 'k7', corps: {'otp': '123456'}),
      );

      // Un autre objet, le meme coffre : c'est le cas du redemarrage.
      expect(await FileDActions(coffre).nombre, 1);
      expect(lireLeCoffre(coffre).single['corps'], {'otp': '123456'});
    });
  });

  group('purge', () {
    test('la deconnexion efface la file ET le proprietaire', () async {
      final coffre = CoffreFactice();
      final file = FileDActions(coffre);

      await file.noterLeProprietaire('livreur-1');
      await file.pousser(action('/missions/1/deliver', cle: 'k1'));

      await file.purger();

      expect(await file.nombre, 0);
      expect(coffre.valeurs.containsKey(cleFile), isFalse);
      expect(coffre.valeurs.containsKey(cleProprietaire), isFalse);
      expect(await file.proprietaireCourant(), '');
    });

    test('le code de remise ne survit pas a la deconnexion', () async {
      // LA RAISON D'ETRE DE LA PURGE : le code dicte par le destinataire est
      // la preuve qu'un colis a ete remis. Il n'a rien a faire sur le
      // telephone apres le depart de son proprietaire.
      final coffre = CoffreFactice();
      final file = FileDActions(coffre);

      await file.pousser(action('/missions/1/deliver', cle: 'k1', corps: {'otp': '482913'}));
      await file.purger();

      expect(coffre.valeurs.values.join(), isNot(contains('482913')));
    });
  });

  group('coffre en panne', () {
    test('une lecture qui leve rend une file vide, pas une exception', () async {
      final file = FileDActions(CoffreFactice(enPanne: true));

      expect(await file.nombre, 0);
      expect(await file.proprietaireCourant(), '');
    });

    test('une purge qui leve ne fait pas tomber la deconnexion', () async {
      final file = FileDActions(CoffreFactice(enPanne: true));

      await expectLater(file.purger(), completes);
    });
  });
}
