import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import 'auth_repository.dart';
import 'session_controller.dart';

class CodeScreen extends ConsumerStatefulWidget {
  const CodeScreen({required this.challenge, required this.phone, super.key});

  final OtpChallenge challenge;
  final String phone;

  @override
  ConsumerState<CodeScreen> createState() => _CodeScreenState();
}

class _CodeScreenState extends ConsumerState<CodeScreen> {
  final _code = TextEditingController();
  final _name = TextEditingController();
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _code.dispose();
    _name.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (_code.text.length != 6) {
      setState(() => _error = 'Le code compte six chiffres.');
      return;
    }

    if (_name.text.trim().isEmpty) {
      setState(() => _error = 'Votre nom est necessaire pour les livraisons.');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      final customer = await ref.read(authRepositoryProvider).verifyOtp(
            challengeId: widget.challenge.challengeId,
            code: _code.text,
            displayName: _name.text.trim(),
          );

      ref.read(sessionProvider.notifier).signedIn(customer);
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
      appBar: AppBar(leading: const BackButton()),
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: HbaSpacing.gutter),
          child: ListView(
            children: [
              Text('Votre code', style: theme.textTheme.displaySmall),
              const SizedBox(height: HbaSpacing.sm),
              Text('Envoye au ${widget.phone}.', style: theme.textTheme.bodyMedium),
              const SizedBox(height: HbaSpacing.xl),
              TextField(
                controller: _code,
                keyboardType: TextInputType.number,
                autofocus: true,
                textAlign: TextAlign.center,
                inputFormatters: [
                  FilteringTextInputFormatter.digitsOnly,
                  LengthLimitingTextInputFormatter(6),
                ],
                style: const TextStyle(
                  fontSize: 30,
                  fontWeight: FontWeight.w800,
                  letterSpacing: 12,
                ),
                decoration: const InputDecoration(hintText: '000000'),
              ),
              const SizedBox(height: HbaSpacing.lg),
              Text('Votre nom', style: theme.textTheme.titleMedium),
              const SizedBox(height: HbaSpacing.sm),
              TextField(
                controller: _name,
                textCapitalization: TextCapitalization.words,
                decoration: InputDecoration(
                  hintText: 'Nom et prenom',
                  errorText: _error,
                ),
              ),
              const SizedBox(height: HbaSpacing.xl),
              HbaButton(label: 'Se connecter', busy: _busy, onPressed: _submit),
            ],
          ),
        ),
      ),
    );
  }
}
