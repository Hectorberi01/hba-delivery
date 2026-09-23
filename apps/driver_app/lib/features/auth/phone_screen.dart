import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_ui/hba_ui.dart';

import '../../core/api_exception.dart';
import 'session_controller.dart';

/// Indicatif du pilote. A rendre selectionnable le jour ou HBA sort du Benin.
const _dialingCode = '+229';

class PhoneScreen extends ConsumerStatefulWidget {
  const PhoneScreen({super.key});

  @override
  ConsumerState<PhoneScreen> createState() => _PhoneScreenState();
}

class _PhoneScreenState extends ConsumerState<PhoneScreen> {
  final _controller = TextEditingController();
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final digits = _controller.text.replaceAll(RegExp(r'\D'), '');
    if (digits.length < 8) {
      setState(() => _error = 'Numero incomplet.');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    final phone = '$_dialingCode$digits';

    try {
      final challenge =
          await ref.read(authRepositoryProvider).requestOtp(phone);

      if (!mounted) return;
      context.push('/code', extra: (challenge, phone));
    } on OfflineException {
      if (mounted) setState(() => _error = 'Pas de reseau. Reessayez.');
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
              const _Logo(),
              const SizedBox(height: HbaSpacing.lg),
              Text('Bonjour', style: theme.textTheme.displaySmall),
              const SizedBox(height: HbaSpacing.sm),
              Text(
                'Entrez votre numero. Vous recevrez un code a six chiffres.',
                style: theme.textTheme.bodyMedium,
              ),
              const SizedBox(height: HbaSpacing.xl),
              TextField(
                controller: _controller,
                keyboardType: TextInputType.phone,
                autofocus: true,
                inputFormatters: [
                  FilteringTextInputFormatter.digitsOnly,
                  LengthLimitingTextInputFormatter(12),
                ],
                style: const TextStyle(
                  fontSize: 20,
                  fontWeight: FontWeight.w700,
                  letterSpacing: 1.2,
                ),
                decoration: InputDecoration(
                  hintText: '97 00 00 00',
                  prefixIcon: Padding(
                    padding: const EdgeInsets.only(
                      left: HbaSpacing.md,
                      right: HbaSpacing.sm,
                    ),
                    child: Text(
                      _dialingCode,
                      style: theme.textTheme.titleMedium
                          ?.copyWith(color: HbaColors.inkMuted),
                    ),
                  ),
                  prefixIconConstraints: const BoxConstraints(minWidth: 0),
                  errorText: _error,
                ),
              ),
              const Spacer(),
              HbaButton(
                label: 'Continuer',
                busy: _busy,
                onPressed: _submit,
              ),
              const SizedBox(height: HbaSpacing.md),
              Text(
                "En continuant, vous acceptez les conditions generales d'HBA Delivery.",
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
