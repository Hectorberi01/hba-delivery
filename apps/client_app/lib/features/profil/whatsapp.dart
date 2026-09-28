import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';

import '../../core/providers.dart';
import 'tuile.dart';

/// Le consentement a recevoir des messages WhatsApp.
///
/// CE BLOC ANNONCAIT « A VENIR » DEVANT UN MECANISME COMPLET. Le domaine
/// d'Identity porte le consentement depuis le 25 septembre : la colonne,
/// l'evenement auditable, et « CanReceiveWhatsApp » qui decide deja par quel
/// canal part le code de connexion. Il manquait la commande et la route ; l'app
/// en a conclu que la fonction n'existait pas.
///
/// EXPLICITE ET REVOCABLE, comme le referentiel l'exige. Explicite : le defaut
/// est « non », et seul un geste l'accorde. Revocable : le meme interrupteur le
/// retire, par le meme appel — un retrait plus difficile que l'accord ne serait
/// pas une revocation.
class ConsentementWhatsApp {
  const ConsentementWhatsApp({required this.accorde, this.accordeLe});

  final bool accorde;
  final DateTime? accordeLe;

  static ConsentementWhatsApp depuis(Map<String, dynamic> json) {
    final quand = json['grantedAt'];

    return ConsentementWhatsApp(
      accorde: json['granted'] == true,
      accordeLe: quand is String ? DateTime.tryParse(quand) : null,
    );
  }
}

class WhatsAppRepository {
  WhatsAppRepository(this._api);

  final ApiClient _api;

  Future<ConsentementWhatsApp> lire() async =>
      ConsentementWhatsApp.depuis(await _api.get('/me/whatsapp'));

  Future<ConsentementWhatsApp> enregistrer(bool accorde) async =>
      ConsentementWhatsApp.depuis(
        await _api.put('/me/whatsapp', body: {'granted': accorde}),
      );
}

final whatsAppRepositoryProvider = Provider<WhatsAppRepository>(
  (ref) => WhatsAppRepository(ref.watch(apiClientProvider)),
);

final whatsAppProvider = FutureProvider<ConsentementWhatsApp>(
  (ref) => ref.watch(whatsAppRepositoryProvider).lire(),
);

class BlocWhatsApp extends ConsumerStatefulWidget {
  const BlocWhatsApp({super.key});

  @override
  ConsumerState<BlocWhatsApp> createState() => _BlocWhatsAppState();
}

class _BlocWhatsAppState extends ConsumerState<BlocWhatsApp> {
  bool _enCours = false;

  /// L'INTERRUPTEUR NE BOUGE QU'UNE FOIS LE SERVICE D'ACCORD.
  ///
  /// Le basculer tout de suite, puis le remettre en cas d'echec, donne un
  /// consentement qui s'affiche accorde une seconde avant de ne plus l'etre.
  /// Sur un consentement, cette seconde-la n'est pas acceptable : ce que
  /// l'ecran montre doit etre ce que le service a enregistre.
  Future<void> _basculer(bool valeur) async {
    setState(() => _enCours = true);

    try {
      await ref.read(whatsAppRepositoryProvider).enregistrer(valeur);
      ref.invalidate(whatsAppProvider);
    } on Object catch (erreur) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            erreur is ApiException
                ? erreur.message
                : 'Enregistrement impossible.',
          ),
        ),
      );
    } finally {
      if (mounted) setState(() => _enCours = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final async = ref.watch(whatsAppProvider);

    return HbaTuile(
      icone: Icons.chat_bubble_outline,
      teinte: const Color(0xFFE3F0FB),
      couleurIcone: const Color(0xFF1B6FA8),
      titre: 'Recevoir sur WhatsApp',
      detail: async.when(
        loading: () => 'Chargement…',
        error: (_, __) => 'État indisponible. Tirez vers le bas.',
        // CE QUE LE CONSENTEMENT CHANGE VRAIMENT, DIT SIMPLEMENT.
        // « Consentement explicite et revocable » est du vocabulaire de
        // referentiel ; le client, lui, veut savoir ou arrivera son code.
        data: (c) => c.accorde
            ? 'Votre code de connexion et le suivi de vos courses arrivent '
                'sur WhatsApp.'
            : 'Tout arrive par SMS par défaut. Activez pour recevoir sur '
                'WhatsApp à la place.',
      ),
      fin: Switch(
        value: async.valueOrNull?.accorde ?? false,
        onChanged: _enCours || !async.hasValue ? null : _basculer,
      ),
    );
  }
}
