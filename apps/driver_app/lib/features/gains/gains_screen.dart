import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import '../../core/rafraichir.dart';
import 'gains_providers.dart';
import 'models.dart';

/// Gains du livreur.
///
/// CET ECRAN AFFICHE UN SOLDE, ET C'EST NOUVEAU. L'ancienne version
/// additionnait les remunerations des vingt-cinq dernieres courses que
/// l'application avait sous la main : un chiffre qui bougeait avec la taille
/// d'une page, et qui devait donc avouer qu'il n'etait pas un solde. Le service
/// Payment tient desormais un grand livre par livreur et calcule les cumuls
/// sur TOUT le compte ; « reste du » est une dette, pas une addition.
///
/// CE QUE LE LIVREUR DOIT COMPRENDRE DE CET ECRAN, en une phrase : demander
/// n'est pas recevoir, et approuver n'est pas recevoir non plus. Le compte ne
/// baisse qu'au moment ou HBA consigne la reference du virement. Tout le reste
/// de la mise en page sert cette distinction.
class GainsScreen extends ConsumerWidget {
  const GainsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);
    final releve = ref.watch(releveProvider);
    final demandes = ref.watch(demandesProvider);

    // « valueOrNull » PLUTOT QUE « when », ET CE N'EST PAS UN DETAIL DE STYLE.
    // Un AsyncValue conserve sa valeur precedente pendant une relecture : lire
    // l'etat par « when » remplacerait le solde par un chargement a chaque
    // rafraichissement, et surtout — puisque « loading » et « error »
    // partagent la meme carte vide — ferait clignoter « lecture impossible »
    // sur un ecran qui marche. Ici, le chiffre affiche reste a l'ecran pendant
    // qu'on en relit un plus frais.
    final compte = releve.valueOrNull;
    final mesDemandes = demandes.valueOrNull;

    return Scaffold(
      body: SafeArea(
        child: RefreshIndicator(
          onRefresh: () async {
            // LES DEUX LECTURES ENSEMBLE : le solde et le suivi des demandes
            // doivent bouger du meme geste, sinon l'ecran affiche un solde
            // d'apres versement avec une demande d'avant.
            await rafraichirDepuis(ref, releveProvider.future);
            await rafraichirDepuis(ref, demandesProvider.future);
          },
          child: ListView(
            padding: const EdgeInsets.all(HbaSpacing.gutter),
            children: [
              Text('Gains', style: theme.textTheme.headlineMedium),
              const SizedBox(height: HbaSpacing.lg),

              _CarteSolde(releve: compte, echec: releve.hasError),

              const SizedBox(height: HbaSpacing.md),

              // LE SUIVI PASSE AVANT LE BOUTON, et non l'inverse : un livreur
              // qui a une demande en cours n'a rien a demander, et lui montrer
              // d'abord un bouton qui va refuser serait une mauvaise farce.
              //
              // LE BOUTON N'APPARAIT PAS AVANT QUE LES DEMANDES SOIENT LUES.
              // L'afficher pendant le chargement laisserait appuyer un livreur
              // qui a deja une demande en cours, pour ne lui repondre qu'apres
              // l'aller-retour — un refus evitable, et incomprehensible de son
              // point de vue.
              if (mesDemandes != null)
                _Suivi(
                  demandes: mesDemandes,
                  disponibleXof: compte?.restantDuXof ?? 0,
                )
              else if (demandes.hasError)
                _EchecDeLecture(erreur: demandes.error!)
              else
                const _Attente(),

              const SizedBox(height: HbaSpacing.lg),

              Text('Mouvements', style: theme.textTheme.titleLarge),
              const SizedBox(height: HbaSpacing.sm),

              if (compte != null)
                _Mouvements(releve: compte)
              else if (releve.hasError)
                _EchecDeLecture(erreur: releve.error!)
              else
                const _Attente(),

              const SizedBox(height: HbaSpacing.xl),
            ],
          ),
        ),
      ),
    );
  }
}

