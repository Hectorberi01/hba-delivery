import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_ui/hba_ui.dart';

import 'app/router.dart';
import 'core/plantages.dart';
import 'core/service_en_ligne.dart';
import 'core/signal.dart';
import 'features/auth/session_controller.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // LE RAPPORT DE PLANTAGE SE BRANCHE AVANT TOUT LE RESTE. Une erreur survenue
  // pendant le demarrage est precisement celle qu'on ne voit jamais — le
  // livreur relance, ca repart, et personne n'apprend rien.
  //
  // « await », DONC L'APPLICATION ATTEND. C'est quelques dizaines de
  // millisecondes, et sans elles les premieres secondes de vie de
  // l'application ne seraient couvertes par rien.
  await Plantages.demarrer();
  Plantages.brancher();

  // LE CANAL DE NOTIFICATION SE DECLARE UNE FOIS, AU DEMARRAGE, et pas au
  // moment de s'en servir : Android le cree a cette declaration, et un service
  // qui demarre sur un canal inexistant echoue sans rien afficher.
  ServiceEnLigne.preparer();

  runApp(const ProviderScope(child: HbaDriverApp()));
}

class HbaDriverApp extends ConsumerStatefulWidget {
  const HbaDriverApp({super.key});

  @override
  ConsumerState<HbaDriverApp> createState() => _HbaDriverAppState();
}

class _HbaDriverAppState extends ConsumerState<HbaDriverApp> {
  @override
  void initState() {
    super.initState();
    // Relit le trousseau avant d'afficher quoi que ce soit.
    Future.microtask(() => ref.read(sessionProvider.notifier).restore());
  }

  @override
  void dispose() {
    // LE CANAL AUDIO NATIF NE SE FERME PAS TOUT SEUL. Laisse ouvert, il reste
    // reserve apres la fermeture de l'application sur certaines versions
    // d'Android — et le son suivant ne part plus, sans erreur nulle part.
    unawaited(Signal.liberer());
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp.router(
      title: 'HBA Livreur',
      debugShowCheckedModeBanner: false,
      theme: HbaTheme.light(),

      // LE FRANCAIS EST IMPOSE, IL N'EST PAS DEDUIT DU TELEPHONE. Tous les
      // textes de cette application sont ecrits en francais : laisser le
      // cadre suivre la langue de l'appareil donnerait un ecran a moitie
      // francais et a moitie anglais chez un livreur dont le telephone est
      // en anglais — ce qui est courant sur un appareil d'occasion.
      locale: const Locale('fr'),
      supportedLocales: const [Locale('fr')],
      localizationsDelegates: const [
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],

      routerConfig: ref.watch(routerProvider),
    );
  }
}
