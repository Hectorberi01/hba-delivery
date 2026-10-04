import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_ui/hba_ui.dart';

import '../rides/course_models.dart';
import '../rides/course_providers.dart';
import 'delivery_providers.dart';
import 'models.dart';

/// Les courses du client : celles qui tournent, puis celles qui sont closes.
///
/// CET ECRAN EXISTAIT DEJA, EN BAS DE L'ACCUEIL. Il en sort parce que
/// l'accueil devient une carte : une liste et une carte plein ecran ne
/// cohabitent pas sans que l'une des deux soit a l'etroit.
///
/// DEUX SECTIONS, PAS DEUX ONGLETS. « En cours » tient en une ou deux lignes
/// la plupart du temps ; lui donner un onglet obligerait le client a chercher
/// derriere un deuxieme geste ce qu'il vient voir en premier.
///
/// IL MONTRE LES DEUX NATURES DEPUIS LE 2 OCTOBRE 2026 — colis ET courses de
/// personnes, chacune avec sa pastille (point 39, option C).
///
/// CE QUI A EMPORTE LA DECISION N'EST PAS LE VOCABULAIRE. Le mot « course »
/// designait un passager dans le referentiel et un colis dans cet onglet ; les
/// deux autres options reglaient l'ambiguite et laissaient le vrai trou intact :
/// un passager n'avait AUCUN historique. Sa course s'affichait sur l'accueil et
/// disparaissait en se terminant. Cet ecran est ce qui le lui rend.
///
/// DEUX SOURCES, ET AUCUNE NE BLOQUE L'AUTRE. Les livraisons et les courses
/// viennent de deux services et de deux appels ; une seule attente commune
/// ferait dependre l'affichage des colis de la sante de Ride, et inversement.
/// Chaque liste arrive quand elle arrive, et un echec d'un cote n'efface pas
/// l'autre — il se signale.
class CoursesScreen extends ConsumerWidget {
  const CoursesScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final colis = ref.watch(deliveriesProvider);
    final courses = ref.watch(historiqueDesCoursesProvider);
    final coursesActives = ref.watch(coursesActivesProvider);

    return Scaffold(
      appBar: AppBar(title: const Text('Mes courses')),
      body: SafeArea(
        child: RefreshIndicator(
          onRefresh: () async {
            ref
              ..invalidate(deliveriesProvider)
              ..invalidate(historiqueDesCoursesProvider)
              ..invalidate(coursesActivesProvider);
          },
          child: _Contenu(
            colis: colis,
            courses: courses,
            coursesActives: coursesActives,
          ),
        ),
      ),
    );
  }
}

/// Assemble ce qui est arrive, et dit ce qui manque.
///
/// ON N'ATTEND PAS QUE TOUT SOIT LA. Trois appels, trois etats : montrer une
/// roue tant que le dernier n'a pas repondu cacherait des colis deja charges
/// parce que Ride est lent. Seul le cas « rien du tout, et rien n'a encore
/// repondu » merite une attente.
///
/// UN ECHEC NE VIDE PAS LA LISTE, IL S'ANNONCE. Avant, l'erreur remplacait tout
/// l'ecran ; avec deux sources, cela ferait disparaitre des courses saines a
/// cause de l'autre service. Le bandeau dit ce qui manque, le reste s'affiche.
class _Contenu extends StatelessWidget {
  const _Contenu({
    required this.colis,
    required this.courses,
    required this.coursesActives,
  });

  final AsyncValue<List<Delivery>> colis;
  final AsyncValue<List<Course>> courses;
  final AsyncValue<List<Course>> coursesActives;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    final missions = <_Mission>[
      for (final c in colis.valueOrNull ?? const <Delivery>[]) _Mission.duColis(c),

      // LES ACTIVES D'ABORD, L'HISTORIQUE ENSUITE, ET LES DOUBLONS SONT ECARTES
      // JUSTE APRES. Les deux routes se recouvrent : une course en cours peut
      // figurer dans les deux selon le moment de l'appel.
      for (final c in coursesActives.valueOrNull ?? const <Course>[])
        _Mission.deLaCourse(c),
      for (final c in courses.valueOrNull ?? const <Course>[])
        _Mission.deLaCourse(c),
    ];