/// Le solde, en grand.
///
/// « RESTE DU » EST LE CHIFFRE PRINCIPAL, pas « gagne ». C'est celui qui
/// interesse le livreur : ce que HBA lui doit aujourd'hui. Le total gagne
/// depuis toujours est une fierte, pas une information utile pour decider de
/// demander un versement.
class _CarteSolde extends StatelessWidget {
  const _CarteSolde({required this.releve, this.echec = false});

  /// Null tant qu'aucune lecture n'a abouti. UN ZERO SE LIRAIT COMME « VOUS
  /// N'AVEZ RIEN », ce qui n'est pas la meme chose que « je n'ai pas encore
  /// lu ».
  final Releve? releve;

  /// LA PREMIERE LECTURE A ECHOUE, ce qui n'est pas « elle est en cours ».
  /// Les deux cas rendaient la meme carte vide dans la premiere version de cet
  /// ecran, donc un ecran qui marchait affichait « lecture impossible » le
  /// temps de son chargement.
  final bool echec;

  @override
  Widget build(BuildContext context) {
    final compte = releve;

    // Pas de textTheme ici : sur un fond degrade, les styles du theme —
    // penses pour de l'encre sur blanc — donneraient du texte illisible.
    return Container(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      decoration: BoxDecoration(
        gradient: const LinearGradient(
          colors: HbaColors.balanceGradient,
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
        ),
        borderRadius: BorderRadius.circular(HbaRadius.card),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text(
            'Ce que HBA vous doit',
            style: TextStyle(color: Colors.white70, fontSize: 13),
          ),
          const SizedBox(height: HbaSpacing.sm),
          Text(
            compte == null ? '—' : Xof.format(compte.restantDuXof),
            style: const TextStyle(
              color: Colors.white,
              fontSize: 34,
              fontWeight: FontWeight.w800,
              letterSpacing: -0.5,
            ),
          ),
          const SizedBox(height: HbaSpacing.md),
          if (compte == null)
            Text(
              echec
                  ? 'Lecture impossible. Tirez vers le bas pour réessayer.'
                  : 'Lecture en cours…',
              style: const TextStyle(color: Colors.white70, fontSize: 13),
            )
          else
            Row(
              children: [
                _Colonne(titre: 'Gagne', valeur: Xof.format(compte.gagneXof)),
                const SizedBox(width: HbaSpacing.lg),
                _Colonne(titre: 'Déjà versé', valeur: Xof.format(compte.verseXof)),
              ],
            ),
        ],
      ),
    );
  }
}

class _Colonne extends StatelessWidget {
  const _Colonne({required this.titre, required this.valeur});

  final String titre;
  final String valeur;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(titre, style: const TextStyle(color: Colors.white70, fontSize: 12)),
        const SizedBox(height: 2),
        Text(
          valeur,
          style: const TextStyle(
            color: Colors.white,
            fontSize: 15,
            fontWeight: FontWeight.w700,
          ),
        ),
      ],
    );
  }
}

/// Ce qui se passe avec les demandes : celle en cours, ou le bouton.
class _Suivi extends ConsumerStatefulWidget {
  const _Suivi({required this.demandes, required this.disponibleXof});

  final List<DemandeVersement> demandes;
  final int disponibleXof;

  @override
  ConsumerState<_Suivi> createState() => _SuiviState();
}

class _SuiviState extends ConsumerState<_Suivi> {
  bool _envoi = false;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    final enCours = widget.demandes.where((d) => d.enCours).toList();
    final derniere = widget.demandes.isEmpty ? null : widget.demandes.first;

