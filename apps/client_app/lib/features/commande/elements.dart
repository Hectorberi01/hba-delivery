/// Les briques communes aux deux ecrans de commande.
///
/// POURQUOI UN FICHIER PARTAGE PLUTOT QUE DEUX COPIES. « Envoyer un colis » et
/// « Commander une course » demandent la meme chose dans le meme ordre : d'ou
/// a ou, quoi, combien. Les maquettes les dessinent identiques jusqu'au pixel ;
/// deux implementations divergeraient a la premiere retouche, et un client qui
/// fait les deux gestes dans la meme semaine le verrait.
library;

import 'package:flutter/material.dart';
import 'package:hba_ui/hba_ui.dart';

/// La carte des deux points : d'ou part la course, ou elle arrive.
///
/// LES DEUX POINTS DANS UNE SEULE CARTE, RELIES. Separes, ils se lisaient comme
/// deux formulaires sans rapport ; relies par un trait, ils disent un trajet
/// d'un coup d'oeil. C'est le dessin des maquettes, et c'est aussi celui de
/// toutes les applications de transport — un client qui en a deja utilise une
/// n'a rien a reapprendre.
///
/// LE CREUX PLUTOT QU'UNE CARTE BLANCHE. La feuille est deja blanche : poser
/// du blanc sur du blanc oblige a cerner le bloc d'un filet plus marque. Le
/// creux ivoire se detache sans rien ajouter.
class CarteDuTrajet extends StatelessWidget {
  const CarteDuTrajet({required this.depart, required this.arrivee, super.key});

  final PointDuTrajet depart;
  final PointDuTrajet arrivee;

  /// Largeur de la colonne des pastilles. Le trait vertical passe en son
  /// milieu, et les deux lignes s'alignent dessus.
  static const _colonne = 18.0;

  @override
  Widget build(BuildContext context) => HbaCreux(
        padding: const EdgeInsets.symmetric(
          horizontal: HbaSpacing.md,
          vertical: HbaSpacing.sm,
        ),
        child: Column(
          children: [
            _Ligne(point: depart, couleur: HbaColors.ink),
            Row(
              children: [
                // LE TRAIT VERTICAL TRAVERSE LE SEPARATEUR. C'est ce
                // croisement qui fait un trajet plutot que deux champs
                // empiles : le trait continue, la ligne de separation passe
                // derriere.
                const SizedBox(
                  width: _colonne,
                  child: Center(
                    child: SizedBox(
                      width: 1,
                      height: 20,
                      child: ColoredBox(color: HbaColors.border),
                    ),
                  ),
                ),
                Expanded(
                  child: Container(height: 1, color: HbaColors.border),
                ),
              ],
            ),
            _Ligne(point: arrivee, couleur: HbaColors.primary),
          ],
        ),
      );
}

/// Un bout du trajet : ce qu'on en sait, et comment le changer.
class PointDuTrajet {
  const PointDuTrajet({
    required this.intitule,
    required this.valeur,
    required this.onTap,
    this.renseigne = true,
  });

  /// « Enlevement », « Livraison », « Depart », « Destination ».
  final String intitule;

  /// Ce qui est choisi, ou l'invitation a choisir.
  final String valeur;

  final VoidCallback onTap;

  /// Faux tant que rien n'est choisi : la valeur s'affiche alors en gris, comme
  /// un texte d'aide et non comme une adresse.
  final bool renseigne;
}

class _Ligne extends StatelessWidget {
  const _Ligne({required this.point, required this.couleur});

