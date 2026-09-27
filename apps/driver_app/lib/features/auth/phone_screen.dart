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
  PhonePlan _plan = beninPlan;
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _changePlan(PhonePlan? plan) {
    if (plan == null || plan == _plan) {
      return;
    }

    setState(() {
      _plan = plan;
      // Les plans n'ont ni la meme longueur ni le meme prefixe : garder les
      // chiffres deja saisis produirait un numero faux qui a l'air correct.
      _controller.clear();
      _error = null;
    });
  }

  Future<void> _submit() async {
    final digits = _controller.text.replaceAll(RegExp(r'\D'), '');
    final phone = _plan.toE164(digits);

    if (phone == null) {
      setState(() => _error = 'Numéro ${_plan.name} incomplet ou invalide.');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      final challenge =
          await ref.read(authRepositoryProvider).requestOtp(phone);

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

    // LE CLAVIER REDUIT LA HAUTEUR DE MOITIE, ET LE SPACER NE PEUT PLUS
    // ABSORBER LA DIFFERENCE : la colonne debordait des l'ouverture du
    // clavier, et sur un petit ecran en paysage sans meme cela.
    //
    // La recette : un defilement qui n'a d'effet que lorsqu'il manque de la
    // place. ConstrainedBox impose au contenu au moins la hauteur visible, et
    // IntrinsicHeight laisse le Spacer coller le bouton en bas tant qu'il
    // reste de la marge. Un simple passage en ListView aurait fait remonter le
    // bouton au milieu de l'ecran sur un grand telephone.
    return Scaffold(
      body: SafeArea(
        child: LayoutBuilder(
          builder: (context, contraintes) => SingleChildScrollView(
            padding: const EdgeInsets.all(HbaSpacing.gutter),
            child: ConstrainedBox(
              constraints: BoxConstraints(
                minHeight: contraintes.maxHeight - HbaSpacing.gutter * 2,
              ),
              child: IntrinsicHeight(
                child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const SizedBox(height: HbaSpacing.xl),
                      const _Logo(),
                      const SizedBox(height: HbaSpacing.lg),
                      Text('Bonjour', style: theme.textTheme.displaySmall),
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
                          // l'etat ensuite, si bien que l'indicatif affiche et le pays
                          // retenu peuvent diverger.
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
                                  value: _plan,
                                  onChanged: _busy ? null : _changePlan,
                                  isExpanded: true,
                                  items: [
                                    for (final plan in phonePlans)
                                      DropdownMenuItem<PhonePlan>(
                                        value: plan,
                                        child: Text(
                                          plan.name,
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
                                LengthLimitingTextInputFormatter(_plan.maxDigits),
                              ],
                              style: const TextStyle(
                                fontSize: 20,
                                fontWeight: FontWeight.w700,
                                letterSpacing: 1.2,
                              ),
                              decoration: InputDecoration(
                                hintText: _plan.hint,
                                prefixIcon: Padding(
                                  padding: const EdgeInsets.only(
                                    left: HbaSpacing.md,
                                    right: HbaSpacing.sm,
                                  ),
                                  child: Text(
                                    _plan.dialingCode,
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
                      HbaButton(
                        label: 'Continuer',
                        busy: _busy,
                        onPressed: _submit,
                      ),
                      const SizedBox(height: HbaSpacing.md),
                      Text(
                        "En continuant, vous acceptez les conditions générales d'HBA Delivery.",
                        textAlign: TextAlign.center,
                        style: theme.textTheme.bodySmall
                            ?.copyWith(color: HbaColors.inkMuted),
                      ),
                      const SizedBox(height: HbaSpacing.sm),
                    ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _Logo extends StatelessWidget {
  const _Logo();

  @override
  Widget build(BuildContext context) {
    return Container(
      height: 56,
      width: 56,
      decoration: BoxDecoration(
        color: HbaColors.primary,
        borderRadius: BorderRadius.circular(18),
      ),
      child: const Icon(Icons.local_shipping_outlined,
          color: Colors.white, size: 30),
    );
  }
}
