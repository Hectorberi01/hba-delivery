import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import '../../core/providers.dart';
import '../auth/session_controller.dart';

/// L'etat de la demande de suppression du compte.
///
/// « demandee » FAUX N'EST PAS UNE ABSENCE DE REPONSE. L'ecran doit pouvoir
/// distinguer « vous n'avez rien demande » de « nous n'avons pas pu verifier » :
/// sans ce booleen, une date vide dirait les deux a la fois.
class DemandeDeSuppression {
  const DemandeDeSuppression({
    required this.demandee,
    required this.echeance,
  });

  final bool demandee;
  final DateTime? echeance;

  static DemandeDeSuppression depuis(Map<String, dynamic> json) =>
      DemandeDeSuppression(
        demandee: json['requested'] == true,
        echeance: json['scheduledFor'] is String
            ? DateTime.tryParse(json['scheduledFor'] as String)?.toLocal()
            : null,
      );
}

class SuppressionRepository {
  SuppressionRepository(this._api);

  final ApiClient _api;

  Future<DemandeDeSuppression> lire() async =>
      DemandeDeSuppression.depuis(await _api.get('/me/account/deletion'));

  /// Demande la suppression. Le compte entre en sursis ; les sessions tombent.
  Future<DemandeDeSuppression> demander() async {
    // « delete » NE REND PAS LE CORPS, et il nous faut la date. On relit donc
    // juste apres — le compte existe encore, c'est tout l'interet du sursis.
    await _api.delete('/me/account');
    return lire();
  }

  Future<DemandeDeSuppression> annuler() async => DemandeDeSuppression.depuis(
        await _api.post(
          '/me/account/keep',
          body: const <String, Object?>{},
          idempotencyKey: ApiClient.newIdempotencyKey(),
        ),
      );
}

final suppressionRepositoryProvider = Provider<SuppressionRepository>(
  (ref) => SuppressionRepository(ref.watch(apiClientProvider)),
);

final suppressionProvider = FutureProvider<DemandeDeSuppression>(
  (ref) => ref.watch(suppressionRepositoryProvider).lire(),
);

/// Le bandeau qui ne parait QUE si une suppression est en cours.
///
/// IL EST EN HAUT DU PROFIL, ET C'EST LE MINIMUM. Un client qui a demande la
/// suppression est deconnecte sur-le-champ ; quand il revient, il atterrit sur
/// l'accueil, pas ici. Tant que rien ne l'accueille a la connexion, ce bandeau
/// est le seul endroit ou il apprendra que son compte est en sursis — et il
/// faut donc qu'il aille au profil pour le savoir. La suite du travail est de
/// le lui dire des la reconnexion.
class BandeauSuppression extends ConsumerWidget {
  const BandeauSuppression({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final demande = ref.watch(suppressionProvider).valueOrNull;

    if (demande == null || !demande.demandee) return const SizedBox.shrink();

    final theme = Theme.of(context);
    final quand = demande.echeance;

    return Padding(
      padding: const EdgeInsets.only(bottom: HbaSpacing.md),
      child: Container(
        padding: const EdgeInsets.all(HbaSpacing.md),
        decoration: BoxDecoration(
          color: HbaColors.dangerSoft,
          borderRadius: BorderRadius.circular(HbaRadius.field),
          border: Border.all(color: HbaColors.danger.withValues(alpha: 0.35)),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Icon(Icons.delete_forever_outlined,
                    size: 20, color: HbaColors.danger),
                const SizedBox(width: HbaSpacing.sm),
                Expanded(
                  child: Text(
                    quand == null
                        ? 'Votre compte est en cours de suppression.'
                        : 'Votre compte sera supprimé le ${_jour(quand)}.',
                    style: theme.textTheme.titleMedium
                        ?.copyWith(color: HbaColors.danger),
                  ),
                ),
              ],
            ),
            const SizedBox(height: HbaSpacing.sm),
            Text(
              "D'ici là, rien n'est perdu : vos courses, vos adresses et votre "
              'photo sont toujours là. Passé cette date, tout disparaît sans '
              'retour possible.',
              style: theme.textTheme.bodyMedium
                  ?.copyWith(color: HbaColors.inkMuted),
            ),
            const SizedBox(height: HbaSpacing.md),
            _Garder(),
          ],
        ),
      ),
    );
  }

  /// JJ/MM/AAAA, sans paquet de localisation.
  ///
  /// « intl » N'EST PAS DANS CETTE APPLICATION, et l'ajouter pour une date
  /// serait cher. Le format jour/mois/annee est celui du Benin, et il est ecrit
  /// ici une fois.
  static String _jour(DateTime date) =>
      '${date.day.toString().padLeft(2, '0')}/'
      '${date.month.toString().padLeft(2, '0')}/${date.year}';
}

class _Garder extends ConsumerStatefulWidget {
  @override
  ConsumerState<_Garder> createState() => _GarderState();
}

