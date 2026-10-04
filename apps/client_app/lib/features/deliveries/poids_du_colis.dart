import 'package:flutter/material.dart';
import 'package:hba_ui/hba_ui.dart';

/// Le poids declare, regle au pouce entre deux bornes.
///
/// POURQUOI DES BOUTONS ET NON UN CHAMP DE SAISIE. Le champ libre existait
/// avant les tailles, et il a ete supprime pour une raison qui tient toujours :
/// il ouvre le clavier numerique par-dessus la moitie du formulaire, il accepte
/// « 4 » comme « 4000 » sans que personne sache lequel etait voulu, et il
/// demande une precision que le client n'a pas. Deux boutons bornes ne peuvent
/// produire qu'un poids valide.
///
/// LE PAS CHANGE AVEC L'ECHELLE. Sous le kilo on avance de cent grammes — c'est
/// la ou vivent les enveloppes et les documents, et cinq cents grammes y
/// doublerait le colis ; au-dela, de cinq cents grammes, sinon il faut quarante
/// appuis pour declarer vingt kilos.
///
/// PAS DE BASCULE kg / g, CONTRAIREMENT AUX MAQUETTES. L'unite n'est pas une
/// preference : elle decoule du poids. Sous le kilo l'affichage dit « 800 g »,
/// au-dessus « 2,5 kg ». Une bascule laisserait le client lire « 800 » en
/// pensant kilos, ce qu'aucun affichage automatique ne permet.
class SelecteurDePoids extends StatelessWidget {
  const SelecteurDePoids({
    required this.grammes,
    required this.plafondGrammes,
    required this.onChange,
    super.key,
  });

  final int grammes;

  /// Le plafond de la taille choisie. Voir TailleDuColis.plafondGrammes.
  final int plafondGrammes;

  final void Function(int) onChange;

  /// Le colis le plus leger qu'on accepte de declarer.
  ///
  /// CENT GRAMMES ET NON ZERO. Un colis de zero gramme n'existe pas, et la
  /// grille facturerait alors la seule distance : autant laisser la borne dire
  /// « une enveloppe ».
  static const planche = 100;

  static int pas(int grammes) => grammes < 1000 ? 100 : 500;

  /// « 800 g », « 2 kg », « 2,5 kg ».
  ///
  /// LA VIRGULE EST CELLE DU FRANCAIS, et le demi-kilo ne s'ecrit pas « 2.5 ».
  static String libelle(int grammes) {
    if (grammes < 1000) return '$grammes g';

    final kilos = grammes / 1000;
    final texte = kilos.toStringAsFixed(kilos == kilos.roundToDouble() ? 0 : 1);
    return '${texte.replaceAll('.', ',')} kg';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    // LE PAS DE LA DESCENTE EST CELUI DU POIDS D'ARRIVEE, PAS DU DEPART. A
    // 1000 g, descendre de cinq cents grammes sauterait 900 g, 800 g et tout
    // le domaine des enveloppes d'un seul appui.
    final moins = grammes - pas(grammes - 1);
    final plus = grammes + pas(grammes);

    return Row(
      children: [
        _Bouton(
          icone: Icons.remove,
          etiquette: 'Alléger',
          onTap: grammes > planche
              ? () => onChange(moins < planche ? planche : moins)
              : null,
        ),
        Expanded(
          child: Text(
            libelle(grammes),
            textAlign: TextAlign.center,
            style: theme.textTheme.titleMedium?.copyWith(
              fontWeight: FontWeight.w700,
            ),
          ),
        ),
        _Bouton(
          icone: Icons.add,
          etiquette: 'Alourdir',
          onTap: grammes < plafondGrammes
              ? () => onChange(plus > plafondGrammes ? plafondGrammes : plus)
              : null,
        ),
      ],
    );
  }
}

/// Un rond de reglage. Eteint, il reste visible : la borne est une information.
class _Bouton extends StatelessWidget {
  const _Bouton({
    required this.icone,
    required this.etiquette,
    required this.onTap,
  });

  final IconData icone;
  final String etiquette;

  /// Nul au bord de la plage.
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final actif = onTap != null;

    return Semantics(
      button: true,
      enabled: actif,
      label: etiquette,
      child: Material(
        color: actif ? HbaColors.primarySoft : HbaColors.surfaceSunken,
        shape: const CircleBorder(),
        child: InkWell(
          customBorder: const CircleBorder(),
          onTap: onTap,
          child: SizedBox(
            height: HbaSpacing.cible,
            width: HbaSpacing.cible,
            child: Icon(
              icone,
              size: 22,
              color: actif ? HbaColors.primary : HbaColors.inkFaint,
            ),
          ),
        ),
      ),
    );
  }
}
