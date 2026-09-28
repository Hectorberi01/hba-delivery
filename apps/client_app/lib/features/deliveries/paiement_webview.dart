import 'dart:async';

import 'package:flutter/material.dart';
import 'package:hba_ui/hba_ui.dart';
import 'package:url_launcher/url_launcher.dart';
import 'package:webview_flutter/webview_flutter.dart';

/// Comment le client est sorti de la page de paiement.
enum IssuePaiement {
  /// Le fournisseur l'a renvoye sur notre adresse de retour. Cela ne prouve
  /// AUCUN paiement — ni encaisse, ni refuse. Seul le webhook signe fait foi
  /// (ADR 0017). Cela prouve seulement qu'il est alle au bout de l'ecran.
  retour,

  /// Il a ferme la page lui-meme.
  abandon,

  /// La page ne s'est pas chargee.
  erreur,
}

/// La page de paiement du fournisseur, DANS l'application.
///
/// CE CHOIX EN REMPLACE UN AUTRE, ET IL FAUT SAVOIR CE QU'IL COUTE. Le code
/// precedent ouvrait le navigateur du telephone, avec cette raison ecrite noir
/// sur blanc : « le paiement mobile money passe souvent par l'application de
/// l'operateur, qui a besoin de reprendre la main sur l'ecran ». La crainte
/// etait fondee, et elle est traitee plus bas : tout ce qui n'est pas http(s)
/// est confie au systeme au lieu d'etre charge ici.
///
/// CE QUE LE NAVIGATEUR EXTERNE FAISAIT DE PIRE. Quand l'operateur rendait la
/// main, le client revenait dans SAFARI, pas dans HBA. Il se retrouvait devant
/// une page « Merci » et devait remarquer tout seul un petit lien de retour en
/// haut de l'ecran. Un client qui ne revient pas ne voit jamais sa course
/// partir — et il n'y a rien, dans l'application, pour l'y ramener : aucun
/// schema d'URL n'est declare, ni sur iOS ni sur Android.
class PaiementWebView extends StatefulWidget {
  const PaiementWebView({required this.url, super.key});

  /// L'adresse de la page de paiement, rendue par le fournisseur.
  final String url;

  @override
  State<PaiementWebView> createState() => _PaiementWebViewState();
}

class _PaiementWebViewState extends State<PaiementWebView> {
  /// LE RETOUR SE RECONNAIT A SON CHEMIN, JAMAIS A SON HOTE.
  ///
  /// L'hote change a chaque session de developpement — c'est une adresse ngrok
  /// — et changera encore en production. Le chemin, lui, est le notre : il est
  /// fixe dans la passerelle. Comparer l'hote obligerait a transporter
  /// « FEDAPAY_CALLBACK_URL » jusqu'au telephone, et l'ecran se casserait
  /// chaque fois que le tunnel est relance.
  static const _cheminDeRetour = '/paiement/retour';

  late final WebViewController _controleur;

  bool _ferme = false;
  int _avancement = 0;

  @override
  void initState() {
    super.initState();

    _controleur = WebViewController()
      // La page du fournisseur ne fonctionne pas sans JavaScript.
      ..setJavaScriptMode(JavaScriptMode.unrestricted)
      ..setNavigationDelegate(
        NavigationDelegate(
          onProgress: (valeur) {
            if (mounted) setState(() => _avancement = valeur);
          },
          onNavigationRequest: _filtrer,
          onWebResourceError: (erreur) {
            // SEULE L'ERREUR DU CADRE PRINCIPAL COMPTE. Une image ou un script
            // tiers qui tombe ne doit pas fermer un ecran de paiement en
            // cours : la page reste utilisable sans lui.
            if (erreur.isForMainFrame ?? false) {
              _fermer(IssuePaiement.erreur);
            }
          },
        ),
      )
      ..loadRequest(Uri.parse(widget.url));
  }

