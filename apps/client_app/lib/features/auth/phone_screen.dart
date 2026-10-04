import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import '../../core/providers.dart';
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

  /// Une touche du pavé. LE PLAFOND EST CELUI DU PLAN, pas une longueur
  /// générique : un numéro béninois fait huit chiffres, un français neuf, et
  /// laisser taper au-delà ferait découvrir le refus au moment d'envoyer.
  void _taper(String chiffre) {
    if (_controller.text.length >= _country.maxDigits) return;

    setState(() {
      _ecrire(_controller.text + chiffre);
      _error = null;
    });
  }

  void _effacer() {
    final saisi = _controller.text;
    if (saisi.isEmpty) return;

    setState(() {
      _ecrire(saisi.substring(0, saisi.length - 1));
      _error = null;
    });
  }

  /// Écrit le texte ET replace le curseur au bout.
  ///
  /// « controller.text = … » NE SUFFIT PAS : l'affectation remplace la valeur
  /// entiere, selection comprise, et la selection devient invalide. Le curseur
  /// se dessine alors au debut du champ, donc a gauche des chiffres deja tapes
  /// — on croit taper avant son propre numero.
  void _ecrire(String valeur) {
    _controller.value = TextEditingValue(
      text: valeur,
      selection: TextSelection.collapsed(offset: valeur.length),
    );
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
      // LE MESSAGE NOMME L'ADRESSE ESSAYEE, EN DEBUG SEULEMENT. « Pas de
      // reseau » est faux quand le Wi-Fi marche et que c'est la passerelle qui
      // ne repond pas — le cas courant en developpement, et celui qui fait
      // chercher le plus longtemps du mauvais cote.
      if (mounted) setState(() => _error = messageHorsLigne(apiBaseUrl));
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final saisi = _controller.text;

    return Scaffold(
      appBar: AppBar(
        leading: BackButton(onPressed: () => context.go('/bienvenue')),
        backgroundColor: HbaColors.background,
        elevation: 0,
      ),
      body: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: HbaSpacing.gutter),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'Votre numéro de téléphone',
                style: theme.textTheme.headlineSmall
                    ?.copyWith(fontWeight: FontWeight.w700),
              ),
              const SizedBox(height: HbaSpacing.sm),
              Text(
                'Nous allons vous envoyer un code par SMS pour vous connecter.',
                style: theme.textTheme.bodyMedium
                    ?.copyWith(color: HbaColors.inkMuted),
              ),

              // L'ADRESSE DU SERVEUR, EN DEBUG SEULEMENT.
              //
              // ELLE SE VOIT AVANT LA PREMIERE TENTATIVE, ET C'EST TOUT
              // L'INTERET. Une base mal reglee ne se manifestait qu'apres la
              // saisie d'un numero, sous la forme d'un « pas de reseau » qui
              // accusait le telephone.
              //
              // JAMAIS EN PRODUCTION : « kDebugMode » est une constante que la
              // compilation en release met a faux, et l'arbre est elague.
              if (kDebugMode) ...[
                const SizedBox(height: HbaSpacing.xs),
                Text(
                  apiBaseUrl,
                  style: theme.textTheme.bodySmall
                      ?.copyWith(color: HbaColors.inkFaint),
                ),
              ],

              const SizedBox(height: HbaSpacing.lg),

              // LE CHAMP NE LEVE PAS LE CLAVIER DU SYSTEME : « readOnly », et
              // c'est le pave du bas qui ecrit. Les deux a la fois feraient
              // sauter l'ecran a chaque pression, et donneraient deux facons de
              // saisir le meme numero — dont une qui ignore le plafond du plan.
              _ChampTelephone(
                plan: _country,
                controller: _controller,
                actif: !_busy,
                onPays: _changeCountry,
              ),

              if (_error != null) ...[
                const SizedBox(height: HbaSpacing.sm),
                Text(
                  _error!,
                  style: theme.textTheme.bodySmall
                      ?.copyWith(color: HbaColors.danger),
                ),
              ],

              const SizedBox(height: HbaSpacing.lg),
              HbaButton(
                label: 'Continuer',
                busy: _busy,

                // RIEN A ENVOYER TANT QUE LE NUMERO EST COURT. Le refus arrive
                // alors avant l'appel, et il est muet : un bouton eteint dit
                // « pas encore » mieux qu'un message d'erreur.
                onPressed: saisi.length < _country.maxDigits ? null : _submit,
              ),

              const SizedBox(height: HbaSpacing.md),
              Row(
                children: [
                  const Expanded(child: Divider()),
                  Padding(
                    padding:
                        const EdgeInsets.symmetric(horizontal: HbaSpacing.sm),
                    child: Text(
                      'ou',
                      style: theme.textTheme.bodySmall
                          ?.copyWith(color: HbaColors.inkFaint),
                    ),
                  ),
                  const Expanded(child: Divider()),
                ],
              ),
              const SizedBox(height: HbaSpacing.xs),

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
          ),
        ),
      ),
    );
  }
}

/// L'indicatif et le numéro, dans un seul champ.
///
/// L'INDICATIF EST UN MENU, PAS UNE LISTE DEROULANTE DE 132 POINTS. La
/// maquette montre un drapeau et « +229 » ; la liste des pays n'a aucune raison
/// d'occuper un quart de la largeur en permanence pour un choix qu'on fait une
/// fois dans sa vie.
class _ChampTelephone extends StatelessWidget {
  const _ChampTelephone({
    required this.plan,
    required this.controller,
    required this.actif,
    required this.onPays,
  });

  final PhonePlan plan;
  final TextEditingController controller;
  final bool actif;
  final ValueChanged<PhonePlan?> onPays;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      height: 60,
      padding: const EdgeInsets.only(left: HbaSpacing.sm, right: HbaSpacing.md),
      decoration: BoxDecoration(
        color: HbaColors.surface,
        borderRadius: BorderRadius.circular(HbaRadius.field),
        border: Border.all(color: HbaColors.border),
      ),
      child: Row(
        children: [
          PopupMenuButton<PhonePlan>(
            enabled: actif,
            onSelected: onPays,
            itemBuilder: (_) => [
              for (final pays in phonePlans)
                PopupMenuItem<PhonePlan>(
                  value: pays,
                  child: Text('${pays.name}  ${pays.dialingCode}'),
                ),
            ],
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  plan.dialingCode,
                  style: theme.textTheme.titleMedium
                      ?.copyWith(fontWeight: FontWeight.w700),
                ),
                const Icon(Icons.arrow_drop_down, color: HbaColors.inkMuted),
              ],
            ),
          ),
          Container(
            width: 1,
            height: 24,
            color: HbaColors.border,
            margin: const EdgeInsets.symmetric(horizontal: HbaSpacing.sm),
          ),
          Expanded(
            child: TextField(
              controller: controller,

              // NI CLAVIER SYSTEME, NI CURSEUR CLIGNOTANT QUI N'ECRIT RIEN.
              readOnly: true,
              showCursor: true,
              decoration: InputDecoration(
                hintText: plan.hint,
                border: InputBorder.none,
                enabledBorder: InputBorder.none,
                focusedBorder: InputBorder.none,
                contentPadding: EdgeInsets.zero,
                isDense: true,
              ),
              style: const TextStyle(
                fontSize: 20,
                fontWeight: FontWeight.w700,
                letterSpacing: 1.4,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