  final PointDuTrajet point;
  final Color couleur;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Semantics(
      button: true,
      label: '${point.intitule} : ${point.valeur}',
      child: GestureDetector(
        // OPAQUE : la ligne est faite de marges, et sans cela le doigt
        // tomberait entre les deux textes sans rien ouvrir.
        behavior: HitTestBehavior.opaque,
        onTap: point.onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: HbaSpacing.sm),
          child: Row(
            children: [
              SizedBox(
                width: CarteDuTrajet._colonne,
                child: Center(
                  child: Container(
                    height: 9,
                    width: 9,
                    decoration: BoxDecoration(
                      color: couleur,
                      shape: BoxShape.circle,
                    ),
                  ),
                ),
              ),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      point.intitule,
                      style: theme.textTheme.bodySmall
                          ?.copyWith(color: HbaColors.inkMuted),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      point.valeur,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: theme.textTheme.titleMedium?.copyWith(
                        color: point.renseigne
                            ? HbaColors.ink
                            : HbaColors.inkFaint,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Une carte qu'on choisit parmi plusieurs : un vehicule, une taille de colis.
///
/// L'ICONE EST DANS UN CARRE TEINTE, ET LE CARRE SE REMPLIT QUAND LA CARTE EST
/// CHOISIE. Deux signaux pour un seul etat — le contour de la carte et le fond
/// de l'icone — parce qu'un contour terre cuite sur fond ivoire se perd au
/// soleil de Cotonou, qui est la condition d'usage reelle.
///
/// LE PRIX EST A DROITE, JAMAIS SOUS LE TITRE. C'est ce qu'on compare entre
/// deux lignes ; aligne a droite, les montants se lisent l'un sous l'autre.
class CarteDeChoix extends StatelessWidget {
  const CarteDeChoix({
    required this.icone,
    required this.titre,
    required this.detail,
    required this.choisie,
    required this.onTap,
    this.valeur,
    super.key,
  });

  final IconData icone;
  final String titre;

  /// Ce que la ligne dit de plus, en petit sous le titre.
  final String detail;

  final bool choisie;
  final VoidCallback onTap;

  /// Le montant, a droite. Nul tant qu'il n'est pas connu : la place reste
  /// vide plutot que d'afficher un tiret, qui se lirait comme « gratuit ».
  final String? valeur;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final montant = valeur;

    return HbaCard(
      highlighted: choisie,
      onTap: onTap,
      padding: const EdgeInsets.all(HbaSpacing.md),
      child: Row(
        children: [
          Container(
            height: 44,
            width: 44,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: choisie ? HbaColors.primary : HbaColors.surfaceSunken,
              borderRadius: BorderRadius.circular(HbaRadius.field),
            ),
            child: Icon(
              icone,
              size: 22,
              color: choisie ? HbaColors.surface : HbaColors.inkMuted,
            ),
          ),
          const SizedBox(width: HbaSpacing.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(titre, style: theme.textTheme.titleMedium),
                const SizedBox(height: 2),
                Text(
                  detail,
                  style: theme.textTheme.bodySmall
                      ?.copyWith(color: HbaColors.inkMuted),
                ),
              ],
            ),
          ),
          if (montant != null) ...[
            const SizedBox(width: HbaSpacing.sm),
            Text(
              montant,
              style: theme.textTheme.titleMedium
                  ?.copyWith(color: HbaColors.primaryInk),
            ),
          ],
        ],
      ),
    );
  }
}

/// Le pied collant : ce que ca coute, et le geste qui engage.
///
/// IL NE DEFILE PAS, ET C'EST TOUT SON INTERET. Le bouton de confirmation
/// vivait en bas d'une liste : sur un formulaire de dix champs, il fallait
/// faire defiler pour le retrouver, et le prix avait disparu de l'ecran au
/// moment de s'engager. Ici, le montant et le bouton restent cote a cote,
/// toujours visibles — on ne confirme jamais sans voir ce qu'on paie.
///
/// LE FILET DU HAUT PLUTOT QU'UNE OMBRE. Le pied touche le contenu qui passe
/// dessous ; une ombre portee vers le haut salit le dernier champ a chaque
/// defilement, un filet non.
class PiedDeCommande extends StatelessWidget {
  const PiedDeCommande({
    required this.action,
    this.montant,
    this.detail,
    super.key,
  });

  /// Le bouton principal, deja construit par l'ecran : lui seul sait s'il est
  /// occupe, et sous quel libelle.
  final Widget action;

  /// Le prix, en grand. Nul tant qu'aucun devis n'est etabli.
  final String? montant;

  /// Ce qui qualifie le prix : le vehicule, qui paie.
  final String? detail;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final prix = montant;

    return DecoratedBox(
      decoration: const BoxDecoration(
        color: HbaColors.surface,
        border: Border(top: BorderSide(color: HbaColors.border)),
      ),
      child: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(
            HbaSpacing.gutter,
            HbaSpacing.md,
            HbaSpacing.gutter,
            HbaSpacing.md,
          ),
          child: prix == null
              ? action
              : Row(
                  children: [
                    Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Text(
                          prix,
                          style: theme.textTheme.headlineSmall
                              ?.copyWith(fontWeight: FontWeight.w700),
                        ),
                        if (detail != null)
                          Text(
                            detail!,
                            style: theme.textTheme.bodySmall
                                ?.copyWith(color: HbaColors.inkMuted),
                          ),
                      ],
                    ),
                    const SizedBox(width: HbaSpacing.md),
                    Expanded(child: action),
                  ],
                ),
        ),
      ),
    );
  }
}
