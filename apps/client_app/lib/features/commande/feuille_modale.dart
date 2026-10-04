/// La presentation en feuille : tout se commande par-dessus la carte.
///
/// POURQUOI DES FEUILLES ET PLUS DES PAGES. Une page pleine remplace la carte
/// par un formulaire blanc : le passager perd de vue ou il est, et l'ecran
/// d'avant n'existe plus que comme une fleche de retour. Les maquettes ne
/// quittent jamais la carte — elle reste derriere, la feuille monte dessus. Le
/// contexte geographique ne disparait a aucun moment du parcours, et c'est ce
/// qui fait qu'on sait toujours de quel trajet on parle.
///
/// LES ROUTES NE CHANGENT PAS. « /nouvelle » et « /nouvelle-course » designent
/// les memes ecrans qu'avant ; seule leur FACON D'APPARAITRE change. Remplacer
/// les routes par des appels a showModalBottomSheet aurait sorti ces ecrans du
/// routeur, et avec eux la reprise de paiement, qui a besoin que l'ecran existe
/// encore au retour de la page du fournisseur.
library;

import 'package:flutter/material.dart';
import 'package:hba_ui/hba_ui.dart';

/// Une page du routeur qui se pose en feuille sur la page precedente.
///
/// LA ROUTE EST TRANSPARENTE, ET C'EST TOUT L'INTERET. ModalBottomSheetRoute
/// descend de PopupRoute : elle ne masque pas ce qu'il y a dessous, donc la
/// carte d'accueil continue de vivre — meme controleur, meme position, memes
/// livreurs — pendant que la feuille est ouverte. Une page ordinaire aurait
/// detruit tout cela et l'aurait reconstruit au retour.
class PageEnFeuille<T> extends Page<T> {
  const PageEnFeuille({required this.enfant, super.key});

  final Widget enfant;

  @override
  Route<T> createRoute(BuildContext context) => ModalBottomSheetRoute<T>(
        settings: this,
        builder: (_) => enfant,

        // SANS CECI, LA FEUILLE S'ARRETE A LA MOITIE DE L'ECRAN. C'est la
        // regle par defaut de Material, pensee pour un menu de trois lignes ;
        // un formulaire d'envoi de colis en fait dix.
        isScrollControlled: true,

        // LE VOILE EST LEGER : la carte doit rester LISIBLE derriere, pas
        // seulement devinee. Le noir a 54 % de Material en ferait une image
        // eteinte, et on perdrait justement ce qu'on cherche a garder.
        modalBarrierColor: const Color(0x33000000),

        // LA FEUILLE PORTE SON PROPRE FOND ET SES PROPRES COINS (voir
        // [FeuilleModale]) : en ajouter ici poserait un second rectangle blanc
        // derriere le premier, visible sur les coins arrondis.
        backgroundColor: Colors.transparent,
        elevation: 0,
      );
}

/// Le dessin d'une feuille : un en-tete qui flotte, puis le bloc blanc.
///
/// L'EN-TETE EST AU-DESSUS DE LA FEUILLE, PAS DEDANS. Le bouton de retour et la
/// pastille du titre se posent sur la carte, dans l'espace que la feuille ne
/// couvre pas. C'est ce que montrent les maquettes, et ce n'est pas qu'une
/// question de gout : une barre de titre DANS la feuille mangerait une ligne de
/// contenu sur un ecran deja court.
///
/// LA POIGNEE NE TIRE RIEN ICI. Elle dit que la feuille se referme — et le
/// geste marche, Material s'en charge. Elle ne redimensionne pas : une feuille
/// a deux hauteurs demanderait de choisir ce qu'on montre a la hauteur basse,
/// et il n'y a rien a montrer de moins qu'un formulaire incomplet.
class FeuilleModale extends StatelessWidget {
  const FeuilleModale({
    required this.enfant,
    this.titre,
    this.icone,
    this.pied,
    this.retourPossible = true,
    super.key,
  });

  /// Le contenu defilant de la feuille.
  final Widget enfant;

  /// La pastille posee a cote du bouton de retour. Nulle quand la feuille dit
  /// deja son titre dans son contenu, comme le choix du trajet.
  final String? titre;

  final IconData? icone;

  /// Le pied collant, hors du defilement.
  final Widget? pied;

  /// Faux pendant qu'une operation ne doit pas etre interrompue.
  ///
  /// IL FERME LES TROIS SORTIES A LA FOIS : le bouton, le voile et le
  /// glissement vers le bas. Fermer la feuille pendant qu'une commande part
  /// detruirait l'ecran qui attend la reponse — et avec lui l'adresse de
  /// paiement qu'il allait ouvrir.
  final bool retourPossible;