    final vues = <String>{};
    final uniques = [
      for (final m in missions)
        if (vues.add('${m.estUneCourse}:${m.id}')) m,
    ];

    final enCours = uniques.where((m) => !m.estClose).toList()..sort(_duPlusRecent);
    final closes = uniques.where((m) => m.estClose).toList()..sort(_duPlusRecent);

    final manquant = [
      if (colis.hasError) 'vos colis',
      if (courses.hasError || coursesActives.hasError) 'vos courses',
    ];

    final charge = colis.isLoading && courses.isLoading && coursesActives.isLoading;

    if (charge && uniques.isEmpty && manquant.isEmpty) {
      return const Center(child: CircularProgressIndicator());
    }

    return ListView(
      padding: const EdgeInsets.all(HbaSpacing.gutter),
      children: [
        if (manquant.isNotEmpty) ...[
          HbaCard(
            padding: const EdgeInsets.all(HbaSpacing.lg),
            child: Text(
              'Impossible de charger ${manquant.join(" et ")}. Tirez vers le bas '
              'pour réessayer.',
              style: theme.textTheme.bodyMedium,
            ),
          ),
          const SizedBox(height: HbaSpacing.md),
        ],
        if (uniques.isEmpty && manquant.isEmpty) ...[
          const SizedBox(height: HbaSpacing.xxl),
          const Icon(Icons.local_shipping_outlined,
              size: 48, color: HbaColors.inkFaint),
          const SizedBox(height: HbaSpacing.md),
          Text(
            "Vous n'avez encore ni colis ni course.",
            textAlign: TextAlign.center,
            style: theme.textTheme.titleMedium,
          ),
          const SizedBox(height: HbaSpacing.xs),
          Text(
            "Vos envois et vos trajets s'afficheront ici, en cours puis terminés.",
            textAlign: TextAlign.center,
            style: theme.textTheme.bodyMedium
                ?.copyWith(color: HbaColors.inkMuted),
          ),
        ],
        if (enCours.isNotEmpty) ...[
          Text('EN COURS', style: theme.textTheme.bodySmall),
          const SizedBox(height: HbaSpacing.sm),
          for (final mission in enCours) ...[
            _CarteDeMission(mission: mission, enAvant: true),
            const SizedBox(height: HbaSpacing.sm),
          ],
          const SizedBox(height: HbaSpacing.md),
        ],
        if (closes.isNotEmpty) ...[
          Text('TERMINÉES', style: theme.textTheme.bodySmall),
          const SizedBox(height: HbaSpacing.sm),
          for (final mission in closes) ...[
            _CarteDeMission(mission: mission, enAvant: false),
            const SizedBox(height: HbaSpacing.sm),
          ],
        ],
      ],
    );
  }

  /// LA PLUS RECENTE EN HAUT, ET LES SANS-DATE EN BAS. Une date absente ne doit
  /// pas remonter en tete : elle serait traitee comme l'epoque Unix et passerait
  /// devant une course d'il y a dix minutes.
  static int _duPlusRecent(_Mission a, _Mission b) {
    if (a.date == null && b.date == null) return 0;
    if (a.date == null) return 1;
    if (b.date == null) return -1;
    return b.date!.compareTo(a.date!);
  }
}

/// Ce qu'une liste a besoin de savoir d'une mission, quelle que soit sa nature.
///
/// UNE PROJECTION, PAS UNE ABSTRACTION DE PLUS. Delivery et Course viennent de
/// deux services, portent des champs differents et n'ont aucune raison de
/// converger : leur donner une interface commune ferait remonter dans les deux
/// modeles une contrainte qui n'appartient qu'a cet ecran. Ce type-ci ne vit que
/// le temps d'un affichage.
class _Mission {
  const _Mission({
    required this.id,
    required this.route,
    required this.estUneCourse,
    required this.libelleDEtat,
    required this.ton,
    required this.montantXof,
    required this.lieu,
    required this.reference,
    required this.date,
    required this.estClose,
  });

