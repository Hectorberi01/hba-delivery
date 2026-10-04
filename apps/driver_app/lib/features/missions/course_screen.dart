import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import '../../core/navigation_externe.dart';
import 'carte_course.dart';
import 'course_models.dart';
import 'mission_providers.dart';

/// La course de personnes, conduite par le chauffeur.
///
/// QUATRE GESTES, UN A LA FOIS, ET JAMAIS DEUX BOUTONS QUI AVANCENT. Le chauffeur
/// conduit : l'ecran doit tenir dans un coup d'oeil a un feu rouge. L'etat du
/// serveur decide quel geste existe — « Je suis en route », « Je suis arrive »,
/// « Demarrer », « Terminer » — et les autres n'apparaissent pas, plutot que de se
/// griser. Un bouton grise se lit « plus tard » ; ici il n'y a rien a faire avec
/// lui, jamais.
///
/// PAS D'OTP, PAS DE PREUVE, PAS DE PHOTO. Un passager n'est pas un colis : il est
/// la, il monte, il descend. Le point 35 n'a pose aucune preuve de fin de course,
/// et en inventer une ici l'inscrirait dans l'interface avant que personne ne
/// l'ait decidee.
///
/// LE PRIX N'EST PAS AFFICHE COMME UN GAIN. Ride porte ce que le PASSAGER paie ;
/// la part du chauffeur est calculee par Pricing puis par Billing. L'ecrire sur
/// cet ecran serait un mensonge, et les gains ont leur onglet, qui dit la verite.
class CourseScreen extends ConsumerStatefulWidget {
  const CourseScreen({
    required this.course,
    required this.onClosed,
    this.position,
    this.vehicule,
    super.key,
  });

  final CourseLivreur course;

  /// Appele quand la course quitte l'etat ouvert : l'accueil cesse alors de la
  /// montrer et le sondage des offres repart.
  final VoidCallback onClosed;

  /// Position du chauffeur, quand l'accueil la connait.
  final LatLng? position;

  /// Son type de vehicule, pour le pictogramme de l'epingle.
  final String? vehicule;

  @override
  ConsumerState<CourseScreen> createState() => _CourseScreenState();
}

class _CourseScreenState extends ConsumerState<CourseScreen> {
  late CourseLivreur _course = widget.course;
  bool _envoi = false;
  String? _notice;

  /// Avance d'une etape.
  ///
  /// L'ECRAN NE DEVINE PAS L'ETAT SUIVANT, IL PREND CELUI DU SERVEUR. Poser
  /// l'etat localement ferait avancer l'ecran sur une action que le service a
  /// peut-etre refusee — et le chauffeur attendrait un passager qui n'existe pas.
  Future<void> _avancer(EtapeDeCourse etape) async {
    setState(() {
      _envoi = true;
      _notice = null;
    });

    try {
      final issue =
          await ref.read(missionRepositoryProvider).avancerLaCourse(_course.id, etape);

      if (!mounted) return;

      if (issue.miseEnFile) {
        // MISE EN FILE : on ne sait pas ce que le serveur en pense, donc on ne
        // fait pas avancer l'ecran. Le dire est la seule honnetete possible.
        setState(() => _notice =
            "Pas de réseau. L'étape partira dès que le réseau revient.");
        return;
      }

      final course = issue.course;
      if (course == null) return;

      setState(() => _course = course);

      if (!course.etat.estOuverte) {
        widget.onClosed();
        if (mounted) Navigator.of(context).maybePop();
      }
    } on ApiException catch (erreur) {
      if (mounted) setState(() => _notice = erreur.message);
    } on Object {
      // APPELE EN « unawaited », DONC PERSONNE AU-DESSUS. Ce qui n'est pas
      // attrape ici ne l'est nulle part : l'imprevu doit sortir par une phrase,
      // et cette phrase reste du cote prudent — l'etape n'a pas ete declaree.
      if (mounted) {
        setState(() => _notice =
            "L'étape n'a pas pu être déclarée. Réessayez dans un instant.");
      }
    } finally {
      if (mounted) setState(() => _envoi = false);
    }
  }

  Future<void> _annuler() async {
    final messager = ScaffoldMessenger.of(context);

    final confirme = await showDialog<bool>(
      context: context,
      builder: (contexte) => AlertDialog(
        title: const Text('Annuler la course ?'),
        content: const Text(
          'Le passager sera prévenu que vous ne venez pas. Annuler une course '
          'acceptée peut compter dans votre dossier.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(contexte).pop(false),
            child: const Text('Garder'),
          ),
          TextButton(
            onPressed: () => Navigator.of(contexte).pop(true),
            child: const Text('Annuler'),
          ),
        ],
      ),
    );

    if (confirme != true) return;

