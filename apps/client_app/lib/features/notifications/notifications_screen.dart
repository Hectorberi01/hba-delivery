import 'package:flutter/material.dart';
import 'package:hba_ui/hba_ui.dart';

/// Écran 23 : les notifications.
///
/// LA CLOCHE DE L'ACCUEIL MENE ICI, ET ELLE NE MENT PAS. Un bouton qui
/// n'ouvre rien est pire qu'un bouton absent : on appuie deux fois, on croit
/// que l'application est cassée, et on cesse de regarder cette cloche.
///
/// IL N'Y A RIEN A LISTER AUJOURD'HUI, ET C'EST ECRIT. Notification envoie des
/// SMS, du WhatsApp et des courriels, et consigne une ligne par tentative dans
/// SA base ; aucune route ne rend ce journal à un client, et l'application ne
/// reçoit aucune notification poussée — la branche « push » du service n'a
/// d'ailleurs aucun producteur. Dresser une liste vide avec des onglets
/// « Toutes / Commandes / Promotions » donnerait l'illusion d'un centre de
/// notifications qui n'existe pas.
class NotificationsScreen extends StatelessWidget {
  const NotificationsScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(
        title: const Text('Notifications'),
        backgroundColor: HbaColors.background,
        elevation: 0,
      ),
      body: SafeArea(
        child: Center(
          child: Padding(
            padding: const EdgeInsets.all(HbaSpacing.xl),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Icon(Icons.notifications_none,
                    size: 48, color: HbaColors.inkFaint),
                const SizedBox(height: HbaSpacing.md),
                Text(
                  'Aucune notification',
                  style: theme.textTheme.titleMedium,
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: HbaSpacing.sm),
                Text(
                  'Les nouvelles de vos commandes vous arrivent par SMS. '
                  'Elles se retrouveront ici quand les notifications de '
                  "l'application seront en place.",
                  textAlign: TextAlign.center,
                  style: theme.textTheme.bodyMedium
                      ?.copyWith(color: HbaColors.inkMuted),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