    if (enCours.isNotEmpty) {
      return _EnCours(demande: enCours.first);
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // LE MOTIF DU DERNIER REFUS RESTE AFFICHE, et c'est tout l'interet de
        // l'avoir rendu obligatoire cote service : un refus qu'on ne lit pas
        // ne sert a rien, et le livreur redemanderait la meme chose demain.
        if (derniere != null && derniere.etat == EtatVersement.refusee) ...[
          _Refus(demande: derniere),
          const SizedBox(height: HbaSpacing.md),
        ],

        HbaButton(
          label: 'Demander un versement',
          icon: Icons.account_balance_wallet_outlined,
          busy: _envoi,
          onPressed: widget.disponibleXof <= 0 ? null : _ouvrirLaSaisie,
        ),

        const SizedBox(height: HbaSpacing.sm),
        Text(
          widget.disponibleXof <= 0
              ? 'Rien à demander pour le moment. Vos courses livrées viendront ici.'
              : 'HBA fait le virement à la main, puis en consigne la référence. '
                  'Votre solde ne baisse qu\'à ce moment-là.',
          style: theme.textTheme.bodySmall,
        ),
      ],
    );
  }

  Future<void> _ouvrirLaSaisie() async {
    final montant = await showModalBottomSheet<int>(
      context: context,
      // MEME RAISON QUE LA FEUILLE DE RECAPITULATIF : les onglets ont chacun
      // leur Navigator, loge au-dessus de la barre du bas. Sans ceci la
      // feuille se dessine sous la barre et son bas devient inatteignable.
      useRootNavigator: true,
      isScrollControlled: true,
      // LA FEUILLE EST DE LA COULEUR DU FOND, pas blanche : les blocs
      // qu'elle contient sont en relief, et un relief ne se lit que
      // s'il sort de la meme matiere que le reste.
      backgroundColor: HbaColors.background,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(HbaRadius.card)),
      ),
      builder: (_) => _SaisieDuMontant(disponibleXof: widget.disponibleXof),
    );

    if (montant == null || !mounted) return;

    setState(() => _envoi = true);

    try {
      await ref.read(gainsRepositoryProvider).demanderVersement(montant);

      // LES DEUX PROVIDERS, PAS SEULEMENT LA LISTE. Le solde ne change pas —
      // c'est la regle — mais la carte doit etre relue quand meme : entre
      // l'ouverture de l'ecran et l'envoi, une course a pu etre livree.
      ref.invalidate(demandesProvider);
      ref.invalidate(releveProvider);
    } on ApiException catch (erreur) {
      // LE MESSAGE DU SERVICE EST AFFICHE TEL QUEL. Il est redige en francais
      // et, pour un depassement, il DIT LE MONTANT DISPONIBLE. Le remplacer
      // par un « demande refusee » maison ferait perdre precisement
      // l'information qui permet de redemander juste.
      _dire(erreur.message);
    } on OfflineException {
      _dire('Pas de réseau. Votre demande n\'a pas été envoyée.');
    } finally {
      if (mounted) setState(() => _envoi = false);
    }
  }

  void _dire(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(message)));
  }
}

/// La demande en cours.
class _EnCours extends StatelessWidget {
  const _EnCours({required this.demande});

  final DemandeVersement demande;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final approuvee = demande.etat == EtatVersement.approuvee;

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  Xof.format(demande.montantXof),
                  style: theme.textTheme.titleLarge,
                ),
              ),
              HbaChip(
                label: approuvee ? 'ACCORD DONNE' : 'EN ATTENTE',
                tone: approuvee ? HbaChipTone.primary : HbaChipTone.warning,
              ),
            ],
          ),
          const SizedBox(height: HbaSpacing.md),
          Text(
            approuvee
                // LA NUANCE EST TOUT LE SUJET, ET ELLE EST DITE EN CLAIR.
                // « Approuve » ne veut pas dire « paye » : le virement est
                // fait a la main, ailleurs, et peut attendre. Laisser croire
                // le contraire ferait attendre l'argent d'un jour a l'autre.
                ? 'HBA a donné son accord. Le virement n\'est pas encore parti : '
                    'il se fait à la main chez l\'opérateur. Vous le verrez ici '
                    'dès que sa référence sera consignée.'
                : 'Votre demande est arrivée. HBA doit encore l\'approuver, '
                    'puis faire le virement.',
            style: theme.textTheme.bodyMedium,
          ),
          const SizedBox(height: HbaSpacing.md),
          _Ligne(libelle: 'Demande', valeur: _quand(demande.demandeeLe)),
          if (approuvee) _Ligne(libelle: 'Accord', valeur: _quand(demande.decideeLe)),
          const SizedBox(height: HbaSpacing.md),
          Text(
            'Une seule demande à la fois. La prochaine sera possible quand '
            'celle-ci sera soldée.',
            style: theme.textTheme.bodySmall,
          ),
        ],
      ),
    );
  }
}

