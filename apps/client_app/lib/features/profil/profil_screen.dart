import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import '../auth/session_controller.dart';
import 'adresses.dart';
import 'bloc_adresses.dart';
import 'formulaire_profil.dart';
import 'photo_profil.dart';
import 'profil_providers.dart';
import 'suppression_compte.dart';
import 'support.dart';
import 'tuile.dart';
import 'whatsapp.dart';

/// Le compte du client.
///
/// IL PORTE MAINTENANT UNE FLECHE DE RETOUR, ET ELLE EST HONNETE. Le profil
/// etait un ONGLET de la navigation en arc : rien derriere lui, donc pas de
/// fleche. Depuis le retrait de l'arc le 2 octobre 2026, il s'ouvre EMPILE sur
/// l'accueil, par le portrait du haut ; la fleche que Flutter ajoute alors
/// ramene vraiment la ou l'on etait.
///
/// C'EST AUSSI DEVENU LA PORTE DU RESTE DE L'APPLICATION. « Mes courses » et
/// « Aide » n'ont plus d'onglet : on y entre d'ici, et de nulle part ailleurs.
/// Deux lignes en haut de l'ecran, avant les reglages du compte — quelqu'un qui
/// cherche sa derniere commande ne doit pas avoir a traverser ses adresses
/// favorites pour la trouver.
///
/// LE TELEPHONE NE SE MODIFIE PAS ICI. C'est l'identifiant du compte, celui par
/// lequel arrive le code de connexion ; le changer serait changer de compte.
class ProfilScreen extends ConsumerWidget {
  const ProfilScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final async = ref.watch(profilProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Mon profil'), centerTitle: true),
      body: SafeArea(
        child: RefreshIndicator(
          onRefresh: () async {
            ref.invalidate(profilProvider);
            ref.invalidate(adressesProvider);
            ref.invalidate(whatsAppProvider);

            // LE LIEN DE LA PHOTO AUSSI, ET IL NE SUIT PAS TOUT SEUL. Il ne se
            // recalcule que si l'identifiant du media change ; or ce qui perime
            // ici, c'est la SIGNATURE de l'adresse, pas la photo. Un tirage vers
            // le bas est justement le geste de quelqu'un dont le portrait vient
            // de disparaître.
            ref.invalidate(photoProvider);
            ref.invalidate(suppressionProvider);
          },
          child: ListView(
            padding: const EdgeInsets.fromLTRB(
              HbaSpacing.gutter,
              HbaSpacing.md,
              HbaSpacing.gutter,
              HbaSpacing.xxl,
            ),
            children: [
              // EN HAUT, AVANT LE PROFIL LUI-MEME. Un compte en sursis n'est
              // pas un detail de reglage : c'est la premiere chose a dire a
              // quelqu'un dont le compte disparaitra dans trois semaines.
              const BandeauSuppression(),

              async.when(
                loading: () => const Padding(
                  padding: EdgeInsets.all(HbaSpacing.xl),
                  child: Center(child: CircularProgressIndicator()),
                ),
                error: (erreur, __) => _Echec(erreur: erreur),
                data: (profil) => _Identite(profil: profil),
              ),

              // LA BANNIERE NE PARAIT QUE SI ELLE A QUELQUE CHOSE A DEMANDER.
              // Affichee en permanence, elle devient un element de decor qu'on
              // ne lit plus — et le jour ou elle dira autre chose, personne ne
              // le remarquera.
              if (async.valueOrNull?.courriel.trim().isEmpty ?? false) ...[
                const SizedBox(height: HbaSpacing.md),
                const _BanniereCourriel(),
              ],

              // LE PROFIL EST LE HUB DE L'APPLICATION, depuis le 4 octobre
              // 2026. L'accueil est une carte plein ecran et n'a qu'une porte —
              // le menu en haut a gauche, qui ouvre cet ecran. Tout ce que le
              // client vient chercher hors d'une commande en cours part d'ici :
              // ses informations, ses commandes, son wallet.
              const SizedBox(height: HbaSpacing.lg),
              const HbaSection('Mon activité'),
              HbaTuile(
                icone: Icons.receipt_long_outlined,

                // « MES COMMANDES » ET NON « MES COURSES ». Le mot « course »
                // designe deja le transport de personnes dans ce depot ; au
                // singulier il disait les deux, et sur cet ecran il ne pouvait
                // qu'induire en erreur.
                titre: 'Mes commandes',
                detail: 'Colis envoyés et trajets passés',
                onTap: () => context.push('/courses'),
              ),
              const SizedBox(height: HbaSpacing.sm),
              HbaTuile(
                icone: Icons.account_balance_wallet_outlined,
                titre: 'Mon wallet',
                detail: 'Vos avoirs ZEDJO',
                onTap: () => context.push('/wallet'),
              ),
              const SizedBox(height: HbaSpacing.sm),
              HbaTuile(
                icone: Icons.help_outline,
                titre: 'Aide',
                detail: 'Les questions qui reviennent',
                onTap: () => context.push('/aide'),
              ),

              const SizedBox(height: HbaSpacing.lg),
              const HbaSection('Adresses favorites'),
              const BlocAdresses(),

              const SizedBox(height: HbaSpacing.lg),
              const HbaSection('Messages'),
              const BlocWhatsApp(),

              const SizedBox(height: HbaSpacing.lg),
              const BlocSupport(),

              const SizedBox(height: HbaSpacing.xl),
              _Deconnexion(),

              // TOUT EN BAS, ET DISCRETE. Elle doit EXISTER — Apple exige
              // qu'une suppression de compte s'amorce dans l'application — mais
              // rien n'oblige a la mettre sur le chemin de quelqu'un qui
              // cherchait ses adresses favorites.
              const SizedBox(height: HbaSpacing.sm),
              const EntreeSuppression(),
            ],
          ),
        ),
      ),
    );
  }
}

