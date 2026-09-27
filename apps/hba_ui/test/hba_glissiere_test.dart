import 'package:flutter/material.dart';
import 'package:flutter/semantics.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hba_ui/hba_ui.dart';

/// LE RAIL EST UNE PROTECTION, ET UNE PROTECTION SE MESURE AUX DEUX BOUTS :
/// elle doit laisser passer le geste voulu, et retenir celui qui ne l'est pas.
/// Un seuil trop haut fait revenir le rail sous le pouce d'un livreur qui
/// croyait avoir accepte ; trop bas, il redevient un bouton qu'une secousse
/// declenche.
///
/// LES LARGEURS SONT CHOISIES POUR QUE LE CALCUL SOIT LISIBLE. Le rail fait
/// 400 px, le curseur 50 et les marges 4 de chaque cote : la course utile vaut
/// 342 px, et le seuil de 70 % tombe a 239 px. Le glissement de Flutter mange
/// en plus une vingtaine de pixels de « touch slop » avant que le geste ne
/// commence — les distances ci-dessous en tiennent compte.
const _largeur = 400.0;

Future<void> poser(
  WidgetTester tester, {
  required VoidCallback? onConfirme,
  bool busy = false,
}) =>
    tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: Center(
            child: SizedBox(
              width: _largeur,
              child: HbaGlissiere(
                libelle: 'Glissez pour accepter',
                busy: busy,
                onConfirme: onConfirme,
              ),
            ),
          ),
        ),
      ),
    );

Finder get curseur => find.byIcon(Icons.arrow_forward_rounded);

/// Apres une confirmation, DEUX CHOSES RESTENT EN VOL : l'animation qui pousse
/// le curseur au bout, et le retour differe de 900 ms qui ramene le rail quand
/// l'action ne passe pas par « busy ». Les laisser pendantes ferait echouer le
/// test sur un minuteur oublie — ce qui serait un faux negatif, mais aussi le
/// signe qu'on n'a pas compris ce que le widget laisse derriere lui.
Future<void> laisserRetomber(WidgetTester tester) async {
  await tester.pump(const Duration(milliseconds: 1200));
  await tester.pumpAndSettle();
}

void main() {
  testWidgets('un glissement court ne confirme pas', (tester) async {
    var confirme = 0;
    await poser(tester, onConfirme: () => confirme++);

    // 120 px, soit un tiers de la course : le geste d'un pouce qui derape.
    await tester.timedDrag(
      curseur,
      const Offset(120, 0),
      const Duration(milliseconds: 600),
    );
    await tester.pumpAndSettle();

    expect(confirme, 0);
  });

  testWidgets('un glissement au-dela du seuil confirme une fois', (tester) async {
    var confirme = 0;
    await poser(tester, onConfirme: () => confirme++);

    await tester.timedDrag(
      curseur,
      const Offset(300, 0),
      const Duration(milliseconds: 600),
    );
    await tester.pump();

    expect(confirme, 1);
    await laisserRetomber(tester);
  });

  testWidgets('une detente rapide confirme sans atteindre le seuil', (tester) async {
    // Le geste de quelqu'un qui sait deja ce qu'il fait : court, mais lance.
    var confirme = 0;
    await poser(tester, onConfirme: () => confirme++);

    await tester.timedDrag(
      curseur,
      const Offset(90, 0),
      const Duration(milliseconds: 30),
    );
    await tester.pump();

    expect(confirme, 1);
    await laisserRetomber(tester);
  });

  testWidgets('un rail desactive ne confirme jamais', (tester) async {
    await poser(tester, onConfirme: null);

    await tester.timedDrag(
      curseur,
      const Offset(360, 0),
      const Duration(milliseconds: 600),
    );
    await tester.pumpAndSettle();

    // Rien a verifier de plus que l'absence de plantage et de mouvement : le
    // rappel est nul, il ne peut pas etre appele.
    expect(tester.takeException(), isNull);
  });

  testWidgets('pendant l\'envoi, un second glissement ne repart pas', (tester) async {
    // DEUX FOIS LA MEME ETAPE, C'EST DEUX APPELS AU SERVEUR. Le livreur qui
    // trouve que c'est long recommence : le rail doit l'ignorer.
    var confirme = 0;
    await poser(tester, onConfirme: () => confirme++, busy: true);

    // PAS « curseur » ICI : pendant l'envoi, le curseur porte un indicateur
    // d'attente a la place de la fleche, et le chercher par son icone ne
    // trouverait rien. Les gestes sont de toute facon desactives.
    await tester.timedDrag(
      find.byType(HbaGlissiere),
      const Offset(360, 0),
      const Duration(milliseconds: 600),
    );
    await tester.pumpAndSettle();

    expect(confirme, 0);
  });

  testWidgets('un lecteur d\'ecran active le rail par un appui', (tester) async {
    // EXIGER LE GESTE FERMERAIT L'APPLICATION A QUI NE PEUT PAS LE FAIRE.
    // C'est le seul chemin d'acceptation pour un livreur malvoyant.
    final semantique = tester.ensureSemantics();
    var confirme = 0;

    await poser(tester, onConfirme: () => confirme++);

    final noeud = tester.getSemantics(find.byType(HbaGlissiere));
    tester.binding.pipelineOwner.semanticsOwner!
        .performAction(noeud.id, SemanticsAction.tap);
    await tester.pump();

    expect(confirme, 1);

    await laisserRetomber(tester);
    semantique.dispose();
  });

  testWidgets('le libelle est annonce', (tester) async {
    await poser(tester, onConfirme: () {});
    expect(find.text('Glissez pour accepter'), findsOneWidget);
  });
}
