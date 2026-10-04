import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import '../deliveries/carte_du_livreur.dart';
import '../deliveries/suivi_temps_reel.dart';
import 'course_models.dart';
import 'course_providers.dart';

/// Suivi d'une course de personnes.
///
/// LE MEME MELANGE QUE POUR UNE LIVRAISON : un sondage qui dit la verite, et un
/// flux en direct qui la dit plus tot. Le service de diffusion consomme Kafka au
/// PLUS RECENT et ne rejoue rien — un changement d'etat emis pendant une coupure
/// est perdu pour toujours. Le direct est donc un confort, le sondage est la
/// verite, et supprimer le second parce que le premier marche echangerait un
/// ecran lent contre un ecran faux.
class SuiviCourseScreen extends ConsumerStatefulWidget {
  const SuiviCourseScreen({required this.courseId, super.key});

  final String courseId;

  @override
  ConsumerState<SuiviCourseScreen> createState() => _SuiviCourseScreenState();
}

class _SuiviCourseScreenState extends ConsumerState<SuiviCourseScreen> {
  /// Trente secondes, comme pour les livraisons : c'est aussi ce qui fait
  /// vieillir l'age de la position affiche sous la carte.
  static const _cadenceDeSondage = Duration(seconds: 30);

  Timer? _sondage;

  @override
  void initState() {
    super.initState();

    _sondage = Timer.periodic(
      _cadenceDeSondage,
      (_) => ref.invalidate(courseProvider(widget.courseId)),
    );

    // LE SIGNAL DU SERVEUR RELIT LA COURSE, IL NE LA REMPLACE PAS. La trame
    // « etat » dit « ceci a change » et ne porte ni le nom du chauffeur ni son
    // telephone : la matrice de visibilite s'applique dans les services et a la
    // passerelle, jamais dans une trame.
    //
    // APRES LA CONSTRUCTION, PAS PENDANT : lire un notificateur dans initState
    // declencherait une modification d'etat au premier cadre.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;

      ref.read(suiviCourseProvider(widget.courseId).notifier).surChangementDEtat = () {
        if (mounted) ref.invalidate(courseProvider(widget.courseId));
      };
    });
  }

  @override
  void dispose() {
    _sondage?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final async = ref.watch(courseProvider(widget.courseId));

    return Scaffold(
      appBar: AppBar(title: const Text('Ma course')),
      body: async.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (erreur, _) => Center(
          child: Padding(
            padding: const EdgeInsets.all(HbaSpacing.gutter),
            child: Text(
              erreur is ApiException ? erreur.message : 'Suivi indisponible.',
              textAlign: TextAlign.center,
              style: theme.textTheme.bodyMedium,
            ),
          ),
        ),
        data: (course) => ListView(
          padding: const EdgeInsets.all(HbaSpacing.gutter),
          children: [
            // LA CARTE EN PREMIER, ET SEULEMENT QUAND IL Y A QUELQU'UN A SUIVRE.
            // Avant l'affectation, le hub n'envoie aucune position : la carte
            // s'effacerait d'elle-meme, mais la demander couterait le chargement
            // d'une vue native pour rien.
            if (course.etat.aUnChauffeurASuivre)
              CarteDuLivreur(
                depart: _point(course.departLatitude, course.departLongitude),
                arrivee: _point(
                  course.destinationLatitude,
                  course.destinationLongitude,
                ),
                vehicule: course.chauffeur?.vehicule ?? course.vehicule,
                suivi: ref.watch(suiviCourseProvider(widget.courseId)),
              ),
            const SizedBox(height: HbaSpacing.md),
            Row(
              children: [
                Expanded(
                  child: Text(course.etat.libelle, style: theme.textTheme.titleMedium),
                ),
                HbaChip(
                  label: Xof.format(course.prixXof),
                  tone: HbaChipTone.primary,
                ),
              ],
            ),
            if (course.etat.consigne.isNotEmpty) ...[
              const SizedBox(height: HbaSpacing.xs),
              Text(course.etat.consigne, style: theme.textTheme.bodyMedium),
            ],
            if (course.motifDAnnulation case final motif?) ...[
              const SizedBox(height: HbaSpacing.sm),
              Text(
                'Motif : $motif',
                style: theme.textTheme.bodySmall?.copyWith(color: HbaColors.inkMuted),
              ),
            ],
            const SizedBox(height: HbaSpacing.md),

            // LE CHAUFFEUR N'APPARAIT QU'UNE FOIS AFFECTE. Une carte vide avec
            // un nom manquant se lit comme une panne ; son absence, elle,
            // s'explique par l'etat affiche juste au-dessus.
            if (course.chauffeur case final chauffeur?) ...[
              HbaCard(
                padding: const EdgeInsets.all(HbaSpacing.lg),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('Votre chauffeur', style: theme.textTheme.bodySmall),
                    const SizedBox(height: HbaSpacing.xs),
                    Text(chauffeur.nom, style: theme.textTheme.titleMedium),
                    const SizedBox(height: HbaSpacing.xs),
                    Text(chauffeur.description, style: theme.textTheme.bodyMedium),
                    if (chauffeur.telephone.isNotEmpty) ...[
                      const SizedBox(height: HbaSpacing.xs),
                      Text(chauffeur.telephone, style: theme.textTheme.bodyMedium),
                    ],
                  ],
                ),
              ),
              const SizedBox(height: HbaSpacing.md),
            ],

            _Trajet(course: course),
            const SizedBox(height: HbaSpacing.lg),
            if (course.etat.peutEtreAnnulee)
              HbaButton(
                label: 'Annuler la course',
                tone: HbaButtonTone.neutral,
                onPressed: () => unawaited(annulerLaDemandeDeCourse(context, ref, course)),
              ),
          ],
        ),
      ),
    );
  }

  /// ZERO N'EST PAS UNE COORDONNEE MANQUANTE, C'EST UNE COORDONNEE — mais la
  /// passerelle rend « 0 » quand le point est absent du message. Le distinguer
  /// evite une epingle au large du golfe de Guinee.
  static LatLng? _point(double latitude, double longitude) =>
      latitude == 0 && longitude == 0 ? null : LatLng(latitude, longitude);
}

