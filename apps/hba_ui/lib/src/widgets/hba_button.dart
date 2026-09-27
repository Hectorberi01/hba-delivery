import 'package:flutter/material.dart';

import '../tokens.dart';

enum HbaButtonTone { primary, neutral, danger }

/// Bouton pleine largeur des ecrans d'action.
///
/// L'ACTION PRINCIPALE RESTE UN APLAT DE COULEUR, ET C'EST LE DEFAUT CONNU DU
/// NEUMORPHISME QU'ON EVITE ICI. Dans un ecran ou tout est en relief doux,
/// plus rien ne se distingue : le bouton principal se noie parmi les cartes,
/// et l'ecran perd son action evidente. Il garde donc sa couleur pleine ; le
/// relief est reserve au secondaire.
///
/// LE BLANC EST SUR #C4550C, PAS SUR L'ORANGE DE MARQUE. Sur #F2690F il ne
/// tenait que 3,09:1 pour un libelle de 16 px gras — sous le seuil de 4,5:1 et
/// sous celui du « grand texte », qui commence a 18,66 px. En interieur, cela
/// paraissait lisible ; c'est en plein soleil que le defaut se paie.
///
/// LA PRESSION EST UN MOUVEMENT, PAS UN CHANGEMENT DE COULEUR. Le bouton
/// s'enfonce : son ombre disparait et il descend d'un pixel. C'est ce geste
/// qui repond au reproche fait au neumorphisme — on ne saurait plus ce qui est
/// cliquable — et il coute moins cher qu'un effet d'encre Material, qui de
/// toute facon se verrait mal sur une surface deja texturee par les ombres.
///
/// POURQUOI PAS FilledButton. Le widget Material apporterait ses etats et sa
/// semantique gratuitement, mais son effet de pression est une nappe d'encre
/// circulaire, etrangere a ce style, et il ne sait pas s'enfoncer. Les trois
/// tons partagent donc une seule implementation — au prix d'une semantique
/// posee a la main, juste en dessous.
class HbaButton extends StatefulWidget {
  const HbaButton({
    required this.label,
    required this.onPressed,
    this.tone = HbaButtonTone.primary,
    this.icon,
    this.busy = false,
    super.key,
  });

  final String label;
  final VoidCallback? onPressed;
  final HbaButtonTone tone;
  final IconData? icon;
  final bool busy;

  @override
  State<HbaButton> createState() => _HbaButtonState();
}

class _HbaButtonState extends State<HbaButton> {
  bool _presse = false;

  @override
  Widget build(BuildContext context) {
    final actif = widget.onPressed != null && !widget.busy;

    final (fond, encre, enRelief) = switch (widget.tone) {
      HbaButtonTone.primary => (HbaColors.primaryDeep, Colors.white, false),
      HbaButtonTone.neutral => (HbaColors.surface, HbaColors.ink, true),
      HbaButtonTone.danger => (HbaColors.danger, Colors.white, false),
    };

    // UN BOUTON DESACTIVE S'EFFACE SANS DISPARAITRE. Material dispense les
    // elements desactives du seuil de contraste — precisement pour qu'ils se
    // lisent comme indisponibles — mais un bouton qu'on ne voit plus du tout
    // laisse croire que l'ecran est casse.
    final fondRendu = actif ? fond : _attenuer(fond);
    final encreRendue = actif ? encre : _attenuer(encre);

    final ombres = !actif || _presse
        ? const <BoxShadow>[]
        : enRelief
            ? HbaOmbres.relief(HbaElevation.normale)
            : const [
                // L'APLAT DE COULEUR PORTE UNE OMBRE PLUS FRANCHE que les
                // surfaces claires : sur une couleur saturee, la lumiere en
                // haut a gauche ne se verrait pas, et une ombre douce non
                // plus. Une seule ombre, un peu plus dense, suffit a le poser.
                BoxShadow(
                  color: HbaColors.ombre,
                  offset: Offset(0, 6),
                  blurRadius: 14,
                ),
              ];

    return Semantics(
      button: true,
      enabled: actif,
      label: widget.label,
      child: GestureDetector(
        onTapDown: actif ? (_) => setState(() => _presse = true) : null,
        onTapUp: actif ? (_) => setState(() => _presse = false) : null,
        onTapCancel: actif ? () => setState(() => _presse = false) : null,
        onTap: actif ? widget.onPressed : null,
        behavior: HitTestBehavior.opaque,
        child: AnimatedContainer(
          duration: HbaDuration.fast,
          curve: Curves.easeOut,
          height: 56,
          width: double.infinity,

          // LE DEPLACEMENT EST D'UN SEUL PIXEL. Deux se voient comme un
          // defaut d'alignement sur une liste de boutons ; un se ressent
          // sans se remarquer.
          transform: Matrix4.translationValues(0, _presse ? 1 : 0, 0),
          decoration: BoxDecoration(
            color: fondRendu,
            borderRadius: BorderRadius.circular(HbaRadius.button),
            boxShadow: ombres,
          ),
          alignment: Alignment.center,
          child: widget.busy
              ? SizedBox(
                  height: 22,
                  width: 22,
                  child: CircularProgressIndicator(
                    strokeWidth: 2.4,
                    valueColor: AlwaysStoppedAnimation(encreRendue),
                  ),
                )
              : Row(
                  mainAxisAlignment: MainAxisAlignment.center,
                  children: [
                    if (widget.icon != null) ...[
                      Icon(widget.icon, size: 20, color: encreRendue),
                      const SizedBox(width: HbaSpacing.sm),
                    ],
                    Text(
                      widget.label,
                      style: TextStyle(
                        color: encreRendue,
                        fontSize: 16,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                  ],
                ),
        ),
      ),
    );
  }

  /// Rapproche une couleur du fond, sans la rendre transparente : une couleur
  /// translucide laisserait passer l'ombre du bloc qui se trouve dessous.
  static Color _attenuer(Color couleur) =>
      Color.lerp(couleur, HbaColors.background, 0.62) ?? couleur;
}
