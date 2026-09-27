import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import 'delivery_providers.dart';
import 'models.dart';

/// Suivi d'une livraison.
class TrackingScreen extends ConsumerStatefulWidget {
  const TrackingScreen({required this.deliveryId, super.key});

  final String deliveryId;

  @override
  ConsumerState<TrackingScreen> createState() => _TrackingScreenState();
}

class _TrackingScreenState extends ConsumerState<TrackingScreen> {
  Timer? _poller;

  @override
  void initState() {
    super.initState();
    _poller = Timer.periodic(
      const Duration(seconds: 10),
      (_) => ref.invalidate(deliveryProvider(widget.deliveryId)),
    );
  }

  @override
  void dispose() {
    _poller?.cancel();
    super.dispose();
  }

  Future<void> _cancel(Delivery delivery) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Annuler la livraison ?'),
        // Aucun montant n'est annonce ici : la politique d'annulation n'est pas
        // tranchee, et promettre un remboursement que le service ne fera pas
        // serait pire que de ne rien dire.
        content: const Text(
          "Le livreur ne viendra pas. Les conditions de remboursement vous "
          "seront confirmees par le service.",
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(false),
            child: const Text('Garder'),
          ),
          TextButton(
            onPressed: () => Navigator.of(dialogContext).pop(true),
            child: const Text('Annuler la course'),
          ),
        ],
      ),
    );

    if (confirmed != true || !mounted) return;

    try {
      await ref
          .read(deliveryRepositoryProvider)
          .cancel(delivery.id, 'Annulee par le client');
      ref.invalidate(deliveryProvider(widget.deliveryId));
    } on ApiException catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text(error.message)));
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final async = ref.watch(deliveryProvider(widget.deliveryId));

    return Scaffold(
      appBar: AppBar(title: const Text('Suivi')),
      body: async.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (error, _) => Center(
          child: Padding(
            padding: const EdgeInsets.all(HbaSpacing.gutter),
            child: Text(
              error is ApiException ? error.message : 'Suivi indisponible.',
              textAlign: TextAlign.center,
              style: theme.textTheme.bodyMedium,
            ),
          ),
        ),
        data: (delivery) => ListView(
          padding: const EdgeInsets.all(HbaSpacing.gutter),
          children: [
            Row(
              children: [
                Expanded(
                  child: Text(delivery.reference,
                      style: theme.textTheme.headlineMedium),
                ),
                HbaChip(
                  label: delivery.status.label.toUpperCase(),
                  tone: switch (delivery.status) {
                    DeliveryStatus.delivered => HbaChipTone.success,
                    DeliveryStatus.cancelled ||
                    DeliveryStatus.failed ||
                    DeliveryStatus.noDriverFound ||
                    DeliveryStatus.paymentFailed =>
                      HbaChipTone.danger,
                    _ => HbaChipTone.primary,
                  },
                ),
              ],
            ),
            const SizedBox(height: HbaSpacing.lg),
            if (delivery.showsOtp) ...[
              _OtpCard(code: delivery.deliveryOtp),
              const SizedBox(height: HbaSpacing.md),
            ],
            if (delivery.driver != null) ...[
              _DriverCard(driver: delivery.driver!),
              const SizedBox(height: HbaSpacing.md),
            ],
            HbaCard(
              padding: const EdgeInsets.all(HbaSpacing.lg),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  _Line(label: 'Collecte', value: delivery.pickupLandmark),
                  const SizedBox(height: HbaSpacing.md),
                  _Line(label: 'Livraison', value: delivery.dropoffLandmark),
                  const SizedBox(height: HbaSpacing.md),
                  _Line(
                    label: 'Destinataire',
                    value: '${delivery.recipientName} — ${delivery.recipientPhone}',
                  ),
                  const SizedBox(height: HbaSpacing.md),
                  _Line(label: 'Prix', value: Xof.format(delivery.totalXof)),
                ],
              ),
            ),
            if (delivery.status.canCancel) ...[
              const SizedBox(height: HbaSpacing.lg),
              HbaButton(
                label: 'Annuler la livraison',
                tone: HbaButtonTone.danger,
                onPressed: () => _cancel(delivery),
              ),
            ],
            const SizedBox(height: HbaSpacing.lg),
          ],
        ),
      ),
    );
  }
}

/// Code de remise.
///
/// IL S'AFFICHE ICI, et seulement ici : c'est le client qui le transmet au
/// destinataire, par le moyen qu'il veut. Il disparait des que la course est
/// close.
class _OtpCard extends StatelessWidget {
  const _OtpCard({required this.code});

  final String code;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      highlighted: true,
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Code de remise', style: theme.textTheme.titleMedium),
          const SizedBox(height: HbaSpacing.xs),
          Text(
            'Communiquez-le au destinataire. Le livreur le lui demandera.',
            style: theme.textTheme.bodyMedium,
          ),
          const SizedBox(height: HbaSpacing.md),
          Row(
            children: [
              Expanded(
                child: Text(
                  code,
                  style: theme.textTheme.displaySmall?.copyWith(
                    color: HbaColors.primary,
                    letterSpacing: 8,
                  ),
                ),
              ),
              IconButton(
                onPressed: () async {
                  await Clipboard.setData(ClipboardData(text: code));
                  if (!context.mounted) return;
                  ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('Code copie.')),
                  );
                },
                icon: const Icon(Icons.copy_outlined),
                color: HbaColors.inkMuted,
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _DriverCard extends StatelessWidget {
  const _DriverCard({required this.driver});

  final AssignedDriver driver;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Row(
        children: [
          Container(
            height: 44,
            width: 44,
            decoration: BoxDecoration(
              color: HbaColors.primarySoft,
              borderRadius: BorderRadius.circular(14),
            ),
            child: const Icon(Icons.two_wheeler_outlined, color: HbaColors.primary),
          ),
          const SizedBox(width: HbaSpacing.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(driver.displayName, style: theme.textTheme.titleMedium),
                if (driver.vehiclePlate.isNotEmpty)
                  Text(driver.vehiclePlate, style: theme.textTheme.bodyMedium),
              ],
            ),
          ),
          if (driver.phone.isNotEmpty)
            Text(driver.phone, style: theme.textTheme.bodyMedium),
        ],
      ),
    );
  }
}

class _Line extends StatelessWidget {
  const _Line({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(label, style: theme.textTheme.bodySmall),
        const SizedBox(height: 2),
        Text(value.isEmpty ? '--' : value, style: theme.textTheme.titleMedium),
      ],
    );
  }
}