  /// Ce qui a le droit de se charger ici, et ce qui n'y a rien a faire.
  NavigationDecision _filtrer(NavigationRequest requete) {
    final cible = Uri.tryParse(requete.url);
    if (cible == null) return NavigationDecision.navigate;

    // NOTRE ADRESSE DE RETOUR N'EST JAMAIS CHARGEE, ELLE EST INTERCEPTEE.
    //
    // La charger afficherait une page « Merci » qui ne sert plus a rien —
    // l'application sait deja quoi faire. Et en developpement, elle ferait
    // apparaitre l'avertissement de ngrok, qui conseille au client, juste
    // apres avoir paye, de « se mefier avant de communiquer des informations
    // financieres ». On s'arrete donc un cran avant.
    if (cible.path.startsWith(_cheminDeRetour)) {
      _fermer(IssuePaiement.retour);
      return NavigationDecision.prevent;
    }

    // TOUT CE QUI N'EST PAS http(s) PART AU SYSTEME, ET C'EST LA CLE DE CET
    // ECRAN.
    //
    // C'est ici que se joue la crainte du code precedent. Un paiement mobile
    // money bascule souvent hors du web : « tel: » pour un code USSD, ou le
    // schema propre de l'application de l'operateur. Une vue integree qui
    // tenterait de charger ces adresses elle-meme afficherait une page
    // blanche, et le paiement s'arreterait la sans un mot.
    if (cible.scheme != 'http' && cible.scheme != 'https') {
      unawaited(_confierAuSysteme(cible));
      return NavigationDecision.prevent;
    }

    return NavigationDecision.navigate;
  }

  Future<void> _confierAuSysteme(Uri cible) async {
    try {
      if (await launchUrl(cible, mode: LaunchMode.externalApplication)) return;
    } on Object {
      // Greffon absent, schema inconnu du telephone, refus du systeme : on
      // tombe sur le message ci-dessous, qui vaut mieux que le silence.
    }

    // LE GARDE-FOU EST SUR SA PROPRE LIGNE, et pas fondu dans la condition
    // au-dessus : l'analyseur ne reconnait comme protection d'un « context »
    // apres un « await » qu'un test de « mounted » isole.
    if (!mounted) return;

    // ON LE DIT AU LIEU DE NE RIEN FAIRE. Un echec muet ici laisserait le
    // client devant une page figee, persuade que l'application a plante.
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text("Impossible d'ouvrir ${cible.scheme}.")),
    );
  }

  /// Ferme l'ecran UNE SEULE FOIS.
  ///
  /// Le delegue de navigation et le rapporteur d'erreur peuvent se declencher
  /// coup sur coup ; deux fermetures videraient la pile d'un ecran de trop.
  void _fermer(IssuePaiement issue) {
    if (_ferme || !mounted) return;
    _ferme = true;
    Navigator.of(context).pop(issue);
  }

  /// FERMER EN PLEIN PAIEMENT SE CONFIRME.
  ///
  /// A cet instant, l'argent est peut-etre deja parti. Un retour en arriere
  /// accidentel — le geste de bord sur iOS, le bouton retour sur Android —
  /// ne doit pas interrompre l'ecran sans que le client l'ait voulu.
  Future<bool> _confirmerLAbandon() async {
    final quitter = await showDialog<bool>(
      context: context,
      builder: (contexte) => AlertDialog(
        title: const Text('Quitter le paiement ?'),
        content: const Text(
          'Si vous avez deja valide sur votre telephone, le paiement suit son '
          'cours. La course se mettra a jour toute seule.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(contexte).pop(false),
            child: const Text('Continuer le paiement'),
          ),
          TextButton(
            onPressed: () => Navigator.of(contexte).pop(true),
            child: const Text('Quitter'),
          ),
        ],
      ),
    );

    return quitter ?? false;
  }

  @override
  Widget build(BuildContext context) {
    return PopScope(
      // Le retour est intercepte : c'est « _confirmerLAbandon » qui decide.
      canPop: false,
      onPopInvokedWithResult: (sorti, _) async {
        if (sorti || _ferme) return;
        if (await _confirmerLAbandon()) _fermer(IssuePaiement.abandon);
      },
      child: Scaffold(
        appBar: AppBar(
          title: const Text('Paiement'),
          leading: IconButton(
            icon: const Icon(Icons.close),
            tooltip: 'Quitter le paiement',
            onPressed: () async {
              if (await _confirmerLAbandon()) _fermer(IssuePaiement.abandon);
            },
          ),
          bottom: _avancement >= 100
              ? null
              : PreferredSize(
                  preferredSize: const Size.fromHeight(3),
                  child: LinearProgressIndicator(
                    value: _avancement / 100,
                    minHeight: 3,
                    color: HbaColors.primary,
                  ),
                ),
        ),
        body: SafeArea(child: WebViewWidget(controller: _controleur)),
      ),
    );
  }
}