    try {
      final issue = await ref
          .read(missionRepositoryProvider)
          .annulerLaCourse(_course.id, motif: 'Annulée par le chauffeur');

      if (!mounted) return;

      if (issue.miseEnFile) {
        messager.showSnackBar(
          const SnackBar(
            content: Text(
              "Pas de réseau : la course n'est PAS annulée. Le passager vous "
              'attend toujours — réessayez.',
            ),
          ),
        );
        return;
      }

      widget.onClosed();
      if (mounted) Navigator.of(context).maybePop();
    } on ApiException catch (erreur) {
      messager.showSnackBar(SnackBar(content: Text(erreur.message)));
    } on Object {
      messager.showSnackBar(
        const SnackBar(
          content: Text(
            "La course n'a pas pu être annulée. Elle est toujours la vôtre — "
            'réessayez.',
          ),
        ),
      );
    }
  }

  /// Ou le chauffeur doit aller MAINTENANT : le depart avant le passager, la
  /// destination une fois a bord.
  (double, double, String)? get _prochainPoint {
    if (_course.etat == EtatDeCourse.enCours) {
      return _course.aUneDestination
          ? (_course.destinationLatitude, _course.destinationLongitude, 'Destination')
          : null;
    }

    return _course.aUnDepart
        ? (_course.departLatitude, _course.departLongitude, 'Départ du passager')
        : null;
  }

  Future<void> _yAller() async {
    final cible = _prochainPoint;
    if (cible == null) return;

    final ouvert = await ouvrirItineraire(
      latitude: cible.$1,
      longitude: cible.$2,
      etiquette: cible.$3,
    );

    // UN BOUTON QUI NE FAIT RIEN LAISSE LE CHAUFFEUR A L'ARRET SANS COMPRENDRE.
    if (!ouvert && mounted) {
      setState(() => _notice = "Aucune application de navigation n'a pu s'ouvrir.");
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final etape = _course.etat.etapeSuivante;
    final cible = _prochainPoint;

    return Scaffold(
      appBar: AppBar(title: const Text('Course')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.all(HbaSpacing.gutter),
          children: [
            if (_course.aUnDepart)
              CarteCourse(
                collecte: LatLng(_course.departLatitude, _course.departLongitude),
                depot: _course.aUneDestination
                    ? LatLng(
                        _course.destinationLatitude,
                        _course.destinationLongitude,
                      )
                    : null,
                moi: widget.position,
                vehicule: widget.vehicule,
              ),
            const SizedBox(height: HbaSpacing.md),
            Row(
              children: [
                Expanded(
                  child: Text(
                    _course.etat.libelle,
                    style: theme.textTheme.titleMedium,
                  ),
                ),
                HbaChip(
                  label: _distance(_course.distanceEstimeeMetres),
                  tone: HbaChipTone.neutral,
                ),
              ],
            ),
            if (_course.motifDAnnulation case final motif?) ...[
              const SizedBox(height: HbaSpacing.sm),
              Text(
                'Motif : $motif',
                style: theme.textTheme.bodySmall?.copyWith(color: HbaColors.inkMuted),
              ),
            ],
            if (_notice != null) ...[
              const SizedBox(height: HbaSpacing.sm),
              Text(
                _notice!,
                style: theme.textTheme.bodyMedium?.copyWith(color: HbaColors.danger),
              ),
            ],
            const SizedBox(height: HbaSpacing.md),

            // « Y ALLER » AVANT LE GESTE D'AVANCEMENT. Le chauffeur a d'abord
            // besoin de rouler ; declarer vient ensuite.
            if (cible != null) ...[
              HbaButton(
                label: 'Y aller',
                tone: HbaButtonTone.neutral,
                icon: Icons.navigation_outlined,
                onPressed: _yAller,
              ),
              const SizedBox(height: HbaSpacing.sm),
            ],
            if (etape != null) ...[
              HbaCard(
                padding: const EdgeInsets.all(HbaSpacing.md),
                child: Text(etape.explication, style: theme.textTheme.bodyMedium),
              ),
              const SizedBox(height: HbaSpacing.sm),

              // LE DEMARRAGE ET LA FIN SE GLISSENT, LES DEUX AUTRES S'APPUIENT.
              // Demarrer engage le prix et la fin cloture la course : ce sont
              // les deux gestes qu'un telephone secoue dans une poche ne doit
              // pas pouvoir declencher. Les deux premiers ne font que prevenir
              // le passager, et se reprennent.
              if (etape == EtapeDeCourse.demarrer || etape == EtapeDeCourse.terminer)
                HbaGlissiere(
                  libelle: etape.libelle,
                  libelleConfirme: 'Envoi...',
                  busy: _envoi,
                  onConfirme: () => unawaited(_avancer(etape)),
                )
              else
                HbaButton(
                  label: etape.libelle,
                  busy: _envoi,
                  onPressed: () => unawaited(_avancer(etape)),
                ),
              const SizedBox(height: HbaSpacing.sm),
            ],
            if (_course.etat.peutEtreAnnulee)
              HbaButton(
                label: 'Annuler la course',
                tone: HbaButtonTone.neutral,
                onPressed: _envoi ? null : () => unawaited(_annuler()),
              ),
          ],
        ),
      ),
    );
  }

  static String _distance(int metres) => metres < 1000
      ? '$metres m'
      : '${(metres / 1000).toStringAsFixed(1).replaceAll('.', ',')} km';
}
