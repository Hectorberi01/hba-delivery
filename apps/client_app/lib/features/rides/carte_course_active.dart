import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_ui/hba_ui.dart';

import 'course_models.dart';
import 'course_providers.dart';

/// La course de personnes en cours, sur l'accueil.
///
/// ELLE EST LE SEUL CHEMIN DE RETOUR VERS UNE COURSE COMMENCEE. L'ecran de suivi
/// s'ouvre apres le paiement, mais un passager qui ferme l'application n'a
/// ensuite aucune adresse a taper : sans cette carte, sa course existerait au
/// serveur et serait introuvable sur son telephone.
///
/// ELLE S'EFFACE QUAND IL N'Y A RIEN, plutot que d'afficher « aucune course ».
/// L'accueil est fait pour commander ; une ligne qui annonce une absence occupe
/// la place de ce qu'on vient y faire.
///
/// LA PREMIERE DE LA LISTE, ET C'EST UN CHOIX A REVOIR LE JOUR OU IL GENERA. Le
/// point 35 autorise plusieurs courses a la fois — une pour soi, une pour un
/// proche. Tant qu'il n'y a pas d'ecran pour choisir, montrer la premiere vaut
/// mieux que n'en montrer aucune ; le compteur dit qu'il y en a d'autres.
///
/// ELLE SE RELIT TOUTE SEULE, ET C'EST POUR CA QU'ELLE EST DEVENUE UN WIDGET A
/// ETAT. Le 2 octobre 2026, l'accueil a affiche « En attente de paiement »
/// pendant que la course etait deja payee, cherchee, abandonnee faute de
/// chauffeur et remboursee. Le service disait vrai — une course close sort de
/// « EtatsEnCours », donc de la route — mais personne ne la lui redemandait :
/// la carte gardait l'etat lu a la creation, et le passager voyait une attente
/// qui n'existait plus.
///
/// LE RAFRAICHISSEMENT S'ARRETE DE LUI-MEME. Il ne tourne que tant qu'une
/// course est affichee : des que la liste se vide, le minuteur s'eteint. Un
/// accueil sans course en cours ne fait donc aucune requete — ce qui compte sur
/// un forfait de donnees a Cotonou.
///
/// QUINZE SECONDES, PAS CINQ. L'ecran de suivi sonde toutes les cinq secondes
/// parce qu'on l'ouvre pour regarder avancer ; ici la carte est un rappel au
/// coin de l'ecran, et un quart de minute de retard ne se voit pas.
class CarteCourseActive extends ConsumerStatefulWidget {
  const CarteCourseActive({super.key});

  @override
  ConsumerState<CarteCourseActive> createState() => _CarteCourseActiveState();
}

class _CarteCourseActiveState extends ConsumerState<CarteCourseActive>
    with WidgetsBindingObserver {
  static const _periode = Duration(seconds: 15);

  Timer? _minuteur;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    _minuteur?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState etat) {
    // LE RETOUR DE L'ARRIERE PLAN EST LE MOMENT LE PLUS SUSPECT. Le minuteur ne
    // tourne pas pendant que l'application dort, et c'est justement la que la
    // course avance : le passager rouvre son telephone, et la premiere chose
    // qu'il lit doit etre a jour, pas vieille de la derniere fois qu'il a
    // regarde.
    if (etat == AppLifecycleState.resumed) {
      ref.invalidate(coursesActivesProvider);
    }
  }

  /// Allume ou eteint la sonde selon qu'il y a quelque chose a suivre.
  void _reglerLaSonde({required bool ilYAUneCourse}) {
    if (ilYAUneCourse) {
      _minuteur ??= Timer.periodic(
        _periode,
        (_) => ref.invalidate(coursesActivesProvider),
      );

      return;
    }

    _minuteur?.cancel();
    _minuteur = null;
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final courses = ref.watch(coursesActivesProvider).valueOrNull ?? const <Course>[];

    // PENDANT LE RENDU, ON NE TOUCHE PAS A L'ARBRE : poser le minuteur ici est
    // sans danger — il ne reconstruit rien de synchrone — mais l'invalidation
    // qu'il declenche, elle, doit tomber APRES. Timer.periodic s'en charge : sa
    // premiere salve n'arrive qu'au bout de la periode.
    _reglerLaSonde(ilYAUneCourse: courses.isNotEmpty);

    if (courses.isEmpty) return const SizedBox.shrink();

    final course = courses.first;

    return HbaCard(
      highlighted: true,
      padding: const EdgeInsets.all(HbaSpacing.md),
      onTap: () => context.push('/course/${course.id}'),
      child: Row(
        children: [
          const Icon(Icons.person_outline, color: HbaColors.primaryInk),
          const SizedBox(width: HbaSpacing.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  courses.length > 1
                      ? 'Ma course (${courses.length} en cours)'
                      : 'Ma course',
                  style: theme.textTheme.bodySmall,
                ),
                Text(course.etat.libelle, style: theme.textTheme.titleMedium),
              ],
            ),
          ),
          const Icon(Icons.chevron_right, color: HbaColors.inkFaint),
        ],
      ),
    );
  }
}
