import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:hba_ui/hba_ui.dart';

/// Mesures du panneau. UN SEUL ENDROIT : la largeur depliee sert a la fois a
/// dessiner le contenu et a borner le deplacement, et deux constantes qui
/// devraient etre egales finissent toujours par ne plus l'etre.
abstract final class _Mesures {
  static const largeur = 196.0;

  /// Repliee, la pastille ne montre plus que la poignee et le point d'etat.
  /// 40 (la poignee) + 10 (le point) + 8 (l'espace) : la coupe tombe donc
  /// exactement la ou commence le libelle, sans en laisser depasser une
  /// lettre.
  static const largeurRepliee = 58.0;

  static const hauteur = 44.0;

  /// Marge gardee avec les bords de l'ecran.
  static const marge = HbaSpacing.gutter;
}

/// La bascule en ligne / hors ligne, flottante et deplacable sur la carte.
///
/// POURQUOI ELLE SE DEPLACE. C'etait une barre pleine largeur collee en bas :
/// elle masquait la portion de carte qui interesse justement le livreur —
/// celle ou il se trouve. Un element pose sur une carte doit pouvoir s'ecarter,
/// parce que ce qu'il cache change a chaque minute.
///
/// ELLE NE DISPARAIT JAMAIS COMPLETEMENT, ET C'EST DELIBERE. Repliee, elle se
/// reduit a une pastille qui continue d'afficher l'etat : le point vert ou gris
/// reste visible. Un bouton « masquer » qui l'efface tout a fait produirait le
/// pire scenario de cette application — un livreur hors ligne qui se croit en
/// ligne, ou qui ne retrouve pas de quoi se remettre en ligne. Se replier coute
/// un appui pour revenir ; se cacher couterait une course.
///
/// ELLE NE PEUT PAS SORTIR DE L'ECRAN, ni descendre sur la mention Google en
/// bas a gauche, que les conditions d'utilisation de Maps interdisent de
/// masquer.
///
/// SA POSITION NE SURVIT PAS A UNE FERMETURE DE L'APPLICATION. Elle tient dans
/// l'etat de ce widget, donc elle traverse les changements d'onglet — la coque
/// garde les branches vivantes — mais pas un redemarrage. La conserver
/// demanderait de l'ecrire sur le telephone, et une preference de plus a
/// nettoyer le jour ou la disposition changera.
class BasculeFlottante extends StatefulWidget {
  const BasculeFlottante({
    required this.enLigne,
    required this.aPortee,
    required this.occupe,
    required this.actif,
    required this.onChanged,
    this.margeHaute = 0,
    this.margeBasse = 0,
    super.key,
  });

  /// L'INTENTION : la bascule est-elle mise sur « en ligne » ?
  final bool enLigne;

  /// LE FAIT : le serveur a-t-il une position recente de ce livreur ?
  ///
  /// LES DEUX ETAIENT CONFONDUS, ET C'ETAIT LE PIRE DEFAUT DE L'APPLICATION.
  /// Un telephone en poche cesse d'emettre ; au bout de deux minutes le
  /// serveur n'offre plus rien a ce livreur — et l'ecran continuait d'afficher
  /// un point vert et le mot « En ligne ». Il attendait des courses que
  /// personne ne lui envoyait plus.
  final bool aPortee;

  final bool occupe;

  /// Faux tant que le dossier n'est pas valide : la bascule reste visible,
  /// mais grisee. La retirer laisserait le livreur sans explication.
  final bool actif;

  final ValueChanged<bool> onChanged;

  /// Zones interdites, en haut (la salutation) et en bas (les cartes empilees
  /// et la mention Google).
  final double margeHaute;
  final double margeBasse;

  @override
  State<BasculeFlottante> createState() => _BasculeFlottanteState();
}

class _BasculeFlottanteState extends State<BasculeFlottante> {
  /// Coin superieur gauche. Nul tant que le livreur ne l'a pas deplacee : la
  /// position par defaut depend de la taille de l'ecran, qu'on ne connait
  /// qu'au premier calcul de mise en page.
  Offset? _coin;

