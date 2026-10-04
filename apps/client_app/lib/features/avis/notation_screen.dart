import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import '../deliveries/paiement_webview.dart';
import 'avis_models.dart';
import 'avis_providers.dart';

/// La seconde page, apres la mission : la note, et le pourboire s'il y en a un.
///
/// UNE PAGE A PART, PAS UNE FENETRE SUR LE SUIVI (point 41, tranche le
/// 3 octobre 2026). Une boite de dialogue posee sur la fin de course se ferme
/// d'un geste reflexe : le client la balaie pour revoir son recu, et la note est
/// perdue sans qu'il ait choisi de la perdre.
///
/// RIEN N'EST OBLIGATOIRE ICI, ET C'EST LA REGLE DU POINT 41 : la notation n'est
/// jamais bloquante. « Passer » est un bouton de plein droit, pas une sortie de
/// secours — et la mission reste notable cote service, pour plus tard.
///
/// LE LIVREUR N'EST NOMME NULLE PART. Ni sur cet ecran, ni dans ce qui part :
/// le client note sa COURSE, et Rating sait a qui la note revient. C'est aussi
/// ce qui rend le pourboire sans danger — l'application ne peut designer
/// personne, donc elle ne peut envoyer d'argent a personne d'autre.
class NotationScreen extends ConsumerStatefulWidget {
  const NotationScreen({required this.avis, super.key});

  final AvisEnAttente avis;

  @override
  ConsumerState<NotationScreen> createState() => _NotationScreenState();
}

class _NotationScreenState extends ConsumerState<NotationScreen> {
  /// Les montants proposes d'un geste.
  ///
  /// TROIS, ET AUCUN PLAFOND. Ce sont des raccourcis, pas une grille : le champ
  /// libre reste ouvert juste en dessous. Un plafond serait une decision
  /// commerciale que personne n'a prise, et une application n'est pas l'endroit
  /// ou l'inventer.
  ///
  /// DES CENTAINES DE FRANCS, PAS DES MILLIERS. Une course de Cotonou coute
  /// quelques centaines a quelques milliers de francs ; proposer 5 000 F
  /// d'abord ferait passer le geste pour une seconde course.
  static const _montants = [200, 500, 1000];

  /// La borne du commentaire, celle du domaine.
  ///
  /// LA MEME QUE COTE SERVICE, ET C'EST POUR CA QU'ELLE EST ECRITE ICI. Rating
  /// refuse au-dela de 200 caracteres ; laisser le champ libre ferait decouvrir
  /// la limite au moment de l'envoi, apres la frappe.
  static const _longueurMaximale = 200;

  final _commentaire = TextEditingController();
  final _montantLibre = TextEditingController();

  int _etoiles = 0;
  int? _pourboire;
  bool _montantLibreOuvert = false;
  bool _envoi = false;
  String? _erreur;

  /// LA CLE EST TIREE UNE FOIS PAR ECRAN, ET RELUE A CHAQUE TENTATIVE. Sans
  /// cela, un second appui apres une erreur reseau — alors que la requete est
  /// peut-etre passee — ouvrirait un SECOND pourboire, et le client paierait
  /// deux fois un geste qu'il a fait une fois.
  final _cleDuPourboire = ApiClient.newIdempotencyKey();

  @override
  void dispose() {
    _commentaire.dispose();
    _montantLibre.dispose();
    super.dispose();
  }

  /// Le montant retenu : une pastille, ou ce qui est tape a la main.
  int? get _montantChoisi {
    if (!_montantLibreOuvert) return _pourboire;

    final saisi = int.tryParse(_montantLibre.text.trim());
    return saisi != null && saisi > 0 ? saisi : null;
  }

