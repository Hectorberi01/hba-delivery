import 'package:flutter/material.dart';
import 'package:hba_ui/hba_ui.dart';

/// Une ligne de l'ecran profil : icone dans un carre teinte, titre, detail,
/// chevron.
///
/// UNE SEULE FORME POUR TOUTES LES LIGNES, ET C'EST LA RAISON D'ETRE DE CE
/// FICHIER. Le support, les documents legaux et les reglages avaient chacun
/// leur propre mise en page : trois codes pour trois lignes qui se ressemblent,
/// et qui divergeaient a chaque retouche. La maquette les dessine identiques ;
/// le code doit l'etre aussi, sinon la ressemblance ne tiendra pas trois
/// semaines.
///
/// LE CHEVRON N'EST PAS DECORATIF. Il dit qu'un appui ouvre quelque chose. Une
/// ligne sans action n'en porte pas — c'est a cela qu'on distingue, d'un coup
/// d'oeil, ce qui se touche de ce qui s'affiche.
class HbaTuile extends StatelessWidget {
  const HbaTuile({
    required this.icone,
    required this.titre,
    this.detail,
    this.teinte,
    this.couleurIcone,
    this.onTap,
    this.fin,
    super.key,
  });

  final IconData icone;
  final String titre;
  final String? detail;

  /// Fond du carre de l'icone. Le gris doux par defaut ; une teinte pour
  /// distinguer une famille de lignes, comme dans la maquette.
  final Color? teinte;

  final Color? couleurIcone;

  final VoidCallback? onTap;

  /// Ce qui se place a droite a la place du chevron : un interrupteur, par
  /// exemple. Un chevron ET un interrupteur sur la meme ligne annoncent deux
  /// gestes la ou il n'y en a qu'un.
  final Widget? fin;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.md),
      onTap: onTap,
      child: Row(
        children: [
          Container(
            height: 44,
            width: 44,
            decoration: BoxDecoration(
              color: teinte ?? HbaColors.surfaceSunken,
              borderRadius: BorderRadius.circular(HbaRadius.field),
            ),
            child: Icon(icone, size: 22, color: couleurIcone ?? HbaColors.ink),
          ),
          const SizedBox(width: HbaSpacing.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(titre, style: theme.textTheme.titleMedium),
                if (detail != null) ...[
                  const SizedBox(height: 2),
                  Text(
                    detail!,
                    style: theme.textTheme.bodyMedium
                        ?.copyWith(color: HbaColors.inkMuted),
                  ),
                ],
              ],
            ),
          ),
          if (fin != null)
            fin!
          else if (onTap != null) ...[
            const SizedBox(width: HbaSpacing.sm),
            const Icon(Icons.chevron_right, color: HbaColors.inkFaint),
          ],
        ],
      ),
    );
  }
}

/// Le titre d'une section, en petites capitales espacees.
class HbaSection extends StatelessWidget {
  const HbaSection(this.libelle, {super.key});

  final String libelle;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.only(
          left: HbaSpacing.xs,
          bottom: HbaSpacing.sm,
        ),
        child: Text(
          libelle.toUpperCase(),
          style: Theme.of(context).textTheme.bodySmall?.copyWith(
                color: HbaColors.inkFaint,
                letterSpacing: 0.8,
                fontWeight: FontWeight.w600,
              ),
        ),
      );
}
