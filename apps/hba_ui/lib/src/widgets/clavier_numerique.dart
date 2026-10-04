import 'package:flutter/material.dart';

import '../tokens.dart';

/// Le pavé numérique de l'application, à la place de celui du système.
///
/// POURQUOI NE PAS PRENDRE CELUI DU TELEPHONE. Le clavier systeme monte et
/// descend, change de hauteur d'un appareil a l'autre, et pousse le contenu
/// vers le haut : un ecran compose pour lui est un ecran qui saute. Celui-ci
/// fait partie de la page, sa hauteur est connue a la construction, et rien ne
/// bouge quand on tape. C'est ce que montrent les maquettes des ecrans de
/// connexion et de code.
///
/// LES LETTRES SOUS LES CHIFFRES NE SONT PAS DECORATIVES. Un numero beninois se
/// dicte et se retient souvent par groupes ; les lettres sont le reperage que
/// tout le monde connait depuis le cadran du telephone, et elles aident a
/// retrouver une touche sans la chercher des yeux.
///
/// IL N'EST PAS UN CHAMP. Il n'a aucun etat : il annonce ce qu'on a presse, et
/// c'est l'ecran qui tient la saisie. Deux ecrans s'en servent pour deux
/// saisies qui n'ont rien a voir — un numero et un code — sans rien partager.
class ClavierNumerique extends StatelessWidget {
  const ClavierNumerique({
    required this.onChiffre,
    required this.onEffacer,
    this.actif = true,
    super.key,
  });

  final ValueChanged<String> onChiffre;
  final VoidCallback onEffacer;

  /// Grise le pavé pendant un envoi. LE LAISSER VIVANT PENDANT L'ATTENTE
  /// laisserait taper des chiffres qui n'iraient nulle part.
  final bool actif;

  static const _lettres = {
    '2': 'ABC',
    '3': 'DEF',
    '4': 'GHI',
    '5': 'JKL',
    '6': 'MNO',
    '7': 'PQRS',
    '8': 'TUV',
    '9': 'WXYZ',
  };

  @override
  Widget build(BuildContext context) => Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          for (final ligne in const [
            ['1', '2', '3'],
            ['4', '5', '6'],
            ['7', '8', '9'],
          ])
            _Ligne(
              children: [
                for (final chiffre in ligne)
                  _Touche(
                    chiffre: chiffre,
                    lettres: _lettres[chiffre],
                    onPressed: actif ? () => onChiffre(chiffre) : null,
                  ),
              ],
            ),
          _Ligne(
            children: [
              // LA CASE VIDE GARDE LA GRILLE DROITE. Sans elle, le zero se
              // decale a gauche et la rangee du bas ne ressemble plus aux
              // trois autres.
              const Expanded(child: SizedBox.shrink()),
              _Touche(
                chiffre: '0',
                onPressed: actif ? () => onChiffre('0') : null,
              ),
              _Touche(
                icone: Icons.backspace_outlined,
                etiquette: 'Effacer',
                onPressed: actif ? onEffacer : null,
              ),
            ],
          ),
        ],
      );
}

class _Ligne extends StatelessWidget {
  const _Ligne({required this.children});

  final List<Widget> children;

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(vertical: HbaSpacing.xs),
        child: Row(children: children),
      );
}

class _Touche extends StatelessWidget {
  const _Touche({
    required this.onPressed,
    this.chiffre,
    this.lettres,
    this.icone,
    this.etiquette,
  });

  final VoidCallback? onPressed;
  final String? chiffre;
  final String? lettres;
  final IconData? icone;
  final String? etiquette;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final eteint = onPressed == null;

    return Expanded(
      child: Semantics(
        button: true,
        label: etiquette ?? chiffre,
        child: InkWell(
          onTap: onPressed,
          borderRadius: BorderRadius.circular(HbaRadius.field),
          child: SizedBox(
            // CINQUANTE-SIX POINTS, AU-DESSUS DES QUARANTE-HUIT DU MINIMUM.
            // Une touche de clavier se presse vite et sans regarder : c'est le
            // seul endroit de l'application ou la cible merite d'etre plus
            // grande que la regle.
            height: 56,
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                if (icone != null)
                  Icon(
                    icone,
                    size: 24,
                    color: eteint ? HbaColors.inkFaint : HbaColors.ink,
                  )
                else ...[
                  Text(
                    chiffre ?? '',
                    style: theme.textTheme.headlineSmall?.copyWith(
                      fontWeight: FontWeight.w600,
                      color: eteint ? HbaColors.inkFaint : HbaColors.ink,
                    ),
                  ),
                  if (lettres != null)
                    Text(
                      lettres!,
                      style: theme.textTheme.labelSmall?.copyWith(
                        fontSize: 9,
                        letterSpacing: 1.1,
                        color: HbaColors.inkFaint,
                      ),
                    ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }
}
