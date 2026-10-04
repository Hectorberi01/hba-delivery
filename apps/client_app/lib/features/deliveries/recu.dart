import 'package:flutter/material.dart';
import 'package:hba_ui/hba_ui.dart';

import 'models.dart';

/// Le reçu d'une course terminée.
///
/// POURQUOI IL EXISTE. Le reçu partait par courriel (point 29) et nulle part
/// ailleurs : un client sans adresse enregistrée — le cas courant, l'adresse
/// est facultative dans ce produit — ne recevait donc rien, et personne ne
/// pouvait relire ce qu'une course avait coûté. L'application détenait déjà le
/// total ; il lui manquait le détail, que la passerelle rend depuis le
/// 2 octobre 2026.
///
/// IL NE REFAIT AUCUN CALCUL. Le total est celui du service, arrondi à la
/// dizaine par lui : additionner les lignes ici afficherait tôt ou tard un
/// franc d'écart avec ce qui a été prélevé. Les lignes expliquent le montant,
/// elles ne le produisent pas.
///
/// CE N'EST PAS UNE FACTURE NORMALISEE. La facture au sens de la DGI —
/// e-MECeF, numéro de série, QR — est le point 13 des points à trancher et
/// n'est pas tranchée. Ce document dit ce qui a été payé, et le dit comme tel.
class RecuDeCourse extends StatelessWidget {
  const RecuDeCourse({required this.livraison, super.key});

  final Delivery livraison;

  static Future<void> ouvrir(BuildContext context, Delivery livraison) {
    return showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => RecuDeCourse(livraison: livraison),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final detail = livraison.detailDuPrix;
    final remise = livraison.status == DeliveryStatus.delivered;

    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(
          HbaSpacing.gutter,
          0,
          HbaSpacing.gutter,
          HbaSpacing.xl,
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Reçu', style: theme.textTheme.titleLarge),
            const SizedBox(height: HbaSpacing.xs),
            Text(
              livraison.reference.isEmpty ? livraison.id : livraison.reference,
              style: theme.textTheme.bodySmall,
            ),

            const SizedBox(height: HbaSpacing.lg),

            Text(
              Xof.formatLong(livraison.totalXof),
              style: theme.textTheme.headlineMedium,
            ),
            const SizedBox(height: HbaSpacing.xs),
            Text(
              remise
                  ? 'Payé et livré.'
                  // UNE COURSE PAYEE PUIS ANNULEE A QUAND MEME UN RECU, et il
                  // doit dire ce qui s'est passe : c'est precisement celui-la
                  // qu'on relit six semaines plus tard.
                  : 'Payé. ${livraison.closureReason ?? 'Course close sans remise.'}',
              style: theme.textTheme.bodyMedium,
            ),

            if (detail != null) ...[
              const SizedBox(height: HbaSpacing.lg),
              const Divider(height: 1),
              const SizedBox(height: HbaSpacing.md),

              _Ligne(libelle: 'Prise en charge', montant: detail.priseEnChargeXof),
              _Ligne(libelle: 'Distance', montant: detail.distanceXof),
              if (detail.aUneMajoration)
                _Ligne(libelle: 'Majoration', montant: detail.majorationXof),

              const SizedBox(height: HbaSpacing.sm),
              Text(
                '${(detail.metres / 1000).toStringAsFixed(1)} km · '
                '${(detail.secondes / 60).round()} min estimées',
                style: theme.textTheme.bodySmall,
              ),
            ],

            const SizedBox(height: HbaSpacing.lg),
            const Divider(height: 1),
            const SizedBox(height: HbaSpacing.md),

            _Date(libelle: 'Commandée', quand: livraison.createdAt),
            _Date(libelle: 'Payée', quand: livraison.paidAt),
            if (livraison.completedAt != null)
              _Date(libelle: 'Livrée', quand: livraison.completedAt),

            if (livraison.estRemboursee) ...[
              const SizedBox(height: HbaSpacing.md),
              Text(
                // LE FOURNISSEUR NE DIT PAS COMBIEN quand le remboursement est
                // partiel : on ne peut donc annoncer aucun montant.
                livraison.refundPartial
                    ? 'Un remboursement partiel a été constaté.'
                    : 'Le montant vous a été remboursé.',
                style: theme.textTheme.bodyMedium,
              ),
            ],

            const SizedBox(height: HbaSpacing.lg),
            SizedBox(
              width: double.infinity,
              child: HbaButton(
                label: 'Fermer',
                tone: HbaButtonTone.neutral,
                onPressed: () => Navigator.of(context).pop(),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Ligne extends StatelessWidget {
  const _Ligne({required this.libelle, required this.montant});

  final String libelle;
  final int montant;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: HbaSpacing.xs),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(libelle, style: theme.textTheme.bodyMedium),
          Text(Xof.format(montant), style: theme.textTheme.bodyMedium),
        ],
      ),
    );
  }
}

class _Date extends StatelessWidget {
  const _Date({required this.libelle, required this.quand});

  final String libelle;
  final DateTime? quand;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: HbaSpacing.xs),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(libelle, style: theme.textTheme.bodySmall),
          Text(
            quand == null ? '—' : _lisible(quand!),
            style: theme.textTheme.bodySmall,
          ),
        ],
      ),
    );
  }

  /// LE JOUR ET L'HEURE, SANS PAQUET DE LOCALISATION. « intl » n'est pas une
  /// dependance de cette application, et un recu n'a pas besoin d'un format
  /// cultive : deux chiffres, un mois en lettres, l'heure.
  static String _lisible(DateTime quand) {
    const mois = [
      'janv.', 'févr.', 'mars', 'avr.', 'mai', 'juin',
      'juil.', 'août', 'sept.', 'oct.', 'nov.', 'déc.',
    ];

    final h = quand.hour.toString().padLeft(2, '0');
    final m = quand.minute.toString().padLeft(2, '0');

    return '${quand.day} ${mois[quand.month - 1]} ${quand.year}, $h:$m';
  }
}