  @override
  Widget build(BuildContext context) {
    final avis = widget.avis;

    return Scaffold(
      appBar: AppBar(
        title: Text(avis.estUneCourse ? 'Votre course' : 'Votre livraison'),

        // PAS DE FLECHE DE RETOUR : « Passer » dit la meme chose, en mots, et
        // une fleche laisserait croire qu'on revient sur une etape precedente
        // qui n'existe pas — la mission est finie.
        automaticallyImplyLeading: false,
        actions: [
          TextButton(
            onPressed: _envoi ? null : _passer,
            child: const Text('Passer'),
          ),
        ],
      ),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(HbaSpacing.gutter),
          children: [
            Text(
              avis.estUneCourse
                  ? "Comment s'est passé votre trajet ?"
                  : "Comment s'est passée votre livraison ?",
              style: Theme.of(context).textTheme.titleLarge,
            ),
            if (_quand(avis) case final String quand) ...[
              const SizedBox(height: HbaSpacing.xs),
              Text(
                quand,
                style: Theme.of(context)
                    .textTheme
                    .bodySmall
                    ?.copyWith(color: HbaColors.inkMuted),
              ),
            ],
            const SizedBox(height: HbaSpacing.lg),
            _Etoiles(
              valeur: _etoiles,
              onChange: _envoi ? null : (n) => setState(() => _etoiles = n),
            ),
            const SizedBox(height: HbaSpacing.lg),
            TextField(
              controller: _commentaire,
              enabled: !_envoi,
              maxLines: 3,
              maxLength: _longueurMaximale,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(
                labelText: 'Un mot, si vous voulez',
                hintText: 'Facultatif',
              ),
            ),

            // LE POURBOIRE N'APPARAIT QUE POUR UNE COURSE. La route n'existe pas
            // pour un colis : montrer le geste et echouer a l'ouvrir serait pire
            // que de ne rien proposer.
            if (avis.pourboireOffrable) ...[
              const SizedBox(height: HbaSpacing.md),
              _Pourboire(
                montants: _montants,
                choisi: _pourboire,
                libreOuvert: _montantLibreOuvert,
                controleur: _montantLibre,
                actif: !_envoi,
                onChoisir: (montant) => setState(() {
                  _pourboire = _pourboire == montant ? null : montant;
                  _montantLibreOuvert = false;
                }),
                onAutre: () => setState(() {
                  _montantLibreOuvert = !_montantLibreOuvert;
                  _pourboire = null;
                }),
                onSaisie: () => setState(() {}),
              ),
            ],

            if (_erreur case final String message) ...[
              const SizedBox(height: HbaSpacing.md),
              Text(
                message,
                style: Theme.of(context)
                    .textTheme
                    .bodyMedium
                    ?.copyWith(color: HbaColors.danger),
              ),
            ],

            const SizedBox(height: HbaSpacing.lg),
            HbaButton(
              label: _libelleDuBouton(),

              // SANS ETOILE, RIEN A ENVOYER. Le bareme commence a une etoile :
              // zero ne veut pas dire « tres mauvais », il veut dire « pas
              // encore notee », et le service le refuserait.
              onPressed: _etoiles == 0 || !avis.notable || _envoi ? null : _envoyer,
              busy: _envoi,
            ),
          ],
        ),
      ),
    );
  }

  String _libelleDuBouton() {
    final montant = _montantChoisi;

    return montant == null
        ? 'Envoyer'
        : 'Envoyer et donner ${Xof.format(montant)}';
  }

  String? _quand(AvisEnAttente avis) {
    final date = avis.termineeLe;
    if (date == null) return null;

    final jours = DateTime.now().difference(date).inDays;

    return switch (jours) {
      0 => "Terminée aujourd'hui",
      1 => 'Terminée hier',
      _ => 'Terminée il y a $jours jours',
    };
  }

  /// Passer : on retient la mission pour ne pas la reproposer en boucle.
  void _passer() {
    _retenir();
    if (mounted) Navigator.of(context).pop();
  }

  /// Marque la mission comme deja proposee pour cette session.
  void _retenir() {
    final deja = ref.read(avisDejaProposesProvider);
    ref.read(avisDejaProposesProvider.notifier).state = {
      ...deja,
      widget.avis.reference,
    };
  }

  Future<void> _envoyer() async {
    final navigateur = Navigator.of(context);
    final messager = ScaffoldMessenger.of(context);
    final montant = _montantChoisi;

    setState(() {
      _envoi = true;
      _erreur = null;
    });

    // LA NOTE PART D'ABORD, LE POURBOIRE ENSUITE, ET L'ORDRE EST TOUT L'ENJEU.
    // Un paiement qui echoue ne doit pas emporter la note : elle est deja
    // ecrite, elle reste. L'inverse ferait perdre un avis pour un probleme
    // d'operateur mobile.
    try {
      await ref.read(avisRepositoryProvider).noter(
            nature: widget.avis.nature,
            reference: widget.avis.reference,
            etoiles: _etoiles,
            commentaire: _commentaire.text.trim(),
          );
    } on OfflineException {
      if (mounted) {
        setState(() {
          _envoi = false;
          _erreur = "Pas de réseau. Votre note n'est pas partie — réessayez.";
        });
      }
      return;
    } on ApiException catch (erreur) {
      if (mounted) {
        setState(() {
          _envoi = false;
          _erreur = erreur.message;
        });
      }
      return;
    }

    _retenir();
    ref.invalidate(avisEnAttenteProvider);

    if (montant == null) {
      if (!mounted) return;

      messager.showSnackBar(
        const SnackBar(content: Text('Merci, votre note est enregistrée.')),
      );
      navigateur.pop();
      return;
    }

    // LE POURBOIRE EST UN PAIEMENT, DONC UNE PAGE DU FOURNISSEUR. Sortir de
    // cette page ne prouve rien — ni encaisse, ni refuse (ADR 0017) : on ne lit
    // donc pas son issue, et on ne remercie pas d'un don qui n'est peut-etre
    // pas passe.
    String? adresse;
    try {
      adresse = await ref.read(avisRepositoryProvider).pourboire(
            courseId: widget.avis.reference,
            montantXof: montant,
            idempotencyKey: _cleDuPourboire,
          );
    } on OfflineException {
      if (mounted) {
        setState(() {
          _envoi = false;
          _erreur = 'Votre note est enregistrée. Le pourboire, lui, demande du '
              'réseau — réessayez.';
        });
      }
      return;
    } on ApiException catch (erreur) {
      if (mounted) {
        setState(() {
          _envoi = false;
          _erreur = 'Votre note est enregistrée. ${erreur.message}';
        });
      }
      return;
    }

    if (!mounted) return;

    // RECOPIEE DANS UNE VARIABLE FINALE AVANT LA FERMETURE. Une variable locale
    // reassignable ne garde pas sa promotion « non nulle » a l'interieur d'une
    // closure : sans cette copie, il faudrait un « ! » qui ne dit rien de vrai.
    final adresseDuPaiement = adresse;

    if (adresseDuPaiement == null) {
      setState(() {
        _envoi = false;
        _erreur = "Votre note est enregistrée. La page de paiement n'a pas pu "
            "s'ouvrir : vous pouvez réessayer.";
      });
      return;
    }

    await navigateur.push<IssuePaiement>(
      MaterialPageRoute(builder: (_) => PaiementWebView(url: adresseDuPaiement)),
    );

    if (!mounted) return;
    navigateur.pop();
  }
}