class _Refus extends StatelessWidget {
  const _Refus({required this.demande});

  final DemandeVersement demande;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const HbaChip(label: 'DERNIÈRE DEMANDE REFUSÉE', tone: HbaChipTone.danger),
          const SizedBox(height: HbaSpacing.md),
          Text(
            '${Xof.format(demande.montantXof)} — ${_quand(demande.decideeLe)}',
            style: theme.textTheme.titleMedium,
          ),
          if (demande.motifDeRefus.isNotEmpty) ...[
            const SizedBox(height: HbaSpacing.sm),
            Text(demande.motifDeRefus, style: theme.textTheme.bodyMedium),
          ],
          const SizedBox(height: HbaSpacing.sm),
          Text(
            'Vous pouvez en ouvrir une autre.',
            style: theme.textTheme.bodySmall,
          ),
        ],
      ),
    );
  }
}

/// Saisie du montant.
class _SaisieDuMontant extends StatefulWidget {
  const _SaisieDuMontant({required this.disponibleXof});

  final int disponibleXof;

  @override
  State<_SaisieDuMontant> createState() => _SaisieDuMontantState();
}

class _SaisieDuMontantState extends State<_SaisieDuMontant> {
  late final TextEditingController _controleur;
  String? _erreur;

  @override
  void initState() {
    super.initState();

    // PRE-REMPLI AVEC LE SOLDE, PARCE QUE C'EST CE QUE LE LIVREUR VEUT NEUF
    // FOIS SUR DIX. Mais c'est un NOMBRE, pas un « tout retirer » : le montant
    // part fige. Le contrat n'a volontairement pas de « videz mon compte »,
    // sinon il faudrait expliquer pourquoi le versement ne correspond pas a ce
    // qui etait affiche quand une course tombe entre-temps.
    _controleur = TextEditingController(text: '${widget.disponibleXof}');
  }

  @override
  void dispose() {
    _controleur.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: EdgeInsets.only(
        left: HbaSpacing.gutter,
        right: HbaSpacing.gutter,
        top: HbaSpacing.lg,
        // TROIS TERMES, ET AUCUN N'EST DE TROP. « viewInsets » est le clavier ;
        // « padding » est ce qui reste de la barre de gestes UNE FOIS le
        // clavier deduit, donc les deux ne se cumulent jamais a tort. Depuis
        // que la feuille monte au Navigator racine, elle descend jusqu'au bas
        // reel de l'ecran : sans ce second terme, son bouton se retrouverait
        // sous le trait de navigation d'Android.
        bottom: MediaQuery.viewInsetsOf(context).bottom +
            MediaQuery.paddingOf(context).bottom +
            HbaSpacing.lg,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Demander un versement', style: theme.textTheme.headlineMedium),
          const SizedBox(height: HbaSpacing.sm),
          Text(
            'Disponible : ${Xof.format(widget.disponibleXof)}',
            style: theme.textTheme.bodyMedium,
          ),
          const SizedBox(height: HbaSpacing.lg),
          TextField(
            controller: _controleur,
            autofocus: true,
            keyboardType: TextInputType.number,
            textAlign: TextAlign.center,
            inputFormatters: [
              FilteringTextInputFormatter.digitsOnly,
              LengthLimitingTextInputFormatter(9),
            ],
            style: const TextStyle(fontSize: 30, fontWeight: FontWeight.w800),
            decoration: InputDecoration(
              suffixText: 'F',
              errorText: _erreur,
            ),
            onChanged: (_) {
              if (_erreur != null) setState(() => _erreur = null);
            },
          ),
          const SizedBox(height: HbaSpacing.md),
          Text(
            'HBA vérifie votre compte, fait le virement, puis en consigne la '
            'référence. Votre solde baissera à ce moment-là, pas avant.',
            style: theme.textTheme.bodySmall,
          ),
          const SizedBox(height: HbaSpacing.lg),
          HbaButton(label: 'Envoyer la demande', onPressed: _valider),
        ],
      ),
    );
  }

  void _valider() {
    final montant = int.tryParse(_controleur.text.trim()) ?? 0;

    // DEUX CONTROLES SEULEMENT ICI, ET AUCUN AUTRE. Le montant positif et le
    // plafond du solde affiche se verifient sans reseau, et epargnent un
    // aller-retour. Tout le reste — minimum configure, delai de carence,
    // demande deja en cours — appartient au service : le recopier ici
    // produirait deux jeux de regles qui finiraient par diverger, et c'est
    // celui de l'ecran qui aurait tort.
    if (montant <= 0) {
      setState(() => _erreur = 'Entrez un montant.');
      return;
    }

    if (montant > widget.disponibleXof) {
      setState(() => _erreur = 'Plus que votre solde de '
          '${Xof.format(widget.disponibleXof)}.');
      return;
    }

    Navigator.of(context).pop(montant);
  }
}

