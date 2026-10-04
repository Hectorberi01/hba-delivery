import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_ui/hba_ui.dart';

/// Écran 1 : la première chose que voit quelqu'un qui ouvre ZEDJO.
///
/// IL N'EXISTAIT PAS. L'application s'ouvrait sur un rond qui tourne, puis
/// jetait l'inconnu sur un champ « votre numero de telephone » — sans jamais
/// avoir dit ce qu'elle est. Un numero de telephone est ce qu'on donne en
/// dernier, pas en premier.
///
/// LES DEUX BOUTONS MENENT AU MEME ECRAN, ET C'EST ASSUME. La connexion se
/// fait par un code recu par SMS : il n'y a pas de formulaire d'inscription a
/// part, et le service cree le compte au premier code verifie. Les garder tous
/// les deux n'est donc pas un mensonge mais une politesse — « je n'ai pas de
/// compte » et « j'en ai un » sont les deux questions que se pose la personne
/// devant l'ecran, et lui repondre « c'est pareil » avant qu'elle ait demande
/// serait repondre a cote.
///
/// L'ILLUSTRATION DE LA MAQUETTE N'EST PAS LA. Le livreur a moto sur fond de
/// ville est un dessin, pas une forme : il se pose en image dans
/// « assets/bienvenue.webp » et vient remplacer le degrade, sans toucher au
/// reste de l'ecran. Tant qu'il n'existe pas, un aplat propre vaut mieux qu'un
/// gribouillage vectoriel.
class BienvenueScreen extends StatelessWidget {
  const BienvenueScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      body: Column(
        children: [
          Expanded(
            child: DecoratedBox(
              decoration: const BoxDecoration(
                gradient: LinearGradient(
                  begin: Alignment.topCenter,
                  end: Alignment.bottomCenter,
                  colors: HbaColors.balanceGradient,
                ),
              ),
              child: SafeArea(
                bottom: false,
                child: Center(
                  child: Column(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                      const ZedjoLogo(taille: 96),
                      const SizedBox(height: HbaSpacing.lg),
                      Text(
                        'ZEDJO',
                        style: theme.textTheme.displaySmall?.copyWith(
                          color: HbaColors.surface,
                          fontWeight: FontWeight.w800,
                          letterSpacing: 2,
                        ),
                      ),
                      const SizedBox(height: HbaSpacing.xs),
                      Text(
                        'Livraison & Transport',
                        style: theme.textTheme.titleMedium?.copyWith(
                          color: HbaColors.surface.withValues(alpha: 0.92),
                        ),
                      ),
                      const SizedBox(height: HbaSpacing.sm),
                      Text(
                        'Bougeons ensemble, plus loin.',
                        style: theme.textTheme.bodyMedium?.copyWith(
                          color: HbaColors.surface.withValues(alpha: 0.80),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
          SafeArea(
            top: false,
            child: Padding(
              padding: const EdgeInsets.all(HbaSpacing.gutter),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  HbaButton(
                    label: 'Créer un compte',
                    onPressed: () => context.go('/connexion'),
                  ),
                  const SizedBox(height: HbaSpacing.sm),
                  HbaButton(
                    label: "J'ai déjà un compte",
                    tone: HbaButtonTone.neutral,
                    onPressed: () => context.go('/connexion'),
                  ),
                  const SizedBox(height: HbaSpacing.sm),

                  // LA MENTION LEGALE EST ICI ET NULLE PART AILLEURS. Elle doit
                  // etre lisible AVANT de donner son numero, pas apres : c'est
                  // le moment ou la personne decide.
                  Text(
                    "En continuant, vous acceptez nos conditions d'utilisation "
                    'et notre politique de confidentialité.',
                    textAlign: TextAlign.center,
                    style: theme.textTheme.bodySmall
                        ?.copyWith(color: HbaColors.inkMuted),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}
