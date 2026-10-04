import 'package:flutter/material.dart';
import 'package:hba_ui/hba_ui.dart';

/// Écran 18 : le wallet ZEDJO.
///
/// CE QU'IL EST, ET CE QU'IL N'EST PAS. Décision du 4 octobre 2026 : ce wallet
/// ne contient QUE des avoirs — ce que ZEDJO doit au client, c'est-à-dire les
/// remboursements d'annulation et les courses payées pour lesquelles aucun
/// livreur n'a été trouvé. Il se dépense sur une prochaine commande et ne se
/// retire pas.
///
/// POURQUOI PAS LE WALLET DE LA MAQUETTE. L'écran 18 montre un solde qu'on
/// recharge et dont on retire. Un solde rechargeable est de la MONNAIE
/// ÉLECTRONIQUE : dans l'UEMOA, en émettre demande un agrément, ou l'adossement
/// à un établissement qui en a un. Ce n'est pas un écran de plus, c'est un
/// régime juridique, et il n'a pas été instruit. Un avoir, lui, est un
/// remboursement que le client choisit de garder en compte plutôt que de se
/// faire renvoyer — personne n'a déposé d'argent chez nous.
///
/// AUCUN CHIFFRE TANT QUE RIEN NE LE CALCULE. Payment tient une file de
/// remboursements dus, traitée à la main par la finance, et aucun grand livre
/// côté client. Afficher « 0 F » laisserait croire qu'un solde a été lu et qu'il
/// vaut zéro ; la phrase ci-dessous dit la vérité, qui est qu'il n'y a encore
/// rien à lire.
class WalletScreen extends StatelessWidget {
  const WalletScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(
        title: const Text('Mon wallet'),
        backgroundColor: HbaColors.background,
        elevation: 0,
      ),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(HbaSpacing.gutter),
          children: [
            HbaCard(
              padding: const EdgeInsets.all(HbaSpacing.lg),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      const Icon(Icons.savings_outlined,
                          color: HbaColors.primaryInk),
                      const SizedBox(width: HbaSpacing.sm),
                      Text('Vos avoirs', style: theme.textTheme.titleMedium),
                    ],
                  ),
                  const SizedBox(height: HbaSpacing.sm),
                  Text(
                    "Quand une course est annulée ou qu'aucun livreur n'est "
                    'trouvé, ce qui vous est dû apparaît ici et se déduit de '
                    'votre prochaine commande.',
                    style: theme.textTheme.bodyMedium
                        ?.copyWith(color: HbaColors.inkMuted),
                  ),
                ],
              ),
            ),
            const SizedBox(height: HbaSpacing.md),
            Text(
              "Vous n'avez aucun avoir pour le moment.",
              style: theme.textTheme.bodyMedium,
            ),
          ],
        ),
      ),
    );
  }
}