  bool _replie = false;

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, contraintes) {
        // LES BORNES SE CALCULENT SUR LA LARGEUR DEPLIEE, meme repliee : sinon
        // une pastille poussee contre le bord droit se retrouverait a moitie
        // hors de l'ecran au moment de se deplier. Quelques pixels perdus
        // valent mieux qu'un bloc coupe.
        const gauche = _Mesures.marge;
        final droite = contraintes.maxWidth - _Mesures.largeur - _Mesures.marge;
        final haut = widget.margeHaute + _Mesures.marge;
        final bas = contraintes.maxHeight -
            _Mesures.hauteur -
            widget.margeBasse -
            _Mesures.marge;

        // ECRAN TRES ETROIT, OU MARGES TROP GRANDES : si les bornes se
        // croisent, on s'en tient au coin haut-gauche. clamp() leve quand la
        // borne basse depasse la haute, et une exception de mise en page ici
        // emporterait tout l'ecran d'accueil.
        final xMax = math.max(gauche, droite);
        final yMax = math.max(haut, bas);

        final vise = _coin ?? Offset(gauche, yMax);
        final place = Offset(
          vise.dx.clamp(gauche, xMax),
          vise.dy.clamp(haut, yMax),
        );

        return Stack(
          children: [
            Positioned(
              left: place.dx,
              top: place.dy,
              child: GestureDetector(
                // LE GLISSEMENT EST PRIS ICI, DONC LA CARTE NE LE VOIT PAS. Un
                // panneau pose sur une carte qui laisserait passer le doigt
                // ferait defiler la carte en croyant deplacer le panneau.
                //
                // LA BASCULE RESTE MAITRESSE DE SON PROPRE GESTE : un Switch se
                // fait glisser lateralement pour changer d'etat, et il est
                // enfant de ce detecteur, donc il l'emporte sur sa surface. On
                // deplace le panneau en tirant ailleurs que sur le bouton.
                //
                // ON REPART DE « place », PAS DE « _coin » : apres un
                // recadrage, l'ancien coin peut etre hors des bornes, et
                // ajouter le deplacement a une valeur deja rejetee ferait
                // avancer le doigt sans que rien ne bouge.
                onPanUpdate: (details) =>
                    setState(() => _coin = place + details.delta),
                child: _Panneau(
                  enLigne: widget.enLigne,
                  aPortee: widget.aPortee,
                  occupe: widget.occupe,
                  actif: widget.actif,
                  replie: _replie,
                  onPlier: () => setState(() => _replie = !_replie),
                  onChanged: widget.onChanged,
                ),
              ),
            ),
          ],
        );
      },
    );
  }
}

class _Panneau extends StatelessWidget {
  const _Panneau({
    required this.enLigne,
    required this.aPortee,
    required this.occupe,
    required this.actif,
    required this.replie,
    required this.onPlier,
    required this.onChanged,
  });

  final bool enLigne;
  final bool aPortee;
  final bool occupe;
  final bool actif;
  final bool replie;
  final VoidCallback onPlier;
  final ValueChanged<bool> onChanged;

  /// TROIS ETATS, PAS DEUX.
  ///
  /// « Hors de portee » n'est pas une nuance de « en ligne » : c'est l'etat ou
  /// le livreur croit travailler et ou le systeme ne le voit plus. Il merite
  /// donc sa couleur et son mot a lui — l'orange d'avertissement, et une
  /// phrase qui dit ce qu'il faut faire, pas ce qui s'est passe.
  (String, Color) get _etat {
    if (!enLigne) return ('Hors ligne', HbaColors.inkMuted);
    if (!aPortee) return ('Hors de portée', HbaColors.warning);
    return ('En ligne', HbaColors.success);
  }