/// La liste des mouvements.
class _Mouvements extends StatelessWidget {
  const _Mouvements({required this.releve});

  final Releve releve;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    if (releve.mouvements.isEmpty) {
      return HbaCard(
        padding: const EdgeInsets.all(HbaSpacing.lg),
        child: Text(
          'Aucun mouvement pour l\'instant. Votre première course livrée '
          'apparaîtra ici.',
          style: theme.textTheme.bodyMedium,
        ),
      );
    }

    final coupees = releve.totalMouvements - releve.mouvements.length;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        HbaCard(
          padding: const EdgeInsets.symmetric(vertical: HbaSpacing.sm),
          child: Column(
            children: [
              for (final mouvement in releve.mouvements)
                _LigneDeMouvement(mouvement: mouvement),
            ],
          ),
        ),

        // CE QUE LA LISTE NE MONTRE PAS EST DIT, plutot que laisse deviner.
        // Les cumuls de la carte portent sur tout le compte ; cette liste est
        // plafonnee. Sans cette phrase, un livreur qui additionne les lignes
        // et ne retombe pas sur son solde conclut a une erreur.
        if (coupees > 0) ...[
          const SizedBox(height: HbaSpacing.sm),
          Text(
            '$coupees mouvement${coupees > 1 ? 's' : ''} plus ancien'
            '${coupees > 1 ? 's' : ''} ne ${coupees > 1 ? 'sont' : 'est'} pas '
            'affiche${coupees > 1 ? 's' : ''}. Les montants ci-dessus, eux, '
            'portent sur tout votre compte.',
            style: theme.textTheme.bodySmall,
          ),
        ],
      ],
    );
  }
}

class _LigneDeMouvement extends StatelessWidget {
  const _LigneDeMouvement({required this.mouvement});

  final Mouvement mouvement;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final entree = mouvement.estUneEntree;
    final indetermine = mouvement.sensIndetermine;

    final titre = switch (mouvement.nature) {
      NatureMouvement.course => mouvement.referenceCourse.isEmpty
          ? 'Course livrée'
          : 'Course ${mouvement.referenceCourse}',
      NatureMouvement.versement => 'Versement reçu',
      NatureMouvement.inconnue => 'Mouvement',
    };