  /// Ce que la feuille laisse voir de la carte, au plus.
  ///
  /// QUATRE-VINGTS POUR CENT, PAS QUATRE-VINGT-DIX-NEUF. Il faut qu'il reste
  /// assez de carte pour que la feuille se lise comme posee dessus : collee en
  /// haut de l'ecran, elle redevient une page pleine avec un bandeau blanc.
  static const _partDeLEcran = 0.80;

  @override
  Widget build(BuildContext context) {
    final hauteur = MediaQuery.sizeOf(context).height;

    // CE QUE LE CLAVIER PREND, LA FEUILLE LE REND. Une feuille a hauteur
    // controlee ne se decale pas toute seule : sans cette marge, le clavier se
    // poserait PAR-DESSUS les six champs du formulaire d'envoi, et le client
    // taperait a l'aveugle dans un champ qu'il ne voit plus.
    final clavier = MediaQuery.viewInsetsOf(context).bottom;
    final disponible = hauteur - clavier;

    // ET ELLE REPREND CE QU'ELLE PEUT QUAND LE CLAVIER EST LA. Garder quatre-
    // vingts pour cent d'un ecran deja ampute de moitie ne laisserait voir que
    // deux lignes ; la part de carte visible n'a plus d'importance a ce
    // moment-la, puisqu'on est en train d'ecrire.
    final maximum = disponible * (clavier > 0 ? 0.95 : _partDeLEcran);

    return PopScope(
      canPop: retourPossible,
      child: Padding(
        padding: EdgeInsets.only(bottom: clavier),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Padding(
              padding: const EdgeInsets.fromLTRB(
                HbaSpacing.gutter,
                0,
                HbaSpacing.gutter,
                HbaSpacing.sm,
              ),
              child: Row(
                children: [
                  _Retour(actif: retourPossible),
                  if (titre != null) ...[
                    const SizedBox(width: HbaSpacing.sm),
                    Flexible(child: _Pastille(titre: titre!, icone: icone)),
                  ],
                ],
              ),
            ),
            ConstrainedBox(
              constraints: BoxConstraints(maxHeight: maximum),
              child: DecoratedBox(
                decoration: const BoxDecoration(
                  color: HbaColors.surface,
                  borderRadius: BorderRadius.vertical(
                    top: Radius.circular(HbaRadius.feuille),
                  ),
                ),
                child: SafeArea(
                  top: false,
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const _Poignee(),

                      // FLEXIBLE ET NON EXPANDED : une feuille courte doit rester
                      // courte. Expanded l'etirerait jusqu'aux quatre-vingts pour
                      // cent a chaque fois, et le choix du trajet — trois lignes
                      // et un bouton — occuperait tout l'ecran pour rien.
                      Flexible(child: enfant),
                      if (pied != null) pied!,
                    ],
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Retour extends StatelessWidget {
  const _Retour({required this.actif});

  final bool actif;

  @override
  Widget build(BuildContext context) => Semantics(
        button: true,
        label: 'Retour',
        child: GestureDetector(
          behavior: HitTestBehavior.opaque,

          // maybePop ET NON pop : c'est lui qui passe par PopScope, donc qui
          // respecte le verrou pose pendant une commande en cours.
          onTap: actif ? () => Navigator.of(context).maybePop() : null,
          child: Container(
            height: HbaSpacing.cible,
            width: HbaSpacing.cible,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: HbaColors.surface,
              shape: BoxShape.circle,
              boxShadow: HbaOmbres.relief(HbaElevation.haute),
            ),
            child: Icon(
              Icons.chevron_left,
              color: actif ? HbaColors.ink : HbaColors.inkFaint,
            ),
          ),
        ),
      );
}

class _Pastille extends StatelessWidget {
  const _Pastille({required this.titre, this.icone});

  final String titre;
  final IconData? icone;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      padding: const EdgeInsets.symmetric(
        horizontal: HbaSpacing.md,
        vertical: HbaSpacing.sm,
      ),
      decoration: BoxDecoration(
        color: HbaColors.surface,
        borderRadius: BorderRadius.circular(HbaRadius.chip),
        boxShadow: HbaOmbres.relief(HbaElevation.haute),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (icone != null) ...[
            Icon(icone, size: 18, color: HbaColors.primary),
            const SizedBox(width: HbaSpacing.sm),
          ],
          Flexible(
            child: Text(
              titre,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: theme.textTheme.titleMedium,
            ),
          ),
        ],
      ),
    );
  }
}

class _Poignee extends StatelessWidget {
  const _Poignee();

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.symmetric(vertical: HbaSpacing.sm),
        child: Center(
          child: Container(
            height: 4,
            width: 44,
            decoration: BoxDecoration(
              color: HbaColors.border,
              borderRadius: BorderRadius.circular(2),
            ),
          ),
        ),
      );
}