  factory _Mission.duColis(Delivery colis) => _Mission(
        id: colis.id,
        route: '/suivi/${colis.id}',
        estUneCourse: false,
        libelleDEtat: colis.status.label,
        ton: _tonDuColis(colis.status),
        montantXof: colis.totalXof,
        lieu: colis.dropoffLandmark,
        reference: colis.reference,
        date: colis.createdAt,
        estClose: colis.status.isClosed,
      );

  factory _Mission.deLaCourse(Course course) => _Mission(
        id: course.id,
        route: '/course/${course.id}',
        estUneCourse: true,
        libelleDEtat: course.etat.libelle,
        ton: _tonDeLaCourse(course.etat),
        montantXof: course.prixXof,

        // UNE COURSE N'A PAS DE REPERE DE DESTINATION : le passager a pose un
        // point sur une carte, pas un nom de lieu. La carte de la liste reste
        // donc muette sur l'adresse plutot que d'afficher des coordonnees, que
        // personne ne lit.
        lieu: '',

        // NI DE REFERENCE LISIBLE : Ride n'en attribue pas, contrairement a
        // Delivery. Nulle plutot que vide — la carte saute la ligne au lieu de
        // montrer une icone sans texte.
        reference: null,
        date: course.demandeeLe,
        estClose: course.etat.estClose,
      );

  final String id;
  final String route;
  final bool estUneCourse;
  final String libelleDEtat;
  final HbaChipTone ton;
  final int montantXof;
  final String lieu;
  final String? reference;
  final DateTime? date;
  final bool estClose;

  /// LA COULEUR DIT L'ISSUE, PAS L'ETAPE. Vert : c'est arrive. Rouge : c'est
  /// perdu, et le client doit le voir sans lire. Orange : c'est en cours, quel
  /// que soit le cran — distinguer huit crans par huit teintes ne ferait
  /// qu'obliger a apprendre un code.
  static HbaChipTone _tonDuColis(DeliveryStatus statut) => switch (statut) {
        DeliveryStatus.delivered => HbaChipTone.success,
        DeliveryStatus.cancelled ||
        DeliveryStatus.failed ||
        DeliveryStatus.noDriverFound ||
        DeliveryStatus.paymentFailed =>
          HbaChipTone.danger,
        DeliveryStatus.unknown => HbaChipTone.neutral,
        _ => HbaChipTone.primary,
      };

  /// MEME CODE COULEUR QUE LES COLIS, ET C'EST TOUT L'INTERET DE LES MELANGER :
  /// deux cartes voisines dans la meme liste ne peuvent pas se lire avec deux
  /// conventions differentes.
  static HbaChipTone _tonDeLaCourse(EtatDeCourse etat) => switch (etat) {
        EtatDeCourse.terminee => HbaChipTone.success,
        EtatDeCourse.annulee ||
        EtatDeCourse.aucunChauffeur ||
        EtatDeCourse.paiementEchoue =>
          HbaChipTone.danger,
        EtatDeCourse.inconnu => HbaChipTone.neutral,
        _ => HbaChipTone.primary,
      };
}

/// Une mission dans une liste, colis ou course.
///
/// PRIVEE, CONTRAIREMENT A CarteCourse : elle porte un _Mission, qui ne sort pas
/// de ce fichier. Une classe publique exposant un type prive est precisement ce
/// que signale « library_private_types_in_public_api ».
class _CarteDeMission extends StatelessWidget {
  const _CarteDeMission({required this.mission, required this.enAvant});