/// Les cinq etoiles.
///
/// DES CIBLES DE 48 POINTS, PAS DES ICONES DE 24. Cinq etoiles serrees sur un
/// telephone tenu d'une main se touchent a cote, et une note posee par erreur
/// ne se reprend pas : Rating n'accepte qu'une note par mission.
class _Etoiles extends StatelessWidget {
  const _Etoiles({required this.valeur, required this.onChange});

  final int valeur;
  final ValueChanged<int>? onChange;

  @override
  Widget build(BuildContext context) => Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          for (var n = 1; n <= 5; n++)
            IconButton(
              onPressed: onChange == null ? null : () => onChange!(n),
              iconSize: 36,
              constraints: const BoxConstraints(
                minWidth: HbaSpacing.cible,
                minHeight: HbaSpacing.cible,
              ),
              icon: Icon(
                n <= valeur ? Icons.star_rounded : Icons.star_border_rounded,
                color: n <= valeur ? HbaColors.primary : HbaColors.inkFaint,
              ),

              // L'ETIQUETTE EST LUE A VOIX HAUTE PAR LES LECTEURS D'ECRAN, ou
              // cinq icones identiques ne diraient rien.
              tooltip: '$n étoile${n > 1 ? 's' : ''}',
            ),
        ],
      );
}

/// Le choix du pourboire : trois raccourcis et un champ libre.
class _Pourboire extends StatelessWidget {
  const _Pourboire({
    required this.montants,
    required this.choisi,
    required this.libreOuvert,
    required this.controleur,
    required this.actif,
    required this.onChoisir,
    required this.onAutre,
    required this.onSaisie,
  });

  final List<int> montants;
  final int? choisi;
  final bool libreOuvert;
  final TextEditingController controleur;
  final bool actif;
  final ValueChanged<int> onChoisir;
  final VoidCallback onAutre;
  final VoidCallback onSaisie;

  @override
  Widget build(BuildContext context) => HbaCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Laisser un pourboire ?',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: HbaSpacing.xs),

            // LE LIVREUR TOUCHE TOUT, ET LE DIRE FAIT PARTIE DU GESTE. HBA ne
            // preleve rien sur un pourboire (point 42) ; un client qui l'ignore
            // suppose le contraire, et un livreur a qui on le raconte mal ne
            // croira plus le reste.
            Text(
              "C'est facultatif, et le livreur reçoit l'intégralité.",
              style: Theme.of(context)
                  .textTheme
                  .bodySmall
                  ?.copyWith(color: HbaColors.inkMuted),
            ),
            const SizedBox(height: HbaSpacing.md),
            Wrap(
              spacing: HbaSpacing.sm,
              runSpacing: HbaSpacing.sm,
              children: [
                for (final montant in montants)
                  ChoiceChip(
                    label: Text(Xof.format(montant)),
                    selected: !libreOuvert && choisi == montant,
                    onSelected: actif ? (_) => onChoisir(montant) : null,
                  ),
                ChoiceChip(
                  label: const Text('Autre'),
                  selected: libreOuvert,
                  onSelected: actif ? (_) => onAutre() : null,
                ),
              ],
            ),
            if (libreOuvert) ...[
              const SizedBox(height: HbaSpacing.md),
              TextField(
                controller: controleur,
                enabled: actif,
                autofocus: true,

                // DES ENTIERS, ET RIEN D'AUTRE. Le franc CFA n'a pas de
                // sous-unite : un clavier a virgule inviterait a taper « 2,50 »
                // pour un montant qui n'existe pas.
                keyboardType: TextInputType.number,
                inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                onChanged: (_) => onSaisie(),
                decoration: const InputDecoration(
                  labelText: 'Montant',
                  suffixText: 'F',
                ),
              ),
            ],
          ],
        ),
      );
}