/// Les deux bouts du trajet, et ce qu'on en sait.
class _Trajet extends StatelessWidget {
  const _Trajet({required this.course});

  final Course course;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Trajet', style: theme.textTheme.bodySmall),
          const SizedBox(height: HbaSpacing.xs),
          Text(
            VehiculeDeCourse.libelleDe(course.vehicule),
            style: theme.textTheme.bodyMedium,
          ),
          if (course.distanceEstimeeMetres > 0) ...[
            const SizedBox(height: HbaSpacing.xs),
            Text(
              '${_distance(course.distanceEstimeeMetres)} environ',
              style: theme.textTheme.bodyMedium,
            ),
          ],
          if (course.demandeeLe case final quand?) ...[
            const SizedBox(height: HbaSpacing.xs),
            Text(
              'Commandée à ${_heure(quand)}',
              style: theme.textTheme.bodySmall?.copyWith(color: HbaColors.inkMuted),
            ),
          ],
        ],
      ),
    );
  }

  static String _distance(int metres) => metres < 1000
      ? '$metres m'
      : '${(metres / 1000).toStringAsFixed(1).replaceAll('.', ',')} km';

  static String _heure(DateTime quand) =>
      '${quand.hour.toString().padLeft(2, '0')}h${quand.minute.toString().padLeft(2, '0')}';
}

/// Demande confirmation, puis annule la course.
///
/// LE MONTANT EST ANNONCE DEPUIS LE 2 OCTOBRE 2026. Le dialogue disait « les
/// conditions de remboursement vous seront confirmees par le service » — une
/// promesse que personne ne tenait, faute de politique d'annulation (point 3).
/// Elle est tranchee, et le service envoie ce qui serait retenu.
///
/// LA REGLE N'EST PAS REJOUEE ICI : « retenueAnnulation » est le resultat du meme
/// appel qui remplit l'evenement d'annulation cote service. L'ecran ne decide pas
/// si un chauffeur est affecte, ni combien la grille retient.
///
/// PUBLIQUE DEPUIS LE 3 OCTOBRE 2026, parce que l'accueil annule aussi. Le radar
/// de recherche s'y affiche desormais apres le paiement, et son bouton doit
/// ouvrir LE MEME dialogue : deux confirmations differentes pour le meme geste
/// finiraient par annoncer deux retenues differentes.
Future<void> annulerLaDemandeDeCourse(
  BuildContext context,
  WidgetRef ref,
  Course course,
) async {
  // LE MESSAGER EST PRIS AVANT L'ATTENTE : apres le dialogue, le contexte peut
  // avoir disparu, et le chercher alors est la faute que l'analyseur signale
  // sous « use_build_context_synchronously ».
  final messager = ScaffoldMessenger.of(context);

  final confirme = await showDialog<bool>(
    context: context,
    builder: (contexte) => AlertDialog(
      title: const Text('Annuler la course ?'),

      // ZERO N'EST PAS « INCONNU », C'EST « GRATUIT », et les deux phrases disent
      // des choses opposees. Le cas gratuit est le plus frequent — aucun chauffeur
      // affecte — et c'est une bonne nouvelle a annoncer, pas un silence a garder.
      content: Text(
        course.retenueAnnulation > 0
            ? 'Le chauffeur ne viendra pas. Il est déjà en route : '
                '${Xof.format(course.retenueAnnulation)} seront retenus pour son '
                'déplacement, le reste vous sera remboursé.'
            : "Le chauffeur ne viendra pas. L'annulation est gratuite et vous "
                'serez remboursé intégralement.',
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(contexte).pop(false),
          child: const Text('Garder'),
        ),
        TextButton(
          onPressed: () => Navigator.of(contexte).pop(true),
          child: const Text('Annuler la course'),
        ),
      ],
    ),
  );

  if (confirme != true) return;

  try {
    await ref
        .read(courseRepositoryProvider)
        .annuler(course.id, motif: 'Annulée par le passager');

    ref.invalidate(courseProvider(course.id));
    ref.invalidate(coursesActivesProvider);
    ref.read(courseEnAttenteDePaiementProvider.notifier).state = null;
  } on OfflineException {
    // LE MESSAGE DIT CE QUI N'A PAS EU LIEU, pas ce qui s'est mal passe. « Pas
    // de reseau » laisserait croire a un probleme d'affichage ; ce qu'il faut
    // comprendre, c'est que la course EST TOUJOURS EN COURS et que le chauffeur
    // roule vers le passager.
    messager.showSnackBar(
      const SnackBar(
        content: Text(
          "Pas de réseau : la course n'est PAS annulée. Elle est toujours en "
          'cours — réessayez.',
        ),
      ),
    );
  } on ApiException catch (erreur) {
    messager.showSnackBar(SnackBar(content: Text(erreur.message)));
  } on Object {
    // MEME RAISON POUR TOUT LE RESTE : appelee en « unawaited », cette fonction
    // n'a personne au-dessus d'elle, et ce qui n'est pas attrape ici ne l'est
    // nulle part. Le message reste du cote prudent.
    messager.showSnackBar(
      const SnackBar(
        content: Text(
          "La course n'a pas pu être annulée. Elle est toujours en cours — "
          'réessayez.',
        ),
      ),
    );
  }
}
