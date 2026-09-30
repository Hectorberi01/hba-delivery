import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import 'session_controller.dart';

class PhoneScreen extends ConsumerStatefulWidget {
  const PhoneScreen({super.key});

  @override
  ConsumerState<PhoneScreen> createState() => _PhoneScreenState();
}

class _PhoneScreenState extends ConsumerState<PhoneScreen> {
  final _controller = TextEditingController();
  PhonePlan _country = beninPlan;
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _changeCountry(PhonePlan? country) {
    if (country == null || country == _country) {
      return;
    }

    setState(() {
      _country = country;
      // Les plans n'ont ni la même longueur ni le même préfixe : garder les
      // chiffres déjà saisis produirait un numéro faux qui a l'air correct.
      _controller.clear();
      _error = null;
    });
  }

  Future<void> _submit() async {
    final digits = _controller.text.replaceAll(RegExp(r'\D'), '');
    final phone = _country.toE164(digits);

    if (phone == null) {
      setState(() => _error = 'Numéro ${_country.name} incomplet ou invalide.');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      final challenge = await ref.read(authRepositoryProvider).requestOtp(phone);
      if (!mounted) return;
      context.push('/code', extra: (challenge, phone));
    } on OfflineException {
      if (mounted) setState(() => _error = 'Pas de réseau. Réessayez.');
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(HbaSpacing.gutter),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const SizedBox(height: HbaSpacing.xl),
              Container(
                height: 56,
                width: 56,
                decoration: BoxDecoration(
                  color: HbaColors.primary,
                  borderRadius: BorderRadius.circular(18),
                ),
                child: const Icon(Icons.inventory_2_outlined,
                    color: Colors.white, size: 30),
              ),
              const SizedBox(height: HbaSpacing.lg),
              Text('Envoyer un colis', style: theme.textTheme.displaySmall),
              const SizedBox(height: HbaSpacing.sm),
              Text(
                'Entrez votre numéro. Vous recevrez un code à six chiffres.',
                style: theme.textTheme.bodyMedium,
              ),
              const SizedBox(height: HbaSpacing.xl),
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  // DropdownButton et non DropdownButtonFormField : le second
                  // ne prend sa valeur qu'au premier rendu et ne suit plus
                  // l'état ensuite. Ici la liste doit toujours montrer le pays
                  // réellement retenu, sinon l'indicatif affiché à côté du champ
                  // et le pays sélectionné peuvent diverger.
                  SizedBox(
                    width: 132,
                    child: Container(
                      height: 56,
                      padding: const EdgeInsets.symmetric(
                        horizontal: HbaSpacing.sm,
                      ),
                      decoration: BoxDecoration(
                        border: Border.all(color: HbaColors.inkFaint),
                        borderRadius: BorderRadius.circular(12),
                      ),
                      child: DropdownButtonHideUnderline(
                        child: DropdownButton<PhonePlan>(
                          value: _country,
                          onChanged: _busy ? null : _changeCountry,
                          isExpanded: true,
                          items: [
                            for (final country in phonePlans)
                              DropdownMenuItem<PhonePlan>(
                                value: country,
                                child: Text(
                                  country.name,
                                  overflow: TextOverflow.ellipsis,
                                  style: theme.textTheme.bodyMedium,
                                ),
                              ),
                          ],
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(width: HbaSpacing.sm),
                  Expanded(
                    child: TextField(
                      controller: _controller,
                      keyboardType: TextInputType.phone,
                      autofocus: true,
                      inputFormatters: [
                        FilteringTextInputFormatter.digitsOnly,
                        LengthLimitingTextInputFormatter(_country.maxDigits),
                      ],
                      style: const TextStyle(
                        fontSize: 20,
                        fontWeight: FontWeight.w700,
                        letterSpacing: 1.2,
                      ),
                      decoration: InputDecoration(
                        hintText: _country.hint,
                        prefixIcon: Padding(
                          padding: const EdgeInsets.only(
                            left: HbaSpacing.md,
                            right: HbaSpacing.sm,
                          ),
                          child: Text(
                            _country.dialingCode,
                            style: theme.textTheme.titleMedium
                                ?.copyWith(color: HbaColors.inkMuted),
                          ),
                        ),
                        prefixIconConstraints: const BoxConstraints(minWidth: 0),
                      ),
                    ),
                  ),
                ],
              ),
              if (_error != null) ...[
                const SizedBox(height: HbaSpacing.sm),
                Text(
                  _error!,
                  style: theme.textTheme.bodySmall
                      ?.copyWith(color: theme.colorScheme.error),
                ),
              ],
              const Spacer(),
              HbaButton(label: 'Continuer', busy: _busy, onPressed: _submit),
              const SizedBox(height: HbaSpacing.md),
              Text(
                "En continuant, vous acceptez les conditions générales d'HBA Delivery.",
                textAlign: TextAlign.center,
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: HbaColors.inkFaint),
              ),
              const SizedBox(height: HbaSpacing.sm),
            ],
          ),
        ),
      ),
    );
  }
}
