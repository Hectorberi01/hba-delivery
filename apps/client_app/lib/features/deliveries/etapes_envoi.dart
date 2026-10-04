import 'package:flutter/material.dart';
import 'package:hba_ui/hba_ui.dart';

/// Une etape du formulaire d'envoi, telle que le bandeau l'annonce.
class EtapeDEnvoi {
  const EtapeDEnvoi({required this.titre, required this.sousTitre});

  final String titre;

  /// Ce que l'etape demande, en quatre mots. Il dit au client ce qui l'attend
  /// AVANT qu'il y arrive — c'est ce qui distingue un bandeau de progression
  /// d'une simple numerotation.
  final String sousTitre;
}

/// Le bandeau « Enlèvement → Livraison → Détails ».
///
/// POURQUOI UN BANDEAU ET PAS UN SIMPLE TITRE. Le formulaire a ete coupe en
/// trois le 4 octobre 2026 : sans reperage, couper un formulaire ne fait que
/// cacher ce qui reste a remplir, et le client abandonne faute de savoir
/// combien il en a encore. Trois pastilles disent la longueur totale d'un coup
/// d'oeil.
///
/// ON REVIENT EN ARRIERE EN APPUYANT SUR UNE ETAPE FAITE, jamais en avant. Une
/// etape plus loin n'est pas forcement remplissable — l'etape deux a besoin du
/// point d'enlevement — et un bandeau qui laisse sauter pour afficher ensuite
/// une erreur vaut moins qu'un bandeau qui ne laisse pas sauter.
class BandeauDEtapes extends StatelessWidget {
  const BandeauDEtapes({
    required this.etapes,
    required this.courante,
    required this.onRevenir,
    super.key,
  });

  final List<EtapeDEnvoi> etapes;

  /// L'index de l'etape affichee, a partir de zero.
  final int courante;

  /// Appele avec l'index d'une etape DEJA FAITE sur laquelle le client appuie.
  final void Function(int) onRevenir;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      children: [
        Row(
          children: [
            for (final (rang, _) in etapes.indexed)
              Expanded(
                child: _Rail(
                  faite: rang < courante,
                  courante: rang == courante,
                  aGauche: rang > 0,
                  aDroite: rang < etapes.length - 1,

                  // LE TRAIT DE GAUCHE EST VERT DES QUE L'ETAPE PRECEDENTE EST
                  // FAITE : c'est lui qui donne l'impression d'avancee, et il
                  // doit donc suivre l'etape d'avant, pas celle-ci.
                  gaucheFaite: rang <= courante,
                  droiteFaite: rang < courante,
                ),
              ),
          ],
        ),
        const SizedBox(height: HbaSpacing.sm),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            for (final (rang, etape) in etapes.indexed)
              Expanded(
                child: Semantics(
                  button: rang < courante,
                  label: 'Étape ${rang + 1} sur ${etapes.length} : '
                      '${etape.titre}, ${etape.sousTitre}',
                  child: GestureDetector(
                    behavior: HitTestBehavior.opaque,
                    onTap: rang < courante ? () => onRevenir(rang) : null,
                    child: Column(
                      children: [
                        Text(
                          etape.titre,
                          textAlign: TextAlign.center,
                          style: theme.textTheme.bodyMedium?.copyWith(
                            fontWeight: FontWeight.w700,
                            color: HbaColors.ink,
                          ),
                        ),
                        const SizedBox(height: 2),
                        Text(
                          etape.sousTitre,
                          textAlign: TextAlign.center,
                          style: theme.textTheme.bodySmall?.copyWith(
                            color: rang <= courante
                                ? HbaColors.primary
                                : HbaColors.inkMuted,
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
          ],
        ),
      ],
    );
  }
}

/// La pastille d'une etape et les deux moities de trait qui l'encadrent.
///
/// LE TRAIT EST COUPE EN DEUX MOITIES, UNE PAR CELLULE. Dessine d'un bout a
/// l'autre du bandeau, il passerait SOUS les pastilles et depasserait aux deux
/// extremites ; en deux moities portees par les cellules, chaque segment
/// s'arrete exactement ou commence la pastille suivante, quelle que soit la
/// largeur de l'ecran.
class _Rail extends StatelessWidget {
  const _Rail({
    required this.faite,
    required this.courante,
    required this.aGauche,
    required this.aDroite,
    required this.gaucheFaite,
    required this.droiteFaite,
  });

  final bool faite;
  final bool courante;
  final bool aGauche;
  final bool aDroite;
  final bool gaucheFaite;
  final bool droiteFaite;

  @override
  Widget build(BuildContext context) => SizedBox(
        height: 24,
        child: Stack(
          alignment: Alignment.center,
          children: [
            Row(
              children: [
                Expanded(child: _Trait(visible: aGauche, faite: gaucheFaite)),
                Expanded(child: _Trait(visible: aDroite, faite: droiteFaite)),
              ],
            ),
            _Pastille(faite: faite, courante: courante),
          ],
        ),
      );
}

class _Trait extends StatelessWidget {
  const _Trait({required this.visible, required this.faite});

  final bool visible;
  final bool faite;

  @override
  Widget build(BuildContext context) => visible
      ? Container(
          height: 3,
          color: faite ? HbaColors.primary : HbaColors.border,
        )
      : const SizedBox.shrink();
}

class _Pastille extends StatelessWidget {
  const _Pastille({required this.faite, required this.courante});

  final bool faite;
  final bool courante;

  @override
  Widget build(BuildContext context) {
    // L'ETAPE A VENIR EST UN DISQUE GRIS, PAS UN CERCLE VIDE. Un cercle vide se
    // confond avec un bouton radio non coche, a deux doigts d'une liste qui en
    // contient de vrais.
    final couleur =
        faite || courante ? HbaColors.primary : HbaColors.ombre;

    // L'ANNEAU BLANC EST DESSINE AUTOUR, PAS DEDANS. Une bordure de Container
    // se peint a l'interieur de la boite : elle rongerait le disque de trois
    // points de chaque cote au lieu de l'ecarter du trait qui passe derriere.
    return DecoratedBox(
      decoration: const BoxDecoration(
        color: HbaColors.surface,
        shape: BoxShape.circle,
      ),
      child: Padding(
        padding: const EdgeInsets.all(3),
        child: Container(
          height: courante ? 22 : 18,
          width: courante ? 22 : 18,
          decoration: BoxDecoration(color: couleur, shape: BoxShape.circle),
          alignment: Alignment.center,
          child: faite
              ? const Icon(Icons.check, size: 12, color: HbaColors.lumiere)
              : null,
        ),
      ),
    );
  }
}
