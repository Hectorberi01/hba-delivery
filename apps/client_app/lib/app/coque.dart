import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_ui/hba_ui.dart';

/// Coque des quatre onglets de l'application cliente.
///
/// UN INDEXEDSTACK, PAS QUATRE ECRANS QU'ON DETRUIT. Le client bascule sur
/// « Courses » pour relire une reference puis revient : l'accueil ne doit pas
/// avoir perdu sa carte ni redemander la position du telephone, ce qui coute
/// une seconde et un clignotement a chaque aller-retour.
///
/// LA NAVIGATION EST POSEE PAR-DESSUS L'ECRAN, PAS A COTE. L'arc flotte sur le
/// bord droit et la carte d'accueil occupe toute la surface dessous. C'est ce
/// que permet la forme choisie, et c'est sa seule vraie qualite face a une
/// barre en bas : elle ne coupe pas trente pixels d'ecran a un ecran dont le
/// contenu est une carte.
///
/// ELLE EST REPLIEE PAR DEFAUT. Ouverte en permanence, elle posait quatre
/// libelles a nu sur la carte, ou ils se melaient aux noms de villes, et le
/// dernier chevauchait la carte de course en bas. Voir [HbaArcNav].
class Coque extends StatelessWidget {
  const Coque({required this.shell, super.key});

  final StatefulNavigationShell shell;

  static const _onglets = [
    HbaArcItem(icon: Icons.map_outlined, label: 'Accueil'),
    HbaArcItem(icon: Icons.local_shipping_outlined, label: 'Courses'),
    HbaArcItem(icon: Icons.person_outline, label: 'Profil'),
    HbaArcItem(icon: Icons.help_outline, label: 'Aide'),
  ];

  @override
  Widget build(BuildContext context) => Scaffold(
        body: Stack(
          children: [
            Positioned.fill(child: shell),

            // PAS DE SAFEAREA AUTOUR DE LA NAVIGATION, ET C'EST VOULU : son
            // voile doit couvrir l'ecran ENTIER, encoche et barre de geste
            // comprises. Un voile qui s'arrete au bord de la zone sure laisse
            // deux bandes claires en haut et en bas, ce qui se voit. L'arc,
            // lui, respecte les marges par ses propres calculs.
            //
            // REPLIEE, CETTE PILE NE CAPTE RIEN. Elle n'a alors qu'un seul
            // enfant, le bouton : tout le reste du doigt passe au travers et
            // atteint la carte.
            Positioned.fill(
              child: HbaArcNav(
                items: _onglets,
                index: shell.currentIndex,
                // initialLocation: true fait revenir a la racine de la branche
                // quand on retape l'onglet deja ouvert — le geste attendu pour
                // sortir d'une course ouverte depuis l'historique.
                onChange: (i) => shell.goBranch(
                  i,
                  initialLocation: i == shell.currentIndex,
                ),
              ),
            ),
          ],
        ),
      );
}
