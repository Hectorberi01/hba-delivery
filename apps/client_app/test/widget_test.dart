// CE FICHIER ETAIT LE MODELE LIVRE PAR « flutter create », ET IL N'A JAMAIS
// COMPILE.
//
// Il eprouvait un compteur qui n'a jamais existe dans cette application :
// « MyApp », un bouton « + », un chiffre qui passe de 0 a 1. Aucun de ces
// elements n'a jamais ete ecrit ici.
//
// CE QUE CELA PROUVAIT. Un test qui ne compile pas fait echouer TOUTE la
// commande « flutter test ». Sa presence depuis la creation du projet dit donc
// une chose simple et verifiable : la suite de tests de l'application cliente
// n'a jamais ete lancee. Pas « a echoue » — jamais lancee. Et c'est
// « flutter analyze » qui l'a signale le 28 septembre 2026, pas un test.
//
// Il est remplace ici par un vrai test du seul widget de cette application qui
// porte une minuterie.

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hba_client/features/deliveries/tracking_screen.dart';

void main() {
  /// Un instant fixe, choisi loin de toute frontiere de seconde.
  final commande = DateTime(2026, 9, 28, 10, 0, 0);

  /// Pose le bloc seul dans un ecran minimal, avec une horloge qu'on pilote.
  ///
  /// PAS DE `TrackingScreen` ENTIER : il reclame un identifiant de livraison,
  /// un fournisseur Riverpod et un aller-retour reseau. On eprouverait alors la
  /// plomberie, pas la regle.
  ///
  /// L'HORLOGE EST UNE FONCTION, PAS UNE VALEUR, et c'est ce qui permet de la
  /// faire avancer entre deux battements sans reconstruire le widget.
  Future<void> poser(
    WidgetTester tester, {
    required DateTime? depuis,
    required DateTime Function() horloge,
  }) =>
      tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: RechercheEnCours(depuis: depuis, horloge: horloge),
          ),
        ),
      );

  /// DEMONTER LE WIDGET FAIT PARTIE DU TEST, CE N'EST PAS DU RANGEMENT.
  /// `testWidgets` echoue si une minuterie reste pendante a la fin. C'est donc
  /// cette ligne, et elle seule, qui verifie que `dispose` annule l'horloge —
  /// un oubli qui, en production, ferait battre une seconde par seconde un
  /// widget que le client a quitte depuis longtemps.
  Future<void> demonter(WidgetTester tester) =>
      tester.pumpWidget(const MaterialApp(home: Scaffold()));

  /// Pose le bloc a un ecart fige de la commande.
  Future<void> poserA(WidgetTester tester, Duration ecart) => poser(
        tester,
        depuis: commande,
        horloge: () => commande.add(ecart),
      );

  group("Bloc « Recherche d'un livreur »", () {
    testWidgets('sous la minute, le compteur est en secondes', (tester) async {
      await poserA(tester, const Duration(seconds: 45));

      expect(find.text('Commandée il y a 45 s'), findsOneWidget);
      await demonter(tester);
    });

    testWidgets('entre une et dix minutes, les secondes restent visibles',
        (tester) async {
      await poserA(tester, const Duration(minutes: 2, seconds: 5));

      // SUR DEUX CHIFFRES : « 2 min 05 », jamais « 2 min 5 ». Un compteur dont
      // la largeur change a chaque seconde fait sautiller la ligne entiere.
      expect(find.text('Commandée il y a 2 min 05'), findsOneWidget);
      await demonter(tester);
    });

    testWidgets('au-dela de dix minutes, les secondes disparaissent',
        (tester) async {
      await poserA(tester, const Duration(minutes: 12, seconds: 34));

      // PASSE DIX MINUTES, LA SECONDE NE VEUT PLUS RIEN DIRE. Un client qui
      // attend depuis douze minutes ne lit pas « 34 » ; il lit « c'est long ».
      expect(find.text('Commandée il y a 12 min'), findsOneWidget);
      await demonter(tester);
    });

    testWidgets('le compteur avance tout seul', (tester) async {
      // C'EST LA SEULE CHOSE QUE CE WIDGET FAIT VRAIMENT. Un bloc fige qui
      // affiche une duree juste au premier rendu et ne bouge plus est
      // exactement le libelle fixe qu'on remplace.
      var ecart = const Duration(seconds: 10);
      await poser(
        tester,
        depuis: commande,
        horloge: () => commande.add(ecart),
      );
      expect(find.text('Commandée il y a 10 s'), findsOneWidget);

      ecart = const Duration(seconds: 11);
      await tester.pump(const Duration(seconds: 1));
      expect(find.text('Commandée il y a 11 s'), findsOneWidget);

      ecart = const Duration(minutes: 1);
      await tester.pump(const Duration(seconds: 49));
      expect(find.text('Commandée il y a 1 min 00'), findsOneWidget);

      await demonter(tester);
    });

    testWidgets('une horloge en avance ne fait pas reculer le compteur',
        (tester) async {
      // L'HORLOGE DU TELEPHONE PEUT DEVANCER CELLE DU SERVICE. L'ecart calcule
      // est alors negatif. Sans l'aplatissement a zero, le client lirait
      // « Commandee il y a -3 s ».
      await poser(
        tester,
        depuis: commande,
        horloge: () => commande.subtract(const Duration(seconds: 3)),
      );

      expect(find.text('Commandée il y a 0 s'), findsOneWidget);
      await demonter(tester);
    });

    testWidgets("sans date, l'explication reste et le compteur disparait",
        (tester) async {
      await poser(tester, depuis: null, horloge: () => commande);

      expect(find.textContaining('Commandée il y a'), findsNothing);
      expect(find.textContaining('un par un'), findsOneWidget);

      // Aucune minuterie ne doit avoir ete armee : sans date, il n'y a rien a
      // compter. Si `initState` en avait lance une, ce demontage la revelerait.
      await demonter(tester);
    });

    testWidgets("le titre et l'explication sont toujours la", (tester) async {
      await poserA(tester, Duration.zero);

      expect(find.text("Recherche d'un livreur"), findsOneWidget);
      expect(
        find.textContaining('du plus proche au plus éloigné'),
        findsOneWidget,
      );
      await demonter(tester);
    });
  });
}
