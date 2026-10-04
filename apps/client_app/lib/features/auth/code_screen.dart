import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import '../../core/providers.dart';
import '../profil/profil_providers.dart';
import 'auth_repository.dart';
import 'session_controller.dart';

/// Écran 3 : le code reçu par SMS.
///
/// SIX CASES ET NON UN CHAMP, et ce n'est pas une coquetterie. Un champ unique
/// avec un grand interlettrage se lit mal dès qu'un chiffre manque : on ne sait
/// plus où on en est. Six cases disent le nombre attendu avant même qu'on
/// commence, et montrent sans compter combien il en reste.
///
/// IL S'ENVOIE TOUT SEUL AU SIXIEME CHIFFRE. Demander un appui de plus après le
/// dernier chiffre d'un code à six chiffres est une étape que personne ne
/// comprend — le code est complet, il n'y a rien d'autre à décider.
///
/// LE NOM N'EST PLUS DEMANDE ICI, SAUF S'IL MANQUE VRAIMENT. L'écran posait la
/// question à tout le monde, à chaque connexion, y compris à quelqu'un qui a un
/// compte depuis six mois. Le service accepte un nom vide et le profil sait le
/// recueillir ; la question n'est donc posée que lorsque la vérification revient
/// sans nom, c'est-à-dire au tout premier passage.
class CodeScreen extends ConsumerStatefulWidget {
  const CodeScreen({required this.challenge, required this.phone, super.key});

  final OtpChallenge challenge;
  final String phone;

  @override
  ConsumerState<CodeScreen> createState() => _CodeScreenState();
}

class _CodeScreenState extends ConsumerState<CodeScreen> {
  static const _longueur = 6;

  final _nom = TextEditingController();

  late OtpChallenge _defi;
  String _code = '';
  bool _busy = false;
  String? _erreur;

  /// Vrai une fois le code accepté par un compte qui n'a pas encore de nom.
  bool _demandeLeNom = false;

  Timer? _compte;
  int _secondes = 0;

  @override
  void initState() {
    super.initState();
    _defi = widget.challenge;
    _armerLeCompteARebours(_defi.retryAfterSeconds);
  }

  @override
  void dispose() {
    _compte?.cancel();
    _nom.dispose();
    super.dispose();
  }

  /// LE DELAI VIENT DU SERVICE, PAS D'UNE CONSTANTE D'ICI. Deux comptes à
  /// rebours — un sur le téléphone, un dans Redis — finiraient par diverger, et
  /// c'est l'écran qui aurait tort.
  void _armerLeCompteARebours(int secondes) {
    _compte?.cancel();
    setState(() => _secondes = secondes);

    if (secondes <= 0) return;

    _compte = Timer.periodic(const Duration(seconds: 1), (minuterie) {
      if (!mounted) return minuterie.cancel();
      setState(() => _secondes = _secondes <= 1 ? 0 : _secondes - 1);
      if (_secondes == 0) minuterie.cancel();
    });
  }

  void _taper(String chiffre) {
    if (_code.length >= _longueur) return;

    setState(() {
      _code += chiffre;
      _erreur = null;
    });

    if (_code.length == _longueur) unawaited(_verifier());
  }

  void _effacer() {
    if (_code.isEmpty) return;
    setState(() {
      _code = _code.substring(0, _code.length - 1);
      _erreur = null;
    });
  }

