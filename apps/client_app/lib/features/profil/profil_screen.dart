import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import '../auth/session_controller.dart';
import 'adresses.dart';
import 'bloc_adresses.dart';
import 'formulaire_profil.dart';
import 'profil_providers.dart';
import 'support.dart';
import 'tuile.dart';
import 'whatsapp.dart';

/// Le compte du client.
///
/// PAS DE FLECHE DE RETOUR, ET CE N'EST PAS UN OUBLI DE LA MAQUETTE. Le profil
/// est un ONGLET de la navigation en arc : il n'y a rien derriere lui. Une
/// fleche qui ne ramene nulle part — ou qui ramene a l'accueil alors que
/// l'accueil est un autre onglet — apprend au client que ce bouton ment.
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
          },
          child: ListView(
            padding: const EdgeInsets.fromLTRB(
              HbaSpacing.gutter,
              HbaSpacing.md,
              HbaSpacing.gutter,
              HbaSpacing.xxl,
            ),
            children: [
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
            ],
          ),
        ),
      ),
    );
  }
}

/// La carte d'identite : avatar, nom, telephone, et le geste pour modifier.
///
/// L'AVATAR PORTE LES INITIALES, PAS UNE SILHOUETTE GRISE. Une silhouette est
/// la meme pour tout le monde ; deux lettres disent a qui appartient le compte,
/// ce qui compte sur un telephone qu'on prete.
///
/// LE BADGE APPAREIL-PHOTO DE LA MAQUETTE N'EST PAS ENCORE LA. Le depot d'une
/// photo demande une colonne dans Directory, une route, un bucket et une regle
/// de visibilite : il arrive avec ce travail-la, pas avant. Un badge qui
/// n'ouvre rien serait pire que pas de badge.
class _Identite extends ConsumerWidget {
  const _Identite({required this.profil});

  final Profil profil;

  static String initiales(String nom) {
    final mots = nom
        .trim()
        .split(RegExp(r'\s+'))
        .where((m) => m.isNotEmpty)
        .toList();

    // SUBSTRING(0, 1) ET NON [0] : un nom peut commencer par un caractere
    // accentue, et les deux donnent ici le meme resultat — mais substring dit
    // ce qu'on veut, un prefixe, la ou l'index dit une unite de code.
    String premiere(String mot) => mot.substring(0, 1).toUpperCase();

    if (mots.isEmpty) return '?';
    if (mots.length == 1) return premiere(mots.first);

    return premiere(mots.first) + premiere(mots.last);
  }

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
          Container(
            height: 92,
            width: 92,
            alignment: Alignment.center,
            decoration: const BoxDecoration(
              color: HbaColors.primarySoft,
              shape: BoxShape.circle,
            ),
            child: Text(
              initiales(profil.nom),
              style: theme.textTheme.headlineSmall?.copyWith(
                color: HbaColors.primaryInk,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
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
