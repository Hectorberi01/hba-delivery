import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../tokens.dart';

/// Une entree de [HbaArcNav].
class HbaArcItem {
  const HbaArcItem({required this.icon, required this.label});

  final IconData icon;
  final String label;
}

/// Navigation en ARC DE CERCLE, REPLIEE EN UN SEUL BOUTON.
///
/// POURQUOI ELLE SE REPLIE, ET CE QUE CELA A CORRIGE. La premiere version
/// laissait les quatre onglets en permanence sur l'ecran. Sur une carte, les
/// libelles se posaient sur les noms de villes — « Courses » par-dessus
/// « Noisy-le-Grand » — et le dernier onglet chevauchait la carte de course en
/// bas. Une navigation qui rend le contenu illisible coute plus qu'elle ne
/// rapporte, et c'est la carte qui est le contenu de cet ecran.
///
/// LE BOUTON REPLIE EST L'ONGLET ACTIF, pas une icone de menu. Le client sait
/// donc ou il se trouve sans rien ouvrir, et l'ouverture ne deplace rien : les
/// trois autres se deploient AUTOUR de lui, a leur place sur l'arc. Rien ne
/// saute, parce que rien ne bouge.
///
/// TROIS PRECAUTIONS RENDENT CETTE FORME TENABLE :
///
/// 1. L'ARC VIT DANS LA MOITIE BASSE DE L'ECRAN. Un arc centre verticalement
///    placerait son premier onglet hors d'atteinte du pouce sur un grand
///    telephone. Ici il est ancre vers le bas, la ou la main tient l'appareil.
/// 2. CHAQUE CIBLE FAIT 48 dp DE HAUT, quelle que soit la taille dessinee.
/// 3. LES ONGLETS INACTIFS NE S'ESTOMPENT PAS. Le modele dont vient cette
///    forme etait une ROUE : ce qui s'eloignait du centre palissait, parce que
///    c'etait plus loin dans une liste qu'on fait defiler. Une navigation n'a
///    pas de liste : les quatre onglets sont egalement disponibles, et en
///    ternir deux dirait le contraire.
class HbaArcNav extends StatefulWidget {
  const HbaArcNav({
    required this.items,
    required this.index,
    required this.onChange,
    super.key,
  });

  final List<HbaArcItem> items;
  final int index;
  final ValueChanged<int> onChange;

  @override
  State<HbaArcNav> createState() => _HbaArcNavState();
}

