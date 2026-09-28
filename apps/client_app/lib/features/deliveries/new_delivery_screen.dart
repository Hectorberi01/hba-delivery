import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import 'delivery_providers.dart';
import 'models.dart';
import 'paiement_webview.dart';
import 'point_field.dart';

/// Creation d'une livraison : adresses, destinataire, devis, paiement.
class NewDeliveryScreen extends ConsumerStatefulWidget {
  const NewDeliveryScreen({super.key});

  @override
  ConsumerState<NewDeliveryScreen> createState() => _NewDeliveryScreenState();
}

class _NewDeliveryScreenState extends ConsumerState<NewDeliveryScreen> {
  PickedPoint? _pickup;
  PickedPoint? _dropoff;

  final _pickupLandmark = TextEditingController();
  final _pickupContact = TextEditingController();
  final _pickupPhone = TextEditingController();

  final _dropoffLandmark = TextEditingController();

  final _recipientName = TextEditingController();
  final _recipientPhone = TextEditingController();
  final _description = TextEditingController();
  final _weight = TextEditingController(text: '1000');

  Quote? _quote;
  bool _busy = false;
  String? _error;

  /// Tiree une seule fois pour toute la tentative de creation : rejouer la
  /// meme cle renvoie la meme livraison au lieu d'en creer une seconde.
  final _idempotencyKey = ApiClient.newIdempotencyKey();

  @override
  void dispose() {
    for (final controller in [
      _pickupLandmark, _pickupContact, _pickupPhone, _dropoffLandmark,
      _recipientName, _recipientPhone, _description, _weight,
    ]) {
      controller.dispose();
    }
    super.dispose();
  }

  /// Le devis depend des points : des qu'ils bougent, il ne vaut plus rien.
  void _invalidateQuote() {
    if (_quote != null) setState(() => _quote = null);
  }