class _GarderState extends ConsumerState<_Garder> {
  bool _enCours = false;

  @override
  Widget build(BuildContext context) => FilledButton(
        onPressed: _enCours ? null : _annuler,
        style: FilledButton.styleFrom(
          minimumSize: const Size.fromHeight(HbaSpacing.cible),
        ),
        child: Text(_enCours ? 'Annulation…' : 'Garder mon compte'),
      );

  Future<void> _annuler() async {
    setState(() => _enCours = true);

    try {
      await ref.read(suppressionRepositoryProvider).annuler();
      ref.invalidate(suppressionProvider);
    } on ApiException catch (erreur) {
      _dire(erreur.message);
    } on OfflineException {
      _dire("Pas de réseau. Votre compte n'a pas été conservé — réessayez.");
    } on Object {
      _dire("L'annulation a échoué.");
    } finally {
      if (mounted) setState(() => _enCours = false);
    }
  }

  void _dire(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(message)));
  }
}

/// L'entree « Supprimer mon compte », en bas du profil.
///
/// ELLE EST DANS L'APPLICATION, ET C'EST UNE EXIGENCE, PAS UN CONFORT. Apple
/// demande depuis 2022 qu'une application permettant de creer un compte
/// permette d'en AMORCER la suppression depuis l'application elle-meme ;
/// renvoyer vers le support n'y suffit pas.
///
/// ELLE DISPARAIT QUAND UNE SUPPRESSION EST DEJA EN COURS : c'est le bandeau
/// qui parle alors, et proposer les deux ensemble ferait deux verites sur le
/// meme ecran.
class EntreeSuppression extends ConsumerWidget {
  const EntreeSuppression({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final demande = ref.watch(suppressionProvider).valueOrNull;

    if (demande?.demandee ?? false) return const SizedBox.shrink();

    return TextButton(
      onPressed: () => _demander(context, ref),
      style: TextButton.styleFrom(
        minimumSize: const Size.fromHeight(HbaSpacing.cible),
        foregroundColor: HbaColors.danger,
      ),
      child: const Text('Supprimer mon compte'),
    );
  }

  Future<void> _demander(BuildContext context, WidgetRef ref) async {
    // LE DIALOGUE DIT CE QUI PART ET CE QUI RESTE, SANS ADOUCIR NI DRAMATISER.
    // « Etes-vous sur ? » ne renseigne personne. Ce qui compte, c'est que les
    // courses restent chez HBA — elles concernent aussi des livreurs et des
    // destinataires — et que le reste disparait. C'est exactement ce que dit
    // la politique de confidentialite ; les deux textes ne doivent pas diverger.
    final sur = await showDialog<bool>(
      context: context,
      builder: (contexte) => AlertDialog(
        title: const Text('Supprimer votre compte ?'),
        content: const Text(
          'Votre profil, vos adresses enregistrées et votre photo seront '
          'supprimés dans 30 jours. Vous serez déconnecté tout de suite ; '
          'vous reconnecter avant cette date vous permettra de tout garder.\n\n'
          'Les courses déjà effectuées restent dans l\'historique de HBA : '
          'elles concernent aussi des livreurs et des destinataires.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(contexte).pop(false),
            child: const Text('Garder mon compte'),
          ),
          TextButton(
            onPressed: () => Navigator.of(contexte).pop(true),
            style: TextButton.styleFrom(foregroundColor: HbaColors.danger),
            child: const Text('Supprimer'),
          ),
        ],
      ),
    );

    if (!(sur ?? false) || !context.mounted) return;

    // LE MESSAGER EST PRIS AVANT L'ATTENTE, PAS APRES.
    //
    // La demande deconnecte le client : l'ecran Profil est demonte pendant
    // l'appel, et un ScaffoldMessenger.of(context) d'apres coup chercherait un
    // ancetre dans un arbre qui n'existe plus. C'est aussi ce que
    // « use_build_context_synchronously » signale — la regle a raison ici, elle
    // ne se contourne pas avec un « mounted » plus loin.
    final messager = ScaffoldMessenger.of(context);

    try {
      await ref.read(suppressionRepositoryProvider).demander();

      // LA DECONNEXION EST LOCALE, LE SERVEUR A DEJA REVOQUE LES SESSIONS.
      // Sans elle, l'application garderait un jeton d'acces valide encore
      // quinze minutes et le client croirait que rien ne s'est passe.
      await ref.read(sessionProvider.notifier).signOut();
    } on ApiException catch (erreur) {
      messager.showSnackBar(SnackBar(content: Text(erreur.message)));
    } on OfflineException {
      messager.showSnackBar(
        const SnackBar(
          content: Text("Pas de réseau. Votre demande n'a pas été envoyée."),
        ),
      );
    } on Object {
      messager.showSnackBar(
        const SnackBar(content: Text("La demande n'a pas pu être envoyée.")),
      );
    }
  }
}
