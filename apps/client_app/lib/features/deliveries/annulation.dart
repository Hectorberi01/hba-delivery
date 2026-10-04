import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import 'delivery_providers.dart';
import 'models.dart';

/// Demande confirmation, puis annule la course.
///
/// UNE SEULE COPIE DE CE GESTE, ET C'EST TOUT L'OBJET DE CE FICHIER. Il est
/// appele depuis le suivi ET depuis l'accueil ; deux dialogues qui parlent de
/// remboursement, ce sont deux promesses faites au client le jour ou l'une des
/// deux bouge seule.
///
/// LE MONTANT EST ANNONCE DEPUIS LE 2 OCTOBRE 2026, ET C'EST TOUT CE QUI A
/// CHANGE ICI. Le dialogue disait « les conditions de remboursement vous seront
/// confirmees par le service » — une promesse que personne ne tenait, faute de
/// politique d'annulation (point 3). Elle est tranchee : le service calcule ce
/// qui serait retenu et l'envoie dans la fiche.
///
/// LA REGLE N'EST PAS REJOUEE ICI. « retenueAnnulation » est le resultat du
/// meme appel qui remplit l'evenement d'annulation cote service ; l'ecran ne
/// decide pas si un livreur est affecte, ni combien la grille retient. Un
/// « if (course.driver != null) » dans ce fichier mettrait la regle a deux
/// endroits, et le jour ou l'un bougerait l'ecran promettrait autre chose que
/// ce qui serait preleve.
Future<void> annulerLaCourse(
  BuildContext context,
  WidgetRef ref,
  Delivery course,
) async {
  // LE MESSAGER EST PRIS AVANT L'ATTENTE. Apres le dialogue, le contexte peut
  // avoir disparu ; le chercher a ce moment-la est la faute que l'analyseur
  // signale sous « use_build_context_synchronously ».
  final messager = ScaffoldMessenger.of(context);

  final confirme = await showDialog<bool>(
    context: context,
    builder: (contexteDialogue) => AlertDialog(
      title: const Text('Annuler la livraison ?'),

      // ZERO N'EST PAS « INCONNU », C'EST « GRATUIT », et les deux phrases
      // disent donc des choses opposees. Le cas gratuit est le plus frequent —
      // aucun livreur affecte — et c'est une bonne nouvelle a annoncer, pas un
      // silence a garder.
      content: Text(
        course.retenueAnnulation > 0
            ? 'Le livreur ne viendra pas. Il est déjà en route : '
                '${Xof.format(course.retenueAnnulation)} seront retenus pour son '
                'déplacement, le reste vous sera remboursé.'
            : "Le livreur ne viendra pas. L'annulation est gratuite et vous "
                'serez remboursé intégralement.',
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(contexteDialogue).pop(false),
          child: const Text('Garder'),
        ),
        TextButton(
          onPressed: () => Navigator.of(contexteDialogue).pop(true),
          child: const Text('Annuler la course'),
        ),
      ],
    ),
  );

  if (confirme != true) return;

  try {
    await ref
        .read(deliveryRepositoryProvider)
        .cancel(course.id, 'Annulée par le client');

    // LES TROIS SONT NECESSAIRES : la fiche pour l'ecran de suivi, la liste
    // pour l'accueil, et le marqueur d'attente pour que l'accueil cesse de
    // montrer une course qui n'existe plus.
    ref.invalidate(deliveryProvider(course.id));
    ref.invalidate(deliveriesProvider);
    ref.read(courseEnAttenteProvider.notifier).state = null;
  } on OfflineException {
    // LE SILENCE ETAIT LE DEFAUT, ET IL COUTAIT UNE COURSE.
    //
    // Seule ApiException etait attrapee. Sans reseau, OfflineException
    // remontait ; or les trois appelants invoquent cette fonction en
    // « unawaited », donc l'exception partait dans la zone d'erreur de Flutter
    // et PERSONNE ne voyait rien. Le client venait de confirmer « Annuler la
    // course », l'ecran ne bougeait pas, et il rangeait son telephone en
    // croyant l'affaire reglee — pendant que le livreur roulait vers lui.
    //
    // LE MESSAGE DIT CE QUI N'A PAS EU LIEU, pas ce qui s'est mal passe. « Pas
    // de reseau » seul laisserait croire a un probleme d'affichage ; ce qu'il
    // faut comprendre, c'est que la course est TOUJOURS EN COURS.
    messager.showSnackBar(
      const SnackBar(
        content: Text(
          "Pas de réseau : la course n'est PAS annulée. Elle est toujours en "
          'cours — réessayez.',
        ),
      ),
    );
  } on ApiException catch (erreur) {
    messager.showSnackBar(SnackBar(content: Text(erreur.message)));
  } on Object {
    // MEME RAISON, POUR TOUT LE RESTE. Appelee en « unawaited », cette
    // fonction n'a personne au-dessus d'elle : ce qui n'est pas attrape ici
    // n'est attrape nulle part. Un imprevu doit donc sortir par un message, et
    // ce message doit rester du cote prudent — la course n'a pas ete annulee.
    messager.showSnackBar(
      const SnackBar(
        content: Text(
          "La course n'a pas pu être annulée. Elle est toujours en cours — "
          'réessayez.',
        ),
      ),
    );
  }
}