  final _Mission mission;
  final bool enAvant;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      highlighted: enAvant,
      onTap: () => context.push(mission.route),
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // LA NATURE EN PREMIER, PUIS L'ETAT, PUIS LE PRIX.
          //
          // L'ICONE PLUTOT QU'UNE DEUXIEME PASTILLE : deux pastilles cote a cote
          // se lisent comme deux etats, et c'est l'etat qui porte deja la
          // couleur. L'icone dit la nature sans entrer en concurrence avec elle.
          Row(
            children: [
              Icon(
                mission.estUneCourse
                    ? Icons.person_outline
                    : Icons.inventory_2_outlined,
                size: 18,
                color: HbaColors.inkMuted,
                semanticLabel: mission.estUneCourse ? 'Course' : 'Colis',
              ),
              const SizedBox(width: HbaSpacing.sm),
              Expanded(
                child: HbaChip(label: mission.libelleDEtat, tone: mission.ton),
              ),
              const SizedBox(width: HbaSpacing.sm),
              Text(Xof.format(mission.montantXof),
                  style: theme.textTheme.titleMedium),
            ],
          ),
          const SizedBox(height: HbaSpacing.md),
          _Ligne(
            icone: Icons.place_outlined,
            texte: mission.lieu.isEmpty
                ? (mission.estUneCourse
                    ? 'Trajet enregistré sur la carte'
                    : 'Destination non precisee')
                : mission.lieu,
            estompe: mission.lieu.isEmpty,
          ),
          if (mission.reference case final String reference) ...[
            const SizedBox(height: HbaSpacing.xs),
            _Ligne(icone: Icons.tag, texte: reference, estompe: true),
          ],
        ],
      ),
    );
  }
}

/// Une course dans une liste.
///
/// PUBLIQUE PARCE QUE DEUX ECRANS L'UTILISENT : cette liste, et l'accueil, qui
/// montre la course en cours par-dessus la carte. La dupliquer ferait diverger
/// les deux le jour ou l'une des deux change.
class CarteCourse extends StatelessWidget {
  const CarteCourse({required this.course, required this.enAvant, super.key});

  final Delivery course;
  final bool enAvant;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaCard(
      highlighted: enAvant,
      onTap: () => context.push('/suivi/${course.id}'),
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // LE STATUT PASSE EN PREMIERE LIGNE, avec la reference et le prix.
          //
          // Il etait relegue en bas a droite, sous le montant. Or c'est la
          // seule chose que le client vient chercher : il connait deja sa
          // reference et son prix. Le classement d'une liste se lit de haut en
          // bas et de gauche a droite ; ce qui compte se met la ou l'oeil se
          // pose, pas dans le coin qui reste.
          Row(
            children: [
              Expanded(
                child: HbaChip(
                  label: course.status.label,
                  tone: _ton(course.status),
                ),
              ),
              const SizedBox(width: HbaSpacing.sm),
              Text(Xof.format(course.totalXof),
                  style: theme.textTheme.titleMedium),
            ],
          ),
          const SizedBox(height: HbaSpacing.md),
          _Ligne(
            icone: Icons.place_outlined,
            texte: course.dropoffLandmark.isEmpty
                ? 'Destination non precisee'
                : course.dropoffLandmark,
            estompe: course.dropoffLandmark.isEmpty,
          ),
          const SizedBox(height: HbaSpacing.xs),
          _Ligne(
            icone: Icons.tag,
            texte: course.reference,
            estompe: true,
          ),
        ],
      ),
    );
  }

  /// LA COULEUR DIT L'ISSUE, PAS L'ETAPE. Vert : c'est arrive. Rouge : c'est
  /// perdu, et le client doit le voir sans lire. Orange : c'est en cours, quel
  /// que soit le cran — distinguer huit crans par huit teintes ne ferait
  /// qu'obliger a apprendre un code.
  static HbaChipTone _ton(DeliveryStatus statut) => switch (statut) {
        DeliveryStatus.delivered => HbaChipTone.success,
        DeliveryStatus.cancelled ||
        DeliveryStatus.failed ||
        DeliveryStatus.noDriverFound ||
        DeliveryStatus.paymentFailed =>
          HbaChipTone.danger,
        DeliveryStatus.unknown => HbaChipTone.neutral,
        _ => HbaChipTone.primary,
      };
}

/// Une ligne d'appoint dans la carte d'une course.
class _Ligne extends StatelessWidget {
  const _Ligne({
    required this.icone,
    required this.texte,
    required this.estompe,
  });

  final IconData icone;
  final String texte;
  final bool estompe;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final couleur = estompe ? HbaColors.inkFaint : HbaColors.inkMuted;

    return Row(
      children: [
        Icon(icone, size: 16, color: couleur),
        const SizedBox(width: HbaSpacing.sm),
        Expanded(
          child: Text(
            texte,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.bodyMedium?.copyWith(color: couleur),
          ),
        ),
      ],
    );
  }
}
