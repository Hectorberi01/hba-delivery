import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_ui/hba_ui.dart';

/// Coque des quatre onglets.
///
/// UN INDEXEDSTACK, PAS QUATRE ECRANS QU'ON DETRUIT. Le livreur bascule sur
/// « Courses » pour verifier une reference puis revient : l'accueil ne doit
/// pas avoir perdu son compte a rebours d'offre ni redemander la position.
/// StatefulShellRoute conserve l'etat de chaque branche.
///
/// L'ACCUEIL RESTE LE PREMIER ONGLET, et le seul dont le livreur a besoin en
/// roulant. Les trois autres se consultent a l'arret.
class Coque extends StatelessWidget {
  const Coque({required this.shell, super.key});

  final StatefulNavigationShell shell;

  @override
  Widget build(BuildContext context) => Scaffold(
        body: shell,
        // LA BARRE N'EST PLUS SEPAREE PAR UN FILET, ELLE EST EN RELIEF.
        //
        // Le filet d'un pixel disait « ceci est une autre zone » ; le relief
        // le dit mieux, et dans le vocabulaire du reste de l'application. Il
        // est pose au-dessus de la barre — decalage negatif, pas d'ombre en
        // bas — parce qu'une barre collee au bord de l'ecran n'a rien en
        // dessous ou projeter.
        bottomNavigationBar: DecoratedBox(
          decoration: const BoxDecoration(
            color: HbaColors.surface,
            boxShadow: [
              BoxShadow(
                color: HbaColors.ombre,
                offset: Offset(0, -3),
                blurRadius: 12,
              ),
            ],
          ),
          child: BottomNavigationBar(
            currentIndex: shell.currentIndex,
            // initialLocation: true fait revenir a la racine de la branche
            // quand on retape l'onglet deja ouvert — le geste attendu pour
            // sortir d'une course ouverte depuis l'historique.
            onTap: (index) => shell.goBranch(
              index,
              initialLocation: index == shell.currentIndex,
            ),
            items: const [
              BottomNavigationBarItem(
                icon: Icon(Icons.explore_outlined),
                activeIcon: Icon(Icons.explore),
                label: 'Accueil',
              ),
              BottomNavigationBarItem(
                icon: Icon(Icons.receipt_long_outlined),
                activeIcon: Icon(Icons.receipt_long),
                label: 'Courses',
              ),
              BottomNavigationBarItem(
                icon: Icon(Icons.payments_outlined),
                activeIcon: Icon(Icons.payments),
                label: 'Gains',
              ),
              BottomNavigationBarItem(
                icon: Icon(Icons.person_outline),
                activeIcon: Icon(Icons.person),
                label: 'Profil',
              ),
            ],
          ),
        ),
      );
}