    return Padding(
      padding: const EdgeInsets.symmetric(
        horizontal: HbaSpacing.lg,
        vertical: HbaSpacing.sm + 2,
      ),
      child: Row(
        children: [
          // UNE TUILE CREUSEE, PAS UN APLAT POSE. Les aplats doux du systeme
          // n'ont presque aucun ecart de luminance avec la matiere — 1,01:1 —
          // donc poses a plat ils se liraient comme une tache de couleur. Ce
          // qui leur donne une forme, c'est le creux.
          HbaCreux(
            radius: HbaRadius.field,
            color: indetermine
                ? HbaColors.surfaceSunken
                : entree
                    ? HbaColors.successSoft
                    : HbaColors.primarySoft,
            child: SizedBox(
              height: 36,
              width: 36,
              child: Icon(
                indetermine
                    ? Icons.help_outline
                    : entree
                        ? Icons.south_west
                        : Icons.north_east,
                size: 18,
                color: indetermine
                    ? HbaColors.inkMuted
                    : entree
                        ? HbaColors.success
                        : HbaColors.primaryInk,
              ),
            ),
          ),
          const SizedBox(width: HbaSpacing.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(titre, style: theme.textTheme.titleMedium),
                Text(_quand(mouvement.survenuLe), style: theme.textTheme.bodySmall),
              ],
            ),
          ),
          Text(
            // LE SIGNE EST AJOUTE A L'AFFICHAGE, jamais porte par le montant :
            // le serveur ne rend que des nombres positifs, et le sens est un
            // champ a part. UN SENS INCONNU N'EN RECOIT AUCUN — mettre « − »
            // par defaut ferait lire une sortie d'argent la ou l'application
            // ne sait simplement pas.
            indetermine
                ? Xof.format(mouvement.montantXof)
                : '${entree ? '+' : '−'} ${Xof.format(mouvement.montantXof)}',
            style: theme.textTheme.titleMedium?.copyWith(
              color: entree ? HbaColors.success : HbaColors.ink,
            ),
          ),
        ],
      ),
    );
  }
}

class _Attente extends StatelessWidget {
  const _Attente();

  @override
  Widget build(BuildContext context) => const Padding(
        padding: EdgeInsets.symmetric(vertical: HbaSpacing.xl),
        child: Center(child: CircularProgressIndicator()),
      );
}

class _EchecDeLecture extends StatelessWidget {
  const _EchecDeLecture({required this.erreur});

  final Object erreur;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    // LE MESSAGE DU SERVICE QUAND IL Y EN A UN. Un « une erreur est survenue »
    // generique cache la seule phrase qui pourrait aider le livreur a savoir
    // s'il doit reessayer ou appeler.
    // LA VARIABLE LOCALE N'EST PAS DU CONFORT : la promotion de type ne
    // s'applique pas a un champ public, donc « erreur.message » apres un
    // « erreur is ApiException » ne compilerait pas sur le champ lui-meme.
    final cause = erreur;

    final message = cause is ApiException
        ? cause.message
        : cause is OfflineException
            ? 'Pas de réseau.'
            : 'Lecture impossible.';

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(message, style: theme.textTheme.bodyMedium),
          const SizedBox(height: HbaSpacing.sm),
          Text(
            'Tirez vers le bas pour réessayer.',
            style: theme.textTheme.bodySmall,
          ),
        ],
      ),
    );
  }
}

class _Ligne extends StatelessWidget {
  const _Ligne({required this.libelle, required this.valeur});

  final String libelle;
  final String valeur;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(bottom: HbaSpacing.xs),
      child: Row(
        children: [
          SizedBox(
            width: 92,
            child: Text(libelle, style: theme.textTheme.bodySmall),
          ),
          Expanded(child: Text(valeur, style: theme.textTheme.bodyMedium)),
        ],
      ),
    );
  }
}

/// Une date courte, ou un tiret.
///
/// PAS DE « IL Y A TROIS HEURES » ICI. Un versement est un fait comptable : le
/// livreur veut la date, pour la retrouver sur son telephone ou en parler au
/// support. Le relatif convient a une offre qui expire, pas a de l'argent.
String _quand(DateTime? instant) {
  if (instant == null) return '—';

  final d = instant;
  String deuxChiffres(int v) => v.toString().padLeft(2, '0');

  return '${deuxChiffres(d.day)}/${deuxChiffres(d.month)} à '
      '${deuxChiffres(d.hour)}h${deuxChiffres(d.minute)}';
}