  Future<void> _getQuote() async {
    final pickup = _pickup;
    final dropoff = _dropoff;

    if (pickup == null || dropoff == null) {
      setState(() => _error = 'Choisissez les deux points sur la carte.');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      final quote = await ref.read(deliveryRepositoryProvider).quote(
            pickupLatitude: pickup.latitude,
            pickupLongitude: pickup.longitude,
            dropoffLatitude: dropoff.latitude,
            dropoffLongitude: dropoff.longitude,
            packageWeightGrams: int.tryParse(_weight.text) ?? 1000,
          );

      if (mounted) setState(() => _quote = quote);
    } on OfflineException {
      if (mounted) setState(() => _error = 'Pas de reseau. Reessayez.');
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _create() async {
    final quote = _quote;
    if (quote == null) return;

    // Un devis expire. Mieux vaut en redemander un que d'envoyer au service
    // une creation qu'il refusera.
    if (quote.hasExpiredAt(DateTime.now())) {
      setState(() {
        _quote = null;
        _error = 'Le devis a expire. Demandez-en un nouveau.';
      });
      return;
    }

    if (_recipientName.text.trim().isEmpty || _recipientPhone.text.trim().isEmpty) {
      setState(() => _error = 'Le destinataire et son telephone sont obligatoires.');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    final pickup = _pickup!;
    final dropoff = _dropoff!;

    try {
      final (delivery, redirectUrl) =
          await ref.read(deliveryRepositoryProvider).create(
                idempotencyKey: _idempotencyKey,
                quoteId: quote.id,
                pickup: {
                  'latitude': pickup.latitude,
                  'longitude': pickup.longitude,
                  'landmark': _pickupLandmark.text.trim(),
                  'phone': _pickupPhone.text.trim(),
                  'contactName': _pickupContact.text.trim(),
                  'notes': null,
                },
                dropoff: {
                  'latitude': dropoff.latitude,
                  'longitude': dropoff.longitude,
                  'landmark': _dropoffLandmark.text.trim(),
                  'phone': _recipientPhone.text.trim(),
                  'contactName': _recipientName.text.trim(),
                  'notes': null,
                },
                recipientName: _recipientName.text.trim(),
                recipientPhone: _recipientPhone.text.trim(),
                packageDescription: _description.text.trim(),
                packageWeightGrams: int.tryParse(_weight.text) ?? 1000,
              );

      ref.invalidate(deliveriesProvider);
      if (!mounted) return;

      final issue = redirectUrl == null ? null : await _ouvrirPagePaiement(redirectUrl);

      if (redirectUrl != null && issue == null) {
        // ON RESTE SUR L'ECRAN PLUTOT QUE D'ALLER AU SUIVI. La livraison
        // existe deja, mais elle n'est pas payee : l'envoyer vers un suivi
        // qui n'avancera jamais laisserait le client attendre un livreur qui
        // ne partira pas. Reessayer est sans risque — la meme cle
        // d'idempotence rend la meme livraison et la meme adresse.
        if (mounted) {
          setState(() => _error =
              "Impossible d'ouvrir la page de paiement. Reessayez.");
        }
        return;
      }

      if (!mounted) return;

      if (redirectUrl != null) {
        // LE MESSAGE SUIT CE QUI S'EST PASSE, ET AUCUN NE PROMET UN PAIEMENT.
        //
        // Meme arrive au bout de l'ecran, le client n'a rien prouve : le suivi
        // ne passera en PAID que sur le webhook signe, relu chez le
        // fournisseur (ADR 0017). « Confirmation en cours » est donc la chose
        // la plus forte qu'on ait le droit d'ecrire.
        final message = switch (issue) {
          IssuePaiement.retour => 'Paiement en cours de confirmation.',
          IssuePaiement.abandon =>
            'Paiement interrompu. La course partira une fois payee.',
          IssuePaiement.erreur =>
            "La page de paiement n'a pas pu se charger. Reessayez depuis le suivi.",
          null => 'Terminez le paiement pour lancer la course.',
        };

        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
      }

      context.pushReplacement('/suivi/${delivery.id}');
    } on OfflineException {
      if (mounted) setState(() => _error = 'Pas de reseau. Reessayez.');
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  /// Ouvre la page de paiement DANS l'application, et rend ce qu'il en est
  /// advenu.
  ///
  /// C'ETAIT LE NAVIGATEUR DU TELEPHONE, ET LA RAISON ECRITE ICI ETAIT :
  /// « le paiement mobile money passe souvent par l'application de
  /// l'operateur, qui a besoin de reprendre la main sur l'ecran ». La crainte
  /// etait juste ; elle est desormais traitee dans [PaiementWebView], qui
  /// confie au systeme tout ce qui n'est pas http(s) — un code USSD, le schema
  /// propre d'un operateur — au lieu de tenter de l'afficher.
  ///
  /// CE QUE LE NAVIGATEUR EXTERNE COUTAIT. Quand l'operateur rendait la main,
  /// le client revenait dans Safari, devant une page « Merci », et devait
  /// remarquer tout seul le lien de retour vers l'application. Rien ne l'y
  /// ramenait : aucun schema d'URL n'est declare, ni sur iOS ni sur Android.
  /// Un client qui ne revient pas ne voit jamais sa course partir.
  Future<IssuePaiement?> _ouvrirPagePaiement(String url) {
    if (Uri.tryParse(url) == null) return Future.value(null);

    return Navigator.of(context).push<IssuePaiement>(
      MaterialPageRoute(builder: (_) => PaiementWebView(url: url)),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final quote = _quote;

    return Scaffold(
      appBar: AppBar(title: const Text('Envoyer un colis')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.symmetric(horizontal: HbaSpacing.gutter),
          children: [
            Text('Collecte', style: theme.textTheme.titleMedium),
            const SizedBox(height: HbaSpacing.sm),
            PointField(
              label: 'Point de collecte',
              hint: 'Choisir sur la carte',
              value: _pickup,
              onChanged: (point) {
                setState(() => _pickup = point);
                _invalidateQuote();
              },
            ),
            const SizedBox(height: HbaSpacing.sm),
            TextField(
              controller: _pickupLandmark,
              decoration: const InputDecoration(
                hintText: 'Repere, ex. Carre 442 en face de la pharmacie',
              ),
            ),
            const SizedBox(height: HbaSpacing.sm),
            Row(
              children: [
                Expanded(
                  child: TextField(
                    controller: _pickupContact,
                    decoration: const InputDecoration(hintText: 'Contact sur place'),
                  ),
                ),
                const SizedBox(width: HbaSpacing.sm),
                Expanded(
                  child: TextField(
                    controller: _pickupPhone,
                    keyboardType: TextInputType.phone,
                    decoration: const InputDecoration(hintText: '+229...'),
                  ),
                ),
              ],
            ),
            const SizedBox(height: HbaSpacing.lg),
            Text('Livraison', style: theme.textTheme.titleMedium),
            const SizedBox(height: HbaSpacing.sm),
            PointField(
              label: 'Point de livraison',
              hint: 'Choisir sur la carte',
              value: _dropoff,
              onChanged: (point) {
                setState(() => _dropoff = point);
                _invalidateQuote();
              },
            ),
            const SizedBox(height: HbaSpacing.sm),
            TextField(
              controller: _dropoffLandmark,
              decoration: const InputDecoration(hintText: 'Repere de livraison'),
            ),
            const SizedBox(height: HbaSpacing.sm),
            Row(
              children: [
                Expanded(
                  child: TextField(
                    controller: _recipientName,
                    textCapitalization: TextCapitalization.words,
                    decoration: const InputDecoration(hintText: 'Destinataire'),
                  ),
                ),
                const SizedBox(width: HbaSpacing.sm),
                Expanded(
                  child: TextField(
                    controller: _recipientPhone,
                    keyboardType: TextInputType.phone,
                    decoration: const InputDecoration(hintText: '+229...'),
                  ),
                ),
              ],
            ),
            const SizedBox(height: HbaSpacing.lg),
            Text('Colis', style: theme.textTheme.titleMedium),
            const SizedBox(height: HbaSpacing.sm),
            TextField(
              controller: _description,
              decoration: const InputDecoration(hintText: 'Contenu, ex. documents'),
            ),
            const SizedBox(height: HbaSpacing.sm),
            TextField(
              controller: _weight,
              keyboardType: TextInputType.number,
              inputFormatters: [FilteringTextInputFormatter.digitsOnly],
              onChanged: (_) => _invalidateQuote(),
              decoration: const InputDecoration(
                hintText: 'Poids en grammes',
                suffixText: 'g',
              ),
            ),
            if (_error != null) ...[
              const SizedBox(height: HbaSpacing.md),
              Text(
                _error!,
                style: theme.textTheme.bodyMedium?.copyWith(color: HbaColors.danger),
              ),
            ],
            const SizedBox(height: HbaSpacing.lg),
            if (quote == null)
              HbaButton(label: 'Voir le prix', busy: _busy, onPressed: _getQuote)
            else ...[
              HbaCard(
                highlighted: true,
                padding: const EdgeInsets.all(HbaSpacing.lg),
                child: Row(
                  children: [
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text('Prix de la course',
                              style: theme.textTheme.bodyMedium),
                          Text(
                            Xof.format(quote.totalXof),
                            style: theme.textTheme.displaySmall
                                ?.copyWith(color: HbaColors.primary),
                          ),
                        ],
                      ),
                    ),
                    Text(
                      '${(quote.distanceMeters / 1000).toStringAsFixed(1).replaceAll('.', ',')} km',
                      style: theme.textTheme.bodyMedium,
                    ),
                  ],
                ),
              ),
              const SizedBox(height: HbaSpacing.md),
              HbaButton(
                label: 'Confirmer et payer',
                busy: _busy,
                onPressed: _create,
              ),
            ],
            const SizedBox(height: HbaSpacing.xl),
          ],
        ),
      ),
    );
  }
}