  Future<void> _renvoyer() async {
    setState(() {
      _busy = true;
      _erreur = null;
    });

    try {
      final defi = await ref.read(authRepositoryProvider).requestOtp(widget.phone);
      if (!mounted) return;

      // LE NOUVEAU DEFI REMPLACE L'ANCIEN. Garder le premier identifiant ferait
      // verifier le code d'avant, et le refus serait incomprehensible.
      setState(() {
        _defi = defi;
        _code = '';
      });
      _armerLeCompteARebours(defi.retryAfterSeconds);
    } on OfflineException {
      if (mounted) setState(() => _erreur = messageHorsLigne(apiBaseUrl));
    } on ApiException catch (erreur) {
      if (mounted) setState(() => _erreur = erreur.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _verifier() async {
    setState(() {
      _busy = true;
      _erreur = null;
    });

    try {
      final client = await ref.read(authRepositoryProvider).verifyOtp(
            challengeId: _defi.challengeId,
            code: _code,

            // VIDE, ET LE SERVICE L'ACCEPTE : il cree le compte sans nom et on
            // le demande juste apres, une seule fois.
            displayName: '',
          );

      if (!mounted) return;

      if (client.displayName.trim().isEmpty) {
        setState(() {
          _demandeLeNom = true;
          _busy = false;
        });
        return;
      }

      ref.read(sessionProvider.notifier).signedIn(client);
    } on OfflineException {
      if (mounted) {
        setState(() {
          _erreur = messageHorsLigne(apiBaseUrl);
          _code = '';
        });
      }
    } on ApiException catch (erreur) {
      // LE CODE EST EFFACE APRES UN REFUS. Laisser six chiffres faux dans les
      // cases oblige a les effacer un par un avant de reessayer.
      if (mounted) {
        setState(() {
          _erreur = erreur.message;
          _code = '';
        });
      }
    } finally {
      if (mounted && !_demandeLeNom) setState(() => _busy = false);
    }
  }

  Future<void> _enregistrerLeNom() async {
    final nom = _nom.text.trim();
    if (nom.isEmpty) {
      setState(() => _erreur = 'Votre nom est nécessaire pour les livraisons.');
      return;
    }

    setState(() {
      _busy = true;
      _erreur = null;
    });

    try {
      // LE COURRIEL EST VIDE, ET C'EST EXACT ICI. On sort de la creation du
      // compte : il n'y en a aucun a ecraser, et le profil le demandera plus
      // tard si le client veut ses recus par courriel.
      await ref.read(profilRepositoryProvider).enregistrer(nom: nom, courriel: '');
      if (!mounted) return;
      ref.read(sessionProvider.notifier).signedIn(SignedInCustomer(displayName: nom));
    } on OfflineException {
      if (mounted) setState(() => _erreur = messageHorsLigne(apiBaseUrl));
    } on ApiException catch (erreur) {
      if (mounted) setState(() => _erreur = erreur.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(
          leading: _demandeLeNom ? null : const BackButton(),
          backgroundColor: HbaColors.background,
          elevation: 0,
        ),
        body: SafeArea(
          top: false,
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: HbaSpacing.gutter),
            child: _demandeLeNom ? _panneauDuNom() : _panneauDuCode(),
          ),
        ),
      );

  Widget _panneauDuCode() {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          'Entrez le code reçu',
          style: theme.textTheme.headlineSmall
              ?.copyWith(fontWeight: FontWeight.w700),
        ),
        const SizedBox(height: HbaSpacing.sm),
        Text(
          'Nous avons envoyé un code au ${widget.phone}.',
          style: theme.textTheme.bodyMedium?.copyWith(color: HbaColors.inkMuted),
        ),
        const SizedBox(height: HbaSpacing.lg),
        _Cases(code: _code, longueur: _longueur, erreur: _erreur != null),
        if (_erreur != null) ...[
          const SizedBox(height: HbaSpacing.sm),
          Text(
            _erreur!,
            style: theme.textTheme.bodySmall?.copyWith(color: HbaColors.danger),
          ),
        ],
        const SizedBox(height: HbaSpacing.md),
        Center(
          child: _secondes > 0
              ? Text(
                  'Renvoyer le code dans ${_minutes(_secondes)}',
                  style: theme.textTheme.bodySmall
                      ?.copyWith(color: HbaColors.inkMuted),
                )
              : TextButton(
                  onPressed: _busy ? null : () => unawaited(_renvoyer()),
                  child: const Text('Renvoyer le code'),
                ),
        ),
        Expanded(
          child: Align(
            alignment: Alignment.bottomCenter,
            child: ClavierNumerique(
              actif: !_busy,
              onChiffre: _taper,
              onEffacer: _effacer,
            ),
          ),
        ),
        const SizedBox(height: HbaSpacing.sm),
      ],
    );
  }

  Widget _panneauDuNom() {
    final theme = Theme.of(context);

    return ListView(
      children: [
        const SizedBox(height: HbaSpacing.md),
        Text(
          'Comment vous appelez-vous ?',
          style: theme.textTheme.headlineSmall
              ?.copyWith(fontWeight: FontWeight.w700),
        ),
        const SizedBox(height: HbaSpacing.sm),
        Text(
          'Le livreur le verra pour vous reconnaître à la remise.',
          style: theme.textTheme.bodyMedium?.copyWith(color: HbaColors.inkMuted),
        ),
        const SizedBox(height: HbaSpacing.lg),
        TextField(
          controller: _nom,
          autofocus: true,
          textCapitalization: TextCapitalization.words,
          decoration: InputDecoration(
            hintText: 'Nom et prénom',
            errorText: _erreur,
          ),
        ),
        const SizedBox(height: HbaSpacing.lg),
        HbaButton(
          label: 'Terminer',
          busy: _busy,
          onPressed: () => unawaited(_enregistrerLeNom()),
        ),
      ],
    );
  }

  static String _minutes(int secondes) {
    final m = (secondes ~/ 60).toString().padLeft(2, '0');
    final s = (secondes % 60).toString().padLeft(2, '0');
    return '$m:$s';
  }
}

/// Les six cases du code.
class _Cases extends StatelessWidget {
  const _Cases({
    required this.code,
    required this.longueur,
    required this.erreur,
  });

  final String code;
  final int longueur;
  final bool erreur;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Row(
      mainAxisAlignment: MainAxisAlignment.spaceBetween,
      children: [
        for (var i = 0; i < longueur; i++)
          Expanded(
            child: Padding(
              padding: EdgeInsets.only(right: i == longueur - 1 ? 0 : 8),
              child: Container(
                height: 58,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: HbaColors.surface,
                  borderRadius: BorderRadius.circular(HbaRadius.field),
                  border: Border.all(
                    // LA CASE COURANTE EST BORDEE, PAS CLIGNOTANTE. Sans
                    // curseur dans un champ invisible, c'est le seul repere qui
                    // dit ou tombera le prochain chiffre.
                    color: erreur
                        ? HbaColors.danger
                        : i == code.length
                            ? HbaColors.primary
                            : HbaColors.border,
                    width: i == code.length && !erreur ? 1.8 : 1,
                  ),
                ),
                child: Text(
                  i < code.length ? code[i] : '',
                  style: theme.textTheme.headlineSmall
                      ?.copyWith(fontWeight: FontWeight.w700),
                ),
              ),
            ),
          ),
      ],
    );
  }
}
