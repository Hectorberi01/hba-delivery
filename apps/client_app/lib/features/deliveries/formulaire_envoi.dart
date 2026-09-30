import 'package:flutter/material.dart';
import 'package:hba_ui/hba_ui.dart';

/// Les briques visuelles du formulaire d'envoi.
///
/// SEPAREES DE L'ECRAN PARCE QU'ELLES N'ONT AUCUN ETAT. L'ecran porte les
/// controleurs, les erreurs et le devis ; ces trois widgets ne portent que la
/// mise en page, et les sortir rend le build de l'ecran lisible d'un coup
/// d'oeil — ce qu'il n'etait plus.

/// Un bloc du formulaire : icone, titre en capitales, contenu.
class SectionEnvoi extends StatelessWidget {
  const SectionEnvoi({
    required this.icone,
    required this.titre,
    required this.enfants,
    super.key,
  });

  final IconData icone;
  final String titre;
  final List<Widget> enfants;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Icon(icone, size: 20, color: HbaColors.primary),
              const SizedBox(width: HbaSpacing.sm),
              Text(
                titre.toUpperCase(),
                style: theme.textTheme.titleSmall?.copyWith(
                  letterSpacing: 0.6,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ],
          ),
          const SizedBox(height: HbaSpacing.md),
          ...enfants,
        ],
      ),
    );
  }
}

/// Un champ avec son intitule au-dessus, en petites capitales.
///
/// L'INTITULE EST AU-DESSUS, PAS DANS LE CHAMP. Un texte d'aide qui disparait
/// des la premiere frappe laisse un formulaire de six cases anonymes : le
/// client qui revient verifier ne sait plus laquelle contient quoi. C'est
/// surtout vrai ici, ou deux paires « nom + telephone » se ressemblent.
class ChampEnvoi extends StatelessWidget {
  const ChampEnvoi({
    required this.intitule,
    required this.enfant,
    super.key,
  });

  final String intitule;
  final Widget enfant;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          intitule.toUpperCase(),
          style: theme.textTheme.bodySmall?.copyWith(
            color: HbaColors.inkFaint,
            letterSpacing: 0.6,
            fontWeight: FontWeight.w600,
          ),
        ),
        const SizedBox(height: HbaSpacing.xs),
        enfant,
      ],
    );
  }
}

/// Le trait pointille qui relie la collecte a la livraison.
///
/// DESSINE AUTOUR DES DEUX CARTES, PAS ENTRE ELLES. Le peintre prend la taille
/// de son enfant : en l'enroulant autour du bloc « collecte + livraison », les
/// deux extremites du trait tombent d'elles-memes au bon endroit, sans mesurer
/// quoi que ce soit ni supposer une hauteur de carte. Une ligne posee a coup de
/// hauteurs fixes se serait decalee au premier champ qui passe sur deux lignes.
class Chronologie extends StatelessWidget {
  const Chronologie({required this.child, super.key});

  final Widget child;

  /// Largeur de la gouttiere reservee au trait.
  static const gouttiere = 26.0;

  @override
  Widget build(BuildContext context) => CustomPaint(
        painter: _TraitChronologie(),
        child: Padding(
          padding: const EdgeInsets.only(left: gouttiere),
          child: child,
        ),
      );
}

class _TraitChronologie extends CustomPainter {
  static const _x = 8.0;
  static const _marge = 34.0;
  static const _rayon = 5.0;

  @override
  void paint(Canvas canvas, Size size) {
    const haut = Offset(_x, _marge);
    final bas = Offset(_x, size.height - _marge);

    if (bas.dy <= haut.dy) return;

    final trait = Paint()
      ..color = HbaColors.primary.withValues(alpha: 0.45)
      ..strokeWidth = 1.6
      ..strokeCap = StrokeCap.round;

    // POINTILLE DESSINE A LA MAIN : Flutter n'a pas de pinceau pointille, et
    // les paquets qui en ajoutent un pour six lignes de code ne valent pas leur
    // mise a jour.
    const pas = 7.0;
    const plein = 3.5;
    for (var y = haut.dy + _rayon + 3; y < bas.dy - _rayon - 3; y += pas) {
      canvas.drawLine(Offset(_x, y), Offset(_x, (y + plein).clamp(0, bas.dy)), trait);
    }

    // DEPART CREUX, ARRIVEE PLEINE. Le colis part d'un endroit et se pose a
    // l'autre : deux ronds identiques ne diraient pas dans quel sens lire.
    canvas.drawCircle(haut, _rayon, Paint()
      ..color = HbaColors.primary
      ..style = PaintingStyle.stroke
      ..strokeWidth = 2);

    canvas.drawCircle(bas, _rayon, Paint()..color = HbaColors.primary);
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}