/// La carte d'identite : portrait, nom, telephone, et le geste pour modifier.
///
/// DEUX ZONES TOUCHABLES DANS UNE SEULE CARTE, et c'est voulu. La carte entiere
/// ouvre le formulaire ; le rond, lui, ouvre le menu de la photo. Le rond est
/// pose PAR-DESSUS et intercepte le toucher (HitTestBehavior.opaque) : sans
/// cela, le client croirait appuyer sur son portrait et se retrouverait a
/// modifier son nom.
class _Identite extends ConsumerWidget {
  const _Identite({required this.profil});

  final Profil profil;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final theme = Theme.of(context);

    return HbaCard(
      padding: const EdgeInsets.symmetric(
        vertical: HbaSpacing.xl,
        horizontal: HbaSpacing.lg,
      ),
      onTap: () => ouvrirFormulaireProfil(context, profil),
      child: Column(
        children: [
          PhotoProfil(nom: profil.nom, aUnePhoto: profil.aUnePhoto),
          const SizedBox(height: HbaSpacing.md),
          Text(
            profil.nom.trim().isEmpty ? 'Votre compte' : profil.nom,
            style: theme.textTheme.titleLarge,
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 2),
          Text(
            profil.telephone,
            style:
                theme.textTheme.bodyMedium?.copyWith(color: HbaColors.inkMuted),
          ),
          const SizedBox(height: HbaSpacing.md),
          Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Icon(Icons.edit_outlined,
                  size: 16, color: HbaColors.primary),
              const SizedBox(width: HbaSpacing.xs),
              Text(
                'Modifier mes informations',
                style: theme.textTheme.bodyMedium
                    ?.copyWith(color: HbaColors.primary),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

/// L'invitation a renseigner un courriel.
///
/// CE QU'ELLE DIT EST CE QUE LE SYSTEME FAIT, ET RIEN DE PLUS. La version
/// precedente promettait des « recus de livraison » : il n'existe aucun canal
/// e-mail dans Notification — seulement SMS, WhatsApp et notification poussee —
/// et aucun recu de course nulle part. C'etait une phrase de ma main, reprise
/// telle quelle dans la maquette, et elle engageait le service sur une
/// fonction inexistante.
///
/// CETTE LIGNE CHANGERA QUAND LES RECUS EXISTERONT, et pas avant : c'est le
/// travail qui les construit qui la reecrira, pas un drapeau qu'on pense a
/// lever.
class _BanniereCourriel extends StatelessWidget {
  const _BanniereCourriel();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      padding: const EdgeInsets.all(HbaSpacing.md),
      decoration: BoxDecoration(
        color: HbaColors.warningSoft,
        borderRadius: BorderRadius.circular(HbaRadius.field),
        border: Border.all(color: HbaColors.warning.withValues(alpha: 0.35)),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Icon(Icons.info_outline, size: 20, color: HbaColors.warning),
          const SizedBox(width: HbaSpacing.sm),
          Expanded(
            child: Text(
              'Ajoutez un courriel : le support pourra vous joindre autrement '
              'que par téléphone.',
              style:
                  theme.textTheme.bodyMedium?.copyWith(color: HbaColors.warning),
            ),
          ),
        ],
      ),
    );
  }
}

/// CE QUI A ECHOUE, DIT PRECISEMENT.
///
/// « Impossible de charger votre profil » etait vrai et inutile : il couvrait
/// un telephone sans reseau, un jeton expire et un profil qui n'existe pas,
/// trois situations dont une seule se resout en tirant vers le bas.
///
/// LE BOUTON N'APPARAIT QUE SUR UN 404, et il ne fabrique rien : « POST /me »
/// ne prend aucun champ, le service relit le jeton qu'il a valide. C'est une
/// reparation — la fiche naît normalement de l'evenement d'inscription — pas
/// une seconde inscription.
class _Echec extends ConsumerStatefulWidget {
  const _Echec({required this.erreur});

  final Object erreur;

  @override
  ConsumerState<_Echec> createState() => _EchecState();
}

class _EchecState extends ConsumerState<_Echec> {
  bool _enCours = false;

  Future<void> _creer() async {
    setState(() => _enCours = true);

    try {
      final profil = await ref.read(profilRepositoryProvider).creer();
      ref.read(sessionProvider.notifier).renommer(profil.nom);
      ref.invalidate(profilProvider);
      ref.invalidate(adressesProvider);
    } on Object catch (erreur) {
      if (!mounted) return;
      setState(() => _enCours = false);
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            erreur is ApiException ? erreur.message : 'Création impossible.',
          ),
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final erreur = widget.erreur;

    final (icone, titre, detail) = switch (erreur) {
      OfflineException() => (
          Icons.wifi_off_outlined,
          'Pas de réseau',
          'Tirez vers le bas dès que la connexion revient.',
        ),
      ApiException(statusCode: 404) => (
          Icons.person_off_outlined,
          "Votre fiche client n'existe pas encore",
          "Elle aurait dû être créée à votre inscription. Vos livraisons, "
              "elles, fonctionnent. Créez-la ici : rien ne vous sera "
              "redemandé, tout est repris de votre compte.",
        ),
      ApiException(isUnauthorized: true) => (
          Icons.lock_outline,
          'Session expirée',
          'Reconnectez-vous pour retrouver votre profil.',
        ),
      ApiException(:final message) => (
          Icons.error_outline,
          'Profil indisponible',
          message,
        ),
      _ => (
          Icons.error_outline,
          'Profil indisponible',
          'Tirez vers le bas pour réessayer.',
        ),
    };

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icone, color: HbaColors.inkMuted),
          const SizedBox(width: HbaSpacing.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(titre, style: theme.textTheme.titleMedium),
                const SizedBox(height: 2),
                Text(
                  detail,
                  style: theme.textTheme.bodyMedium
                      ?.copyWith(color: HbaColors.inkMuted),
                ),
                if (erreur is ApiException && erreur.statusCode == 404) ...[
                  const SizedBox(height: HbaSpacing.md),
                  FilledButton(
                    onPressed: _enCours ? null : _creer,
                    child: Text(_enCours ? 'Création…' : 'Créer ma fiche'),
                  ),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// SE DECONNECTER SE CONFIRME. Le client se reconnecte par un code reçu par
/// message : s'il n'a pas de réseau au moment où il revient, il est dehors pour
/// un moment.
class _Deconnexion extends ConsumerWidget {
  @override
  Widget build(BuildContext context, WidgetRef ref) => OutlinedButton(
        onPressed: () => _confirmer(context, ref),
        style: OutlinedButton.styleFrom(
          minimumSize: const Size.fromHeight(HbaSpacing.cible),
          foregroundColor: HbaColors.inkMuted,
          side: const BorderSide(color: HbaColors.border),
        ),
        child: const Text('Se déconnecter'),
      );

  Future<void> _confirmer(BuildContext context, WidgetRef ref) async {
    final sortir = await showDialog<bool>(
      context: context,
      builder: (contexte) => AlertDialog(
        title: const Text('Se déconnecter ?'),
        content: const Text(
          'Vous devrez saisir un nouveau code reçu par message pour revenir.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(contexte).pop(false),
            child: const Text('Rester'),
          ),
          TextButton(
            onPressed: () => Navigator.of(contexte).pop(true),
            child: const Text('Se déconnecter'),
          ),
        ],
      ),
    );

    if (sortir ?? false) {
      await ref.read(sessionProvider.notifier).signOut();
    }
  }
}
