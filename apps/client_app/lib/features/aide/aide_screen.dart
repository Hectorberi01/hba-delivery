import 'package:flutter/material.dart';
import 'package:hba_ui/hba_ui.dart';

import '../profil/support.dart';

/// L'aide du client.
///
/// LES REPONSES SONT ECRITES EN DUR, ET C'EST UN CHOIX. Une base de questions
/// servie par une route se justifie le jour ou elles changent sans
/// redeploiement. Aujourd'hui elles ne changent pas, et une route de plus
/// serait une route de plus a maintenir pour six paragraphes.
///
/// LE CONTACT EST LE MEME WIDGET QUE DANS LE PROFIL. Le client cherche le
/// support aux deux endroits, et aucun des deux n'est le mauvais ; le
/// dupliquer ferait diverger les deux le jour ou l'un change. Voir
/// [BlocSupport], qui n'affiche une entree que si elle ouvre reellement
/// quelque chose.
class AideScreen extends StatelessWidget {
  const AideScreen({super.key});

  static const _questions = [
    (
      'Quand mon paiement est-il confirmé ?',
      "Dès que l'opérateur nous le confirme, et jamais avant. L'écran de suivi "
          "passe alors tout seul de « en attente de paiement » à la recherche "
          "d'un livreur. Vous n'avez rien à faire.",
    ),
    (
      'Pourquoi la recherche d\'un livreur prend du temps ?',
      "HBA contacte les livreurs un par un, du plus proche au plus éloigné. "
          "Chacun dispose de quelques secondes pour répondre. C'est plus long "
          "qu'une diffusion à tout le monde, et cela vous donne le livreur le "
          "plus proche plutôt que le plus rapide à appuyer.",
    ),
    (
      'À quoi sert le code de remise ?',
      "C'est lui qui distingue « le colis a été remis » de « quelqu'un a "
          "cliqué ». Communiquez-le au destinataire : le livreur le lui "
          "demandera, et la course ne peut pas être clôturée sans.",
    ),
    (
      'Quand puis-je voir le livreur ?',
      "Son nom, son véhicule et son téléphone s'affichent dès qu'il a accepté "
          "votre course, et jusqu'à la fin de la mission.",
    ),
    (
      'Puis-je annuler ?',
      "Oui, tant que le livreur n'est pas encore en route vers la collecte. "
          "Le bouton disparaît de l'écran de suivi au-delà.",
    ),
  ];

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(title: const Text('Aide')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(HbaSpacing.gutter),
          children: [
            Text('QUESTIONS FRÉQUENTES', style: theme.textTheme.bodySmall),
            const SizedBox(height: HbaSpacing.sm),
            for (final (question, reponse) in _questions) ...[
              HbaCard(
                padding: EdgeInsets.zero,
                child: Theme(
                  // Le trait de séparation par défaut de Material coupe la
                  // carte en deux ; le relief suffit à la délimiter.
                  data: theme.copyWith(dividerColor: Colors.transparent),
                  child: ExpansionTile(
                    title: Text(question, style: theme.textTheme.titleMedium),
                    childrenPadding: const EdgeInsets.fromLTRB(
                      HbaSpacing.lg,
                      0,
                      HbaSpacing.lg,
                      HbaSpacing.lg,
                    ),
                    expandedCrossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(reponse, style: theme.textTheme.bodyMedium),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: HbaSpacing.sm),
            ],

            const SizedBox(height: HbaSpacing.lg),
            const BlocSupport(),
            const SizedBox(height: HbaSpacing.lg),
          ],
        ),
      ),
    );
  }
}