  @override
  Widget build(BuildContext context) {
    final (libelle, couleur) = _etat;

    return AnimatedContainer(
      duration: HbaDuration.normal,
      curve: Curves.easeOutCubic,
      width: replie ? _Mesures.largeurRepliee : _Mesures.largeur,
      height: _Mesures.hauteur,
      decoration: BoxDecoration(
        color: HbaColors.surface,
        borderRadius: BorderRadius.circular(_Mesures.hauteur / 2),

        // UNE OMBRE PORTEE, PAS LE RELIEF DU SYSTEME. Meme raison que la carte
        // flottante de cet ecran : le relief neumorphique suppose de connaitre
        // la couleur de ce qu'il y a dessous, et sa lumiere claire vire a la
        // trainee sale sur une photo aerienne.
        boxShadow: const [
          BoxShadow(color: Color(0x1F141B2D), blurRadius: 16, offset: Offset(0, 5)),
        ],
      ),

      // LE CONTENU RESTE CELUI DU PANNEAU DEPLIE, simplement rogne pendant que
      // la largeur s'anime. Reconstruire deux arrangements differents ferait
      // sauter le libelle d'un cote a l'autre au milieu de la transition ; le
      // rognage le fait glisser dehors.
      //
      // L'ORDRE DES ELEMENTS EST DICTE PAR LE REPLI : ce qui doit survivre est
      // a gauche. La poignee d'abord, pour pouvoir redeployer ; le point
      // d'etat ensuite, parce que c'est la seule information qui compte quand
      // on ne regarde pas. Le libelle et la bascule sortent du cadre.
      //
      // ClipRRect ROGNE AUSSI LE TOUCHER : la bascule sortie du cadre n'est
      // plus atteignable, ce qui evite qu'un doigt tombe sur un bouton
      // invisible.
      child: ClipRRect(
        borderRadius: BorderRadius.circular(_Mesures.hauteur / 2),
        child: OverflowBox(
          alignment: Alignment.centerLeft,
          minWidth: _Mesures.largeur,
          maxWidth: _Mesures.largeur,
          child: Row(
            children: [
              _Poignee(replie: replie, onPlier: onPlier),

              // Le point d'etat : sur un ecran en plein soleil, il se lit avant
              // les mots, et c'est le seul repere qui survit au repli.
              Container(
                width: 10,
                height: 10,
                decoration: BoxDecoration(shape: BoxShape.circle, color: couleur),
              ),
              const SizedBox(width: HbaSpacing.sm),

              Expanded(
                child: Text(
                  libelle,
                  maxLines: 1,
                  softWrap: false,
                  overflow: TextOverflow.clip,
                  style: const TextStyle(
                    fontSize: 15,
                    fontWeight: FontWeight.w700,
                    color: HbaColors.ink,
                  ),
                ),
              ),

              if (occupe)
                const SizedBox(
                  width: 56,
                  height: _Mesures.hauteur,
                  child: Center(
                    child: SizedBox(
                      height: 20,
                      width: 20,
                      child: CircularProgressIndicator(strokeWidth: 2.4),
                    ),
                  ),
                )
              else
                // LA CIBLE TACTILE EST REMISE A LA MAIN. « shrinkWrap » retire
                // les 48 dp que Material ajoute autour d'un Switch — c'est ce
                // qui permet de tenir dans 44 dp de haut — et la boite
                // ci-dessous rend au doigt 56 x 44, au-dessus du minimum de
                // 44 dp admis pour un element dense.
                //
                // FittedBox PLUTOT QUE Transform.scale : une mise a l'echelle
                // par transformation ne change pas la taille MESUREE du
                // bouton. Si une version de Material dessinait un Switch un
                // pixel plus large, la rangee deborderait — et un debordement
                // dans Flutter n'est pas une gene visuelle, c'est un bandeau
                // raye en travers de l'ecran. FittedBox, lui, reduit pour
                // tenir, et ne fait rien quand ce n'est pas necessaire.
                SizedBox(
                  width: 56,
                  height: _Mesures.hauteur,
                  child: FittedBox(
                    fit: BoxFit.scaleDown,
                    child: Switch(
                      value: enLigne,
                      onChanged: actif ? onChanged : null,
                      materialTapTargetSize: MaterialTapTargetSize.shrinkWrap,
                    ),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

/// La poignee : elle replie et deplie, et c'est aussi par elle qu'on tire.
class _Poignee extends StatelessWidget {
  const _Poignee({required this.replie, required this.onPlier});

  final bool replie;
  final VoidCallback onPlier;

  @override
  Widget build(BuildContext context) => Semantics(
        button: true,
        label: replie ? 'Deplier la bascule' : 'Replier la bascule',
        child: GestureDetector(
          onTap: onPlier,
          behavior: HitTestBehavior.opaque,
          child: SizedBox(
            width: 40,
            height: _Mesures.hauteur,
            child: Center(
              child: AnimatedRotation(
                duration: HbaDuration.normal,
                curve: Curves.easeOutCubic,

                // UN DEMI-TOUR PLUTOT QUE DEUX ICONES. Echanger chevron_left
                // et chevron_right ferait clignoter le dessin au milieu de
                // l'animation de largeur ; la meme fleche qui pivote suit le
                // mouvement du panneau.
                turns: replie ? 0.5 : 0,
                child: const Icon(
                  Icons.chevron_left,
                  size: 22,
                  color: HbaColors.inkMuted,
                ),
              ),
            ),
          ),
        ),
      );
}