class _HbaArcNavState extends State<HbaArcNav>
    with SingleTickerProviderStateMixin {
  static const _cible = HbaSpacing.cible;
  static const _rond = 44.0;
  static const _rondActif = 56.0;

  /// De combien l'onglet du milieu deborde vers la gauche par rapport a ceux
  /// des extremites. C'est ce seul nombre qui donne sa courbure a l'arc.
  static const _fleche = 30.0;

  static const _margeDroite = HbaSpacing.md;

  late final AnimationController _deploiement = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 280),
    reverseDuration: const Duration(milliseconds: 180),
  );

  bool _ouvert = false;

  @override
  void didUpdateWidget(covariant HbaArcNav ancien) {
    super.didUpdateWidget(ancien);

    // L'ONGLET A CHANGE SANS PASSER PAR ICI — une navigation programmee, un
    // retour en arriere. Le menu se referme : le laisser ouvert par-dessus un
    // ecran qu'on vient d'atteindre n'aurait aucun sens.
    if (ancien.index != widget.index && _ouvert) _replier();
  }

  @override
  void dispose() {
    _deploiement.dispose();
    super.dispose();
  }

  void _deplier() {
    setState(() => _ouvert = true);
    _deploiement.forward();
  }

  void _replier() {
    _deploiement.reverse();
    if (mounted) setState(() => _ouvert = false);
  }

  void _choisir(int i) {
    _replier();
    // L'ONGLET DEJA OUVERT NE SE RENOTIFIE PAS. Le repli est la reponse
    // attendue quand on retape celui sur lequel on est.
    if (i != widget.index) widget.onChange(i);
  }

  @override
  Widget build(BuildContext context) {
    if (widget.items.isEmpty) return const SizedBox.shrink();

    final marges = MediaQuery.paddingOf(context);

    return LayoutBuilder(
      builder: (context, contraintes) {
        final haut = marges.top;
        final hauteur = contraintes.maxHeight - haut - marges.bottom;

        final pasVoulu = _cible + HbaSpacing.md;
        final pas = math.min(pasVoulu, (hauteur * 0.62) / widget.items.length);
        final etendue = pas * (widget.items.length - 1);

        // Ancre vers le bas : voir la precaution 1 en tete de classe.
        final centre = haut + hauteur * 0.62;

        final demi = etendue / 2;
        final rayon = demi <= 0
            ? 0.0
            : (_fleche * _fleche + demi * demi) / (2 * _fleche);

        double decalage(double dy) {
          if (rayon <= 0) return 0;
          final reste = rayon * rayon - dy * dy;
          if (reste <= 0) return 0;
          return _fleche - (rayon - math.sqrt(reste));
        }

        return AnimatedBuilder(
          animation: _deploiement,
          builder: (context, _) {
            final ouverture = _deploiement.value;

            return Stack(
              clipBehavior: Clip.none,
              children: [
                // LE VOILE N'EXISTE QUE MENU OUVERT, et c'est ce qui laisse la
                // carte se manipuler le reste du temps : sans enfant pleine
                // taille, cette pile ne capte aucun doigt et tout passe au
                // travers.
                if (ouverture > 0)
                  Positioned.fill(
                    child: IgnorePointer(
                      ignoring: !_ouvert,
                      child: GestureDetector(
                        onTap: _replier,
                        child: ColoredBox(
                          // LE VOILE REND AUSSI LES LIBELLES LISIBLES. Poses a
                          // nu sur une carte, ils se melaient aux noms de
                          // villes ; sur un fond assombri, ils se detachent.
                          color: HbaColors.ink.withValues(
                            alpha: 0.38 * ouverture,
                          ),
                        ),
                      ),
                    ),
                  ),

                if (rayon > 0 && ouverture > 0)
                  Positioned.fill(
                    child: IgnorePointer(
                      child: Opacity(
                        opacity: ouverture,
                        child: CustomPaint(
                          painter: _TraitDArc(
                            centre: centre,
                            demi: demi,
                            rayon: rayon,
                            margeDroite: _margeDroite + _rond / 2,
                          ),
                        ),
                      ),
                    ),
                  ),

                for (var i = 0; i < widget.items.length; i++)
                  _onglet(context, i, centre, pas, etendue, decalage, ouverture),
              ],
            );
          },
        );
      },
    );
  }

  Widget _onglet(
    BuildContext context,
    int i,
    double centre,
    double pas,
    double etendue,
    double Function(double) decalage,
    double ouverture,
  ) {
    final actif = i == widget.index;
    final theme = Theme.of(context);

    // L'ACTIF EST A SA PLACE DES LE DEPART : c'est lui, le bouton replie. Les
    // autres partent de cette meme place et rejoignent la leur.
    final dyActif = widget.index * pas - etendue / 2;
    final dyPropre = i * pas - etendue / 2;
    final dy = actif ? dyPropre : dyActif + (dyPropre - dyActif) * ouverture;

    final droite = _margeDroite + decalage(dy);
    final taille = actif ? _rondActif : _rond;

    // Les inactifs n'existent pas tant que rien n'est deploye : ni a l'ecran,
    // ni pour le doigt, ni pour un lecteur d'ecran.
    if (!actif && ouverture <= 0) return const SizedBox.shrink();

    return Positioned(
      right: droite,
      top: centre + dy - _cible / 2,
      child: Opacity(
        opacity: actif ? 1 : ouverture,
        child: Semantics(
          button: true,
          selected: actif,
          label: actif && !_ouvert
              ? 'Menu. Onglet actif : ${widget.items[i].label}'
              : widget.items[i].label,
          // PENDANT LA FERMETURE, LES INACTIFS NE RECOIVENT PLUS RIEN. Ils
          // restent visibles le temps de s'effacer ; un doigt qui en touche un
          // a cet instant ne doit ni naviguer ni rouvrir le menu.
          child: IgnorePointer(
            ignoring: !actif && !_ouvert,
            child: GestureDetector(
              // OPAQUE : sans cela, l'espace entre le libelle et la pastille
              // ne recoit pas le doigt, et le client touche « a cote » d'un
              // onglet qu'il vise pourtant.
              behavior: HitTestBehavior.opaque,
              onTap: () {
                if (!_ouvert) {
                  _deplier();
                } else {
                  _choisir(i);
                }
              },
              child: ExcludeSemantics(
                child: SizedBox(
                  height: _cible,
                  child: Row(
                  mainAxisSize: MainAxisSize.min,
                  mainAxisAlignment: MainAxisAlignment.end,
                  children: [
                    // LE LIBELLE N'APPARAIT QU'OUVERT. Replie, le bouton est
                    // une pastille seule : un mot pose sur la carte serait
                    // illisible, et c'est le defaut qu'on corrige.
                    if (ouverture > 0) ...[
                      Text(
                        widget.items[i].label,
                        style: theme.textTheme.bodyMedium?.copyWith(
                          color: HbaColors.lumiere,
                          fontWeight: actif ? FontWeight.w600 : FontWeight.w400,
                        ),
                      ),
                      const SizedBox(width: HbaSpacing.sm),
                    ],
                    AnimatedContainer(
                      duration: HbaDuration.fast,
                      curve: Curves.easeOut,
                      height: taille,
                      width: taille,
                      decoration: BoxDecoration(
                        color: actif ? HbaColors.primary : HbaColors.surface,
                        shape: BoxShape.circle,
                        boxShadow: HbaOmbres.relief(
                          actif ? HbaElevation.haute : HbaElevation.douce,
                        ),
                      ),
                      child: Icon(
                        widget.items[i].icon,
                        size: actif ? 26 : 20,
                        color: actif ? HbaColors.lumiere : HbaColors.inkMuted,
                      ),
                    ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// Le trait fin qui passe derriere les pastilles et donne sa forme a l'arc.
class _TraitDArc extends CustomPainter {
  const _TraitDArc({
    required this.centre,
    required this.demi,
    required this.rayon,
    required this.margeDroite,
  });

  final double centre;
  final double demi;
  final double rayon;
  final double margeDroite;

  @override
  void paint(Canvas canvas, Size size) {
    if (demi <= 0 || rayon <= 0) return;

    // LE CENTRE DU CERCLE EST LOIN A DROITE, HORS DE L'ECRAN. C'est ce qui
    // rend la courbe douce : un cercle vu de tres pres est presque droit.
    final cx = size.width - margeDroite + rayon;

    final chemin = Path();
    const pas = 4.0;
    for (var dy = -demi; dy <= demi; dy += pas) {
      final reste = rayon * rayon - dy * dy;
      if (reste <= 0) continue;
      final x = cx - math.sqrt(reste);
      final y = centre + dy;
      if (dy == -demi) {
        chemin.moveTo(x, y);
      } else {
        chemin.lineTo(x, y);
      }
    }

    canvas.drawPath(
      chemin,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 1
        ..strokeCap = StrokeCap.round
        ..color = HbaColors.lumiere.withValues(alpha: 0.35),
    );
  }

  @override
  bool shouldRepaint(_TraitDArc ancien) =>
      ancien.centre != centre ||
      ancien.demi != demi ||
      ancien.rayon != rayon ||
      ancien.margeDroite != margeDroite;
}
