import 'dart:io';

import 'package:hba_core/hba_core.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_ui/hba_ui.dart';
import 'package:image_picker/image_picker.dart';

import '../../core/navigation_externe.dart';
import '../../core/plantages.dart';
import '../profil/profil_repository.dart';
import 'carte_course.dart';
import 'mission_providers.dart';
import 'mission_repository.dart';
import 'models.dart';
import 'preuve_repository.dart';

/// Un point exploitable, ou null.
///
/// ZERO, ZERO EST UNE ADRESSE ABSENTE DE LA REPONSE, pas un point au large du
/// Benin : avant acceptation le BFF rend des champs vides, et protobuf rend
/// zero plutot que rien. Une carte centree la-dessus montrerait l'ocean.
LatLng? _pointDe(Place? lieu) =>
    lieu == null || (lieu.latitude == 0 && lieu.longitude == 0)
        ? null
        : LatLng(lieu.latitude, lieu.longitude);

/// Course en cours. Un seul bouton a la fois, celui de l'etape suivante.
class MissionScreen extends ConsumerStatefulWidget {
  const MissionScreen({required this.mission, required this.onClosed, super.key});

  final Mission mission;
  final VoidCallback onClosed;

  @override
  ConsumerState<MissionScreen> createState() => _MissionScreenState();
}

class _MissionScreenState extends ConsumerState<MissionScreen> {
  late Mission _mission = widget.mission;
  bool _busy = false;
  String? _error;

  /// La photo choisie pour l'etape EN COURS, pas encore envoyee.
  ///
  /// ELLE SE PREND AVANT LE GESTE, ET PART APRES. C'est la seule facon de
  /// tenir les deux moities de la decision du point 7 : la photo est proposee
  /// aux deux etapes, et « l'etape ne l'attend jamais ». Si on la prenait
  /// apres, il faudrait retenir le livreur sur un ecran dont il est deja
  /// parti ; si on l'envoyait avant, un envoi qui echoue bloquerait une remise
  /// faite devant un client qui attend.
  File? _photo;

  /// Un mot discret sur la photo — jamais dans « _error ».
  ///
  /// DEUX MESSAGES QUI NE PESENT PAS PAREIL. « _error » parle de la COURSE :
  /// le code est faux, le reseau est tombe, l'etape n'est pas passee. Celui-ci
  /// parle d'un complement facultatif. Les melanger ferait lire « echec » a un
  /// livreur dont la remise a parfaitement abouti.
  String? _motSurLaPhoto;

  /// A quelle etape une photo se rapporterait, maintenant. Null quand il n'y a
  /// rien a prouver : avant l'arrivee, ou une fois la course close.
  EtapeDeLaPreuve? get _etapePhotographiable => switch (_mission.status) {
        DeliveryStatus.driverAtPickup => EtapeDeLaPreuve.collecte,
        DeliveryStatus.pickedUp => EtapeDeLaPreuve.remise,
        _ => null,
      };

  Future<void> _choisirLaPhoto() async {
    final source = await _choisirLaSource();
    if (source == null || !mounted) return;

    final XFile? prise;
    try {
      prise = await ImagePicker().pickImage(
        source: source,
        // LE REDIMENSIONNEMENT SERIEUX EST FAIT APRES, par la compression
        // native du depot. Ces bornes evitent seulement de charger en memoire
        // une photo de cinquante megapixels le temps d'arriver la.
        maxWidth: 3000,
        maxHeight: 3000,
      );
    } on Object {
      // L'APPAREIL PHOTO REFUSE NE BLOQUE RIEN. Permission retiree, memoire
      // pleine : la course continue sans photo, et c'est exactement ce que la
      // decision prevoit.
      if (mounted) {
        setState(() => _motSurLaPhoto = "Impossible d'ouvrir l'appareil photo. "
            "L'étape se fera sans photo.");
      }
      return;
    }

    final chemin = prise?.path;
    if (chemin == null || !mounted) return;

    setState(() {
      _photo = File(chemin);
      _motSurLaPhoto = null;
    });
  }

  Future<ImageSource?> _choisirLaSource() => showModalBottomSheet<ImageSource>(
        context: context,
        // MEME RAISON QUE LES AUTRES FEUILLES : les onglets ont chacun leur
        // Navigator, loge au-dessus de la barre du bas.
        useRootNavigator: true,
        backgroundColor: HbaColors.background,
        shape: const RoundedRectangleBorder(
          borderRadius: BorderRadius.vertical(top: Radius.circular(HbaRadius.card)),
        ),
        builder: (feuille) => SafeArea(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              ListTile(
                leading: const Icon(Icons.photo_camera_outlined),
                title: const Text('Prendre une photo'),
                onTap: () => Navigator.of(feuille).pop(ImageSource.camera),
              ),
              ListTile(
                leading: const Icon(Icons.photo_library_outlined),
                title: const Text('Choisir dans la galerie'),
                onTap: () => Navigator.of(feuille).pop(ImageSource.gallery),
              ),
              const SizedBox(height: HbaSpacing.sm),
            ],
          ),
        ),
      );

  /// Envoie la photo APRES que l'etape a abouti cote serveur.
  ///
  /// RIEN DE CE QUI SE PASSE ICI NE PEUT FAIRE ECHOUER L'ETAPE. Elle est deja
  /// enregistree quand cette methode est appelee ; une exception qui
  /// remonterait ferait lire un echec a un livreur dont la course a avance.
  /// D'ou le « on Object » — un refus du serveur, une coupure, un fichier
  /// disparu : tous finissent par le meme mot discret.
  ///
  /// ET ELLE NE REESSAIE PAS. Le point 7 : « une photo rattrapee plus tard ne
  /// prouverait plus le meme instant ». Le service borne d'ailleurs le depot a
  /// une demi-heure apres l'etape, et la file hors ligne ne sait pas transporter
  /// un fichier — c'est le fait meme qui a fait ecarter « photo exigee ».
  Future<void> _envoyerLaPhoto(EtapeDeLaPreuve etape) async {
    final fichier = _photo;
    if (fichier == null) return;

    // ON L'OUBLIE AVANT D'ENVOYER, pas apres : une photo de collecte qui
    // resterait en memoire partirait une seconde fois a la remise, et le
    // serveur la refuserait en « preuve deja jointe » — un message
    // incomprehensible pour un livreur qui n'a rien redemande.
    if (mounted) setState(() => _photo = null);

    try {
      await ref.read(preuveRepositoryProvider).deposer(
            missionId: _mission.id,
            etape: etape,
            fichier: fichier,
          );

      if (mounted) setState(() => _motSurLaPhoto = 'Photo envoyée.');
    } on Object catch (erreur, pile) {
      Plantages.noter(erreur, pile, contexte: 'depot de preuve ${etape.code}');

      if (mounted) {
        setState(() => _motSurLaPhoto = "La photo n'est pas partie. "
            "L'étape, elle, est bien enregistrée.");
      }
    }
  }

  Future<void> _advance() async {
    final repository = ref.read(missionRepositoryProvider);

    setState(() {
      _busy = true;
      _error = null;

      // LE MOT DE L'ETAPE PRECEDENTE S'EFFACE ICI. « Photo envoyee » laisse
      // sous la glissiere de la remise, il parlerait de la collecte.
      _motSurLaPhoto = null;
    });

    try {
      final issue = switch (_mission.status) {
        DeliveryStatus.driverAssigned => await repository.markArrived(_mission.id),
        DeliveryStatus.driverAtPickup => await repository.markPickedUp(_mission.id),
        _ => const Issue(),
      };

      if (!mounted) return;

      if (issue.mission case final aJour?) {
        final collecte = _mission.status == DeliveryStatus.driverAtPickup;
        setState(() => _mission = aJour);

        // LA PHOTO PART APRES, ET SEULEMENT SI L'ETAPE EST PASSEE. Le serveur
        // refuse une preuve sur une etape qui n'a pas eu lieu — c'est sa
        // regle, et elle est juste : une preuve de ce qui n'est pas arrive
        // n'est pas une preuve.
        if (collecte) await _envoyerLaPhoto(EtapeDeLaPreuve.collecte);
        return;
      }

      if (issue.miseEnFile) {
        // L'ETAPE AVANCE QUAND MEME, ET SEULEMENT POUR CES DEUX-LA. « Je suis
        // arrive » et « j'ai pris le colis » sont des constats : le serveur ne
        // peut pas les contredire, il ne fait que les enregistrer. Bloquer le
        // livreur sur un bouton jusqu'au retour du reseau l'empecherait de
        // finir sa course dans un parking souterrain.
        //
        // LA REMISE, ELLE, N'AVANCE PAS — voir _confirmDelivery. Le serveur y
        // valide un code, donc il PEUT contredire.
        setState(() {
          _mission = _mission.avecStatut(
            _mission.status == DeliveryStatus.driverAssigned
                ? DeliveryStatus.driverAtPickup
                : DeliveryStatus.pickedUp,
          );
          _error = 'Pas de réseau. L étape est enregistrée et repartira toute '
              'seule dès que la connexion revient.';

          // LA PHOTO NE SUIT PAS, ET LE LIVREUR DOIT L'APPRENDRE MAINTENANT.
          // « Sans reseau, l'etape part SANS photo et n'y revient pas » — une
          // photo rattrapee plus tard ne prouverait plus le meme instant. La
          // taire laisserait croire qu'elle est jointe.
          if (_photo != null) {
            _photo = null;
            _motSurLaPhoto = "La photo n'a pas pu être jointe : elle ne "
                "montrerait plus ce moment si elle partait plus tard.";
          }
        });
      }
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  /// Le livreur constate que la course ne peut pas aboutir.
  ///
  /// CONFIRMATION EN DEUX TEMPS : on choisit un motif, puis on confirme. La
  /// course part en ECHEC et ne se rouvre pas ; ce n'est pas un geste qu'on
  /// annule.
  Future<void> _declarerIncident() async {
    final motif = await showModalBottomSheet<String>(
      context: context,
      useRootNavigator: true,
      isScrollControlled: true,
      backgroundColor: HbaColors.background,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(HbaRadius.card)),
      ),
      builder: (_) => const _IncidentSheet(),
    );

    if (motif == null || !mounted) return;

    setState(() {
      _busy = true;
      _error = null;

      // LE MOT DE L'ETAPE PRECEDENTE S'EFFACE ICI. « Photo envoyee » laisse
      // sous la glissiere de la remise, il parlerait de la collecte.
      _motSurLaPhoto = null;
    });

    try {
      final issue =
          await ref.read(missionRepositoryProvider).declareIncident(_mission.id, motif);

      if (!mounted) return;

      if (issue.miseEnFile) {
        // MEME PRUDENCE QUE POUR LA REMISE : le serveur peut refuser — une
        // course deja close par l'exploitation, par exemple. Tant qu'il n'a
        // pas repondu, on n'annonce pas au livreur qu'il est libere.
        setState(() => _error = "Pas de réseau. Le signalement est enregistré "
            'et partira dès que la connexion revient ; la course reste '
            'ouverte en attendant.');
        return;
      }

      final updated = issue.mission;
      if (updated == null) return;

      setState(() => _mission = updated);

      Plantages.trace('incident declare');

      widget.onClosed();
      if (mounted) Navigator.of(context).pop();
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _confirmDelivery() async {
    final otp = await showModalBottomSheet<String>(
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
      builder: (_) => const _OtpSheet(),
    );

    if (otp == null || !mounted) return;

    setState(() {
      _busy = true;
      _error = null;

      // LE MOT DE L'ETAPE PRECEDENTE S'EFFACE ICI. « Photo envoyee » laisse
      // sous la glissiere de la remise, il parlerait de la collecte.
      _motSurLaPhoto = null;
    });

    try {
      final issue =
          await ref.read(missionRepositoryProvider).confirmDelivery(_mission.id, otp);

      if (!mounted) return;

      if (issue.miseEnFile) {
        // LA REMISE NE S'AFFICHE PAS COMME FAITE TANT QUE LE SERVEUR N'A PAS
        // VALIDE LE CODE. C'est la difference avec les deux etapes
        // precedentes : ici le serveur peut refuser — un code mal entendu, mal
        // recopie. Annoncer « course livree » et afficher la remuneration
        // serait promettre au livreur quelque chose qui n'est peut-etre pas
        // arrive.
        setState(() {
          _error = 'Pas de réseau. La remise est enregistrée et '
              'partira toute seule ; elle sera confirmee au retour de la '
              'connexion.';

          if (_photo != null) {
            _photo = null;
            _motSurLaPhoto = "La photo n'a pas pu être jointe : elle ne "
                "montrerait plus ce moment si elle partait plus tard.";
          }
        });
        return;
      }

      final updated = issue.mission;
      if (updated == null) return;

      setState(() => _mission = updated);

      Plantages.trace('colis remis');

      if (updated.status == DeliveryStatus.delivered) {
        // AVANT LE RECAPITULATIF, PAS APRES. La feuille ne se ferme que sur
        // « Terminer », et l'ecran se depile juste derriere : une photo
        // envoyee apres partirait d'un widget qui n'existe plus.
        await _envoyerLaPhoto(EtapeDeLaPreuve.remise);

        // LE RECAPITULATIF PASSE AVANT LA FERMETURE. Jusqu'ici l'ecran se
        // refermait sec : le livreur remettait un colis et se retrouvait sur
        // la carte, sans que rien ne lui dise ce que la course lui avait
        // rapporte. Il devait aller le chercher dans l'onglet Gains, ou ne
        // pas le savoir.
        await _montrerLeRecapitulatif(updated);
        widget.onClosed();

        // L'ECRAN RESTAIT OUVERT SUR UNE COURSE FINIE, ET C'EST LE DEFAUT
        // QU'ON CORRIGE. « onClosed » ne fait qu'effacer la course de
        // l'accueil — la route de cet ecran, elle, avait ete empilee dessus
        // et personne ne la depilait. Le livreur se retrouvait devant le
        // detail d'une course livree, sans bouton, sans rien a y faire, et
        // devait trouver la fleche de retour.
        if (mounted) Navigator.of(context).pop();
      }
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _montrerLeRecapitulatif(Mission mission) async {
    if (!mounted) return;

    await showModalBottomSheet<void>(
      context: context,

      // LA BARRE D'ONGLETS RECOUVRAIT LE BAS DE LA FEUILLE, BOUTON COMPRIS.
      //
      // L'application est batie sur un StatefulShellRoute : chaque onglet a
      // SON PROPRE Navigator, loge dans le corps du Scaffold, au-dessus de la
      // barre. Une feuille ouverte sur ce Navigator-la ne connait donc pas la
      // barre et se dessine dessous. Le livreur voyait « Terminer » depasser
      // derriere « Accueil », sans pouvoir l'atteindre.
      //
      // « useRootNavigator » remonte la feuille au Navigator de l'application
      // entiere : elle couvre alors tout l'ecran, barre comprise, ce qui est
      // de toute facon ce qu'on veut d'un ecran dont on ne peut pas sortir
      // sans lire.
      useRootNavigator: true,

      // SANS CECI LA FEUILLE EST PLAFONNEE A 9/16 DE L'ECRAN et son contenu
      // est coupe en silence sur un telephone court.
      isScrollControlled: true,

      // ON NE S'EN DEBARRASSE PAS D'UN GESTE DE COTE. C'est le seul moment ou
      // le livreur voit ce qu'il a gagne ; une feuille qu'on balaie par
      // reflexe en refermant la precedente la ferait disparaitre sans qu'il
      // l'ait lue.
      isDismissible: false,
      enableDrag: false,
      backgroundColor: HbaColors.background,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(HbaRadius.card)),
      ),
      builder: (_) => _Recapitulatif(mission: mission),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final mission = _mission;
    final action = mission.status.nextDriverAction;
    final atDelivery = mission.status == DeliveryStatus.pickedUp;

    return Scaffold(
      appBar: AppBar(
        title: Text(mission.reference),
        actions: [
          // LA SEULE SORTIE QUAND LA REMISE EST IMPOSSIBLE, et elle n'existait
          // pas. Depuis « colis recupere », la seule chose que le livreur
          // pouvait declarer etait la remise : destinataire absent, adresse
          // fausse ou code bloque apres cinq essais, il gardait le colis et
          // restait en mission — donc sans pouvoir se mettre hors ligne ni
          // recevoir la moindre offre — jusqu'a ce qu'un ops cloture a sa
          // place.
          //
          // DISCRETE, PAS CACHEE. Une icone dans la barre, loin de la
          // glissiere : ce n'est pas un geste qu'on fait par erreur, et ce
          // n'est pas non plus un geste qu'on doit deviner.
          if (mission.isOpen)
            IconButton(
              onPressed: _busy ? null : _declarerIncident,
              icon: const Icon(Icons.report_problem_outlined),
              tooltip: 'Signaler un problème',
            ),
        ],
      ),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.symmetric(horizontal: HbaSpacing.gutter),
          children: [
            HbaCard(
              padding: const EdgeInsets.all(HbaSpacing.lg),
              child: Row(
                children: [
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('Votre rémunération',
                            style: theme.textTheme.bodyMedium),
                        Text(
                          Xof.format(mission.driverEarningXof),
                          style: theme.textTheme.headlineMedium
                              ?.copyWith(color: HbaColors.primaryInk),
                        ),
                      ],
                    ),
                  ),
                  HbaChip(label: _statusLabel(mission.status)),
                ],
              ),
            ),
            const SizedBox(height: HbaSpacing.md),

            // SITUER LA COURSE SANS QUITTER L'APPLICATION. Le guidage reste
            // l'affaire de Waze ou de Plans — ils connaissent le trafic et
            // parlent, ce qu'une carte maison ne fera pas de sitot. Ce qui
            // manquait, c'etait de voir d'un coup d'oeil ou va la course
            // avant de lancer la navigation.
            if (_pointDe(mission.pickup) case final collecte?) ...[
              CarteCourse(
                collecte: collecte,
                depot: _pointDe(mission.dropoff),

                // Le profil est deja en memoire a ce stade — la course est
                // acceptee, l'application tourne depuis un moment. Rien a
                // attendre, et rien a casser s'il manque : l'epingle reste
                // alors un disque nu.
                vehicule: ref.watch(profilProvider).valueOrNull?.vehicule?.type,
              ),
              const SizedBox(height: HbaSpacing.xs),
              Text(
                _pointDe(mission.dropoff) == null
                    ? "Le point de livraison s'affiche une fois la course acceptée."
                    : "Le trait est à vol d'oiseau, ce n'est pas l'itinéraire : « Y aller » ouvre votre application de navigation.",
                style: theme.textTheme.bodySmall,
              ),
              const SizedBox(height: HbaSpacing.md),
            ],

            _PlaceCard(
              title: 'Collecte',
              place: mission.pickup,
              icon: Icons.storefront_outlined,
            ),
            const SizedBox(height: HbaSpacing.md),
            _PlaceCard(
              title: 'Livraison',
              place: mission.dropoff,
              icon: Icons.flag_outlined,
              // Avant acceptation le BFF renvoie des champs vides : l'ecran
              // doit le vivre sans supposer leur presence.
              fallback: "Adresse communiquée après acceptation.",
            ),
            const SizedBox(height: HbaSpacing.md),

            // LA CARTE NE S'AFFICHE QUE SI ELLE DIT QUELQUE CHOSE DE NEUF.
            //
            // « Destinataire » et le contact de la carte « Livraison » sont
            // DEUX CHAMPS DISTINCTS DU CONTRAT, et ce n'est pas une subtilite
            // d'implementation : « Location.contact_name » designe la personne
            // PRESENTE sur le lieu — un gardien, une secretaire, le comptoir
            // d'une boutique —, « recipient_name » celle a qui le colis
            // revient. Les fondre en un seul champ se decide dans le contrat,
            // pas dans cet ecran.
            //
            // MAIS QUAND LES DEUX PORTENT LA MEME VALEUR, les afficher deux
            // fois est du bruit — et c'est le cas de la plupart des courses,
            // ou le client saisit la meme personne aux deux endroits. La carte
            // ne garde alors que ce que l'autre ne dit pas : le colis.
            if (!_memeQueLeContact(mission) ||
                mission.packageDescription.isNotEmpty) ...[
              HbaCard(
                padding: const EdgeInsets.all(HbaSpacing.lg),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (!_memeQueLeContact(mission)) ...[
                      Text('Destinataire', style: theme.textTheme.bodySmall),
                      Text(
                        mission.recipientName.isEmpty
                            ? '--'
                            : mission.recipientName,
                        style: theme.textTheme.titleMedium,
                      ),
                      if (mission.recipientPhone.isNotEmpty) ...[
                        Text(mission.recipientPhone,
                            style: theme.textTheme.bodyMedium),
                        const SizedBox(height: HbaSpacing.md),
                        _BoutonAppel(numero: mission.recipientPhone),
                      ],
                      if (mission.packageDescription.isNotEmpty)
                        const SizedBox(height: HbaSpacing.md),
                    ],
                    if (mission.packageDescription.isNotEmpty) ...[
                      Text('Colis', style: theme.textTheme.bodySmall),
                      Text(mission.packageDescription,
                          style: theme.textTheme.bodyMedium),
                    ],
                  ],
                ),
              ),
            ],
            if (_error != null) ...[
              const SizedBox(height: HbaSpacing.md),
              Text(
                _error!,
                style: theme.textTheme.bodyMedium
                    ?.copyWith(color: HbaColors.danger),
              ),
            ],
            // LA PHOTO, FACULTATIVE, ET AU-DESSUS DU GESTE.
            //
            // AU-DESSUS PARCE QU'ELLE SE PREND AVANT. Le point 7 a tranche :
            // proposee aux deux etapes, jamais exigee, et « l'etape ne
            // l'attend jamais ». La placer sous la glissiere reviendrait a la
            // proposer apres coup, c'est-a-dire a un livreur deja reparti.
            if (_etapePhotographiable case final etape?) ...[
              const SizedBox(height: HbaSpacing.lg),
              _BlocPhoto(
                etape: etape,
                photo: _photo,
                busy: _busy,
                onChoisir: _choisirLaPhoto,
                onRetirer: () => setState(() => _photo = null),
              ),
            ],
            if (_motSurLaPhoto != null) ...[
              const SizedBox(height: HbaSpacing.sm),
              Text(
                _motSurLaPhoto!,
                // PAS EN ROUGE, MEME QUAND LA PHOTO A ECHOUE. Le rouge dit
                // « votre course a un probleme » ; ici la course va bien, et
                // c'est un complement facultatif qui n'est pas parti.
                style: theme.textTheme.bodySmall,
              ),
            ],
            const SizedBox(height: HbaSpacing.lg),
            if (action != null)
              // CHAQUE ETAPE SE GLISSE. « Je suis au point de collecte »
              // envoye par erreur fausse l'horodatage de la course ; « J'ai
              // recupere le colis » declare un colis en main qu'on n'a pas
              // encore. Ces trois gestes se font en roulant ou en tenant un
              // carton : le glissement est le seul qui ne parte pas tout
              // seul.
              HbaGlissiere(
                libelle: action,
                libelleConfirme: 'Envoi...',
                busy: _busy,
                onConfirme: atDelivery ? _confirmDelivery : _advance,
              ),
            const SizedBox(height: HbaSpacing.lg),
          ],
        ),
      ),
    );
  }

  /// Le destinataire porte-t-il exactement le contact du lieu de livraison.
  ///
  /// COMPARAISON EXACTE, SANS NORMALISATION, ET C'EST DELIBERE. Rapprocher
  /// « +229 01 97 … » de « 0197… » demanderait de decider ce qu'est le meme
  /// numero au Benin — indicatif implicite, ancien format a huit chiffres,
  /// espaces. Se tromper la-dessus ferait disparaitre de l'ecran un
  /// destinataire QUI N'EST PAS le contact sur place. En cas de doute on
  /// affiche les deux : du bruit se supporte, une personne cachee non.
  static bool _memeQueLeContact(Mission mission) {
    final lieu = mission.dropoff;

    return mission.recipientName == (lieu?.contactName ?? '') &&
        mission.recipientPhone == (lieu?.phone ?? '');
  }

  static String _statusLabel(DeliveryStatus status) => switch (status) {
        DeliveryStatus.driverAssigned => 'AFFECTEE',
        DeliveryStatus.driverAtPickup => 'SUR PLACE',
        DeliveryStatus.pickedUp => 'EN COURS',
        DeliveryStatus.delivered => 'REMISE',
        _ => '--',
      };
}

/// Le bloc « ajouter une photo » d'une etape.
///
/// IL DIT « FACULTATIF », ET CE MOT EST LA REGLE ELLE-MEME. Ce qui fait foi
/// est le CODE DE REMISE dicte par le destinataire (ADR 0005) ; la photo n'est
/// qu'un complement, et elle ne dit rien de plus qu'un etat de colis. Un
/// livreur qui croirait sa course bloquee sans elle prendrait une photo dans
/// un couloir sombre pour s'en debarrasser.
class _BlocPhoto extends StatelessWidget {
  const _BlocPhoto({
    required this.etape,
    required this.photo,
    required this.busy,
    required this.onChoisir,
    required this.onRetirer,
  });

  final EtapeDeLaPreuve etape;
  final File? photo;
  final bool busy;
  final VoidCallback onChoisir;
  final VoidCallback onRetirer;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final jointe = photo;

    final aide = switch (etape) {
      EtapeDeLaPreuve.collecte => "L'état du colis au moment où vous le prenez.",
      EtapeDeLaPreuve.remise => 'Le colis remis, ou l\'endroit où vous le laissez.',
    };

    if (jointe == null) {
      return Align(
        alignment: Alignment.centerLeft,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            OutlinedButton.icon(
              // GRISE PENDANT L'ENVOI DE L'ETAPE : ouvrir l'appareil photo a
              // cet instant ferait revenir le livreur sur un ecran qui a
              // change d'etape entre-temps.
              onPressed: busy ? null : onChoisir,
              icon: const Icon(Icons.photo_camera_outlined),
              label: const Text('Ajouter une photo (facultatif)'),
            ),
            const SizedBox(height: HbaSpacing.xs),
            Text(aide, style: theme.textTheme.bodySmall),
          ],
        ),
      );
    }

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.md),
      child: Row(
        children: [
          ClipRRect(
            borderRadius: BorderRadius.circular(HbaRadius.card),
            child: Image.file(
              jointe,
              width: 56,
              height: 56,
              fit: BoxFit.cover,
              // UN FICHIER PEUT AVOIR DISPARU ENTRE LA PRISE ET L'AFFICHAGE
              // — cache systeme vide, photo supprimee de la galerie. Sans
              // ceci, l'ecran de la course se casse pour une vignette.
              errorBuilder: (_, __, ___) => const SizedBox(
                width: 56,
                height: 56,
                child: Icon(Icons.image_not_supported_outlined),
              ),
            ),
          ),
          const SizedBox(width: HbaSpacing.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('Photo jointe', style: theme.textTheme.titleSmall),
                Text(
                  "Elle partira une fois l'étape enregistrée.",
                  style: theme.textTheme.bodySmall,
                ),
              ],
            ),
          ),
          IconButton(
            onPressed: busy ? null : onRetirer,
            icon: const Icon(Icons.close),
            tooltip: 'Retirer la photo',
          ),
        ],
      ),
    );
  }
}

class _PlaceCard extends StatelessWidget {
  const _PlaceCard({
    required this.title,
    required this.place,
    required this.icon,
    this.fallback = '--',
  });

  final String title;
  final Place? place;
  final IconData icon;
  final String fallback;

  /// « Nom — numero », ou l'un des deux seul, ou rien.
  static String _contact(Place lieu) {
    if (lieu.contactName.isEmpty) return lieu.phone;
    if (lieu.phone.isEmpty) return lieu.contactName;

    return '${lieu.contactName} — ${lieu.phone}';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final value = place;

    return HbaCard(
      padding: const EdgeInsets.all(HbaSpacing.lg),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, color: HbaColors.primaryInk),
          const SizedBox(width: HbaSpacing.md),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title, style: theme.textTheme.bodySmall),
                const SizedBox(height: HbaSpacing.xs),
                Text(
                  value == null || value.landmark.isEmpty
                      ? fallback
                      : value.landmark,
                  style: theme.textTheme.titleMedium,
                ),
                // LE CONTACT SUR PLACE EST CELUI QU'ON APPELLE EN PREMIER.
                // Au retrait c'est le commercant derriere son comptoir, a la
                // livraison la personne qui doit descendre ouvrir.
                //
                // LE NUMERO EST ECRIT ICI, PAS DANS LE BOUTON. Il y etait, et
                // les deux pastilles n'entraient plus cote a cote sur un
                // telephone de 360 dp : celle qui portait quatorze chiffres
                // faisait deux fois la largeur de « Y aller ». Il se lit donc
                // au-dessus, ou il sert aussi a qui veut le recopier.
                if (value != null && _contact(value).isNotEmpty) ...[
                  const SizedBox(height: HbaSpacing.xs),
                  Text(_contact(value), style: theme.textTheme.bodyMedium),
                ],
                if (value != null && value.notes.isNotEmpty) ...[
                  const SizedBox(height: HbaSpacing.xs),
                  Text(value.notes, style: theme.textTheme.bodyMedium),
                ],

                // LES DEUX GESTES DU LIEU, SUR UNE SEULE LIGNE ET DE MEME
                // LARGEUR. Ils repondent a la meme question — comment
                // j'atteins cet endroit —, l'un par la route, l'autre par le
                // telephone. Empiles l'un sous l'autre, ils se lisaient comme
                // deux etapes successives.
                //
                // UN POINT A ZERO N'EST PAS UN POINT. Une Location absente de
                // la reponse arrive en 0,0 — au large du golfe de Guinee, a
                // six cents kilometres de Cotonou. Y envoyer un livreur
                // serait pire que ne rien proposer : « Y aller » disparait
                // alors, et « Appeler » occupe la ligne seul.
                if (value != null &&
                    (value.phone.isNotEmpty ||
                        value.latitude != 0 ||
                        value.longitude != 0)) ...[
                  const SizedBox(height: HbaSpacing.md),
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      if (value.phone.isNotEmpty)
                        Expanded(child: _BoutonAppel(numero: value.phone)),
                      if (value.phone.isNotEmpty &&
                          (value.latitude != 0 || value.longitude != 0))
                        const SizedBox(width: HbaSpacing.sm),
                      if (value.latitude != 0 || value.longitude != 0)
                        Expanded(
                          child: _BoutonItineraire(
                            place: value,
                            etiquette: title,
                          ),
                        ),
                    ],
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

/// « Appeler » : passe la main au composeur du telephone.
///
/// AUCUNE DONNEE NOUVELLE N'EST EXPOSEE ICI. Le numero du destinataire et
/// celui du contact sur place etaient deja affiches en clair ; la matrice de
/// visibilite les accorde au livreur affecte. Ce bloc n'ajoute qu'un geste :
/// un appui la ou il fallait auparavant memoriser dix chiffres, sortir de
/// l'application et les recomposer.
///
/// LE NUMERO RESTE VISIBLE SOUS LE LIBELLE, et c'est voulu. Un livreur dont
/// le telephone n'a pas d'application d'appel, ou qui veut envoyer un message
/// depuis un autre appareil, doit pouvoir le lire sans appuyer.
class _BoutonAppel extends StatefulWidget {
  const _BoutonAppel({required this.numero});

  final String numero;

  @override
  State<_BoutonAppel> createState() => _BoutonAppelState();
}

class _BoutonAppelState extends State<_BoutonAppel> {
  bool _echec = false;

  Future<void> _appeler() async {
    final ok = await appeler(widget.numero);
    if (mounted) setState(() => _echec = !ok);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        HbaPressable(
          onTap: _appeler,
          elevation: HbaElevation.douce,
          radius: HbaRadius.button,
          // L'ALIGNEMENT ETIRE LE BLOC SUR TOUTE LA LARGEUR DISPONIBLE, et
          // c'est ce qu'on veut ici : le bloc est dans un Expanded, il doit
          // remplir sa moitie de ligne pour que les deux pastilles fassent la
          // meme largeur. La marge verticale porte la hauteur a 48 dp.
          alignment: Alignment.center,
          padding: const EdgeInsets.symmetric(
            horizontal: HbaSpacing.sm,
            vertical: 14,
          ),
          semantique: 'Appeler le ${widget.numero}',
          child: const Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.call_outlined, size: 18, color: HbaColors.primaryInk),
              SizedBox(width: HbaSpacing.sm),
              Text(
                'Appeler',
                style: TextStyle(
                  color: HbaColors.primaryInk,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ],
          ),
        ),
        if (_echec) ...[
          const SizedBox(height: HbaSpacing.xs),
          Text(
            "Ce téléphone n'a pas d'application d'appel. Composez le "
            '${widget.numero}.',
            style: theme.textTheme.bodySmall
                ?.copyWith(color: theme.colorScheme.error),
          ),
        ],
      ],
    );
  }
}

/// « Y aller » : passe la main a l'application de navigation du telephone.
class _BoutonItineraire extends StatefulWidget {
  const _BoutonItineraire({required this.place, required this.etiquette});

  final Place place;
  final String etiquette;

  @override
  State<_BoutonItineraire> createState() => _BoutonItineraireState();
}

class _BoutonItineraireState extends State<_BoutonItineraire> {
  bool _echec = false;

  Future<void> _ouvrir() async {
    final ok = await ouvrirItineraire(
      latitude: widget.place.latitude,
      longitude: widget.place.longitude,
      etiquette: widget.place.landmark.isEmpty
          ? widget.etiquette
          : widget.place.landmark,
    );

    if (mounted) setState(() => _echec = !ok);
  }

  @override
  Widget build(BuildContext context) => Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // UN BLOC EN RELIEF PLUTOT QU'UN BOUTON CONTOURE.
          //
          // DEUX RAISONS, ET LA PREMIERE EST UN DEFAUT CORRIGE : le libelle
          // etait en #F2690F, soit 2,6:1 sur cette matiere — sous le seuil de
          // 3:1 que Material demande pour un simple element d'interface, et
          // tres loin des 4,5:1 d'un texte. Il passe en #A6480A.
          //
          // La seconde est de style : un contour d'un pixel est le seul objet
          // de l'ecran qui ne serait ni en relief ni creux. Ici il s'enfonce
          // comme tout le reste.
          HbaPressable(
            onTap: _ouvrir,
            elevation: HbaElevation.douce,
            radius: HbaRadius.button,
            // MEME LARGEUR QUE « APPELER », a cote duquel il vit desormais.
            // La marge verticale porte la hauteur a 48 dp, la cible tactile
            // de Material.
            alignment: Alignment.center,
            padding: const EdgeInsets.symmetric(
              horizontal: HbaSpacing.sm,
              vertical: 14,
            ),
            semantique: 'Y aller',
            child: const Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(Icons.navigation_outlined, size: 18, color: HbaColors.primaryInk),
                SizedBox(width: HbaSpacing.sm),
                Text(
                  'Y aller',
                  style: TextStyle(
                    color: HbaColors.primaryInk,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ],
            ),
          ),

          // UN BOUTON QUI NE FAIT RIEN EST PIRE QU'UN BOUTON ABSENT : le
          // livreur reste a l'arret a appuyer dessus. S'il n'y a aucune
          // application de navigation et aucun navigateur, on donne les
          // coordonnees a recopier.
          if (_echec) ...[
            const SizedBox(height: HbaSpacing.xs),
            Text(
              'Aucune application de navigation. Coordonnées : '
              '${widget.place.latitude.toStringAsFixed(5)}, '
              '${widget.place.longitude.toStringAsFixed(5)}',
              style: Theme.of(context)
                  .textTheme
                  .bodySmall
                  ?.copyWith(color: Theme.of(context).colorScheme.error),
            ),
          ],
        ],
      );
}

/// Ce que la course a rapporte, dit une fois et clairement.
///
/// LE MONTANT EST CELUI DE L'OFFRE, PAS UN CALCUL D'ICI. C'est la part figee
/// a la confirmation de la course, celle que le livreur avait sous les yeux en
/// acceptant, et celle que le grand livre creditera. Trois chiffres qui
/// doivent etre le meme ; en recalculer un serait s'offrir une chance de les
/// faire diverger.
///
/// ON NE DIT PAS « AJOUTE A VOS GAINS », ET C'EST UNE PRECISION QUI COMPTE. Le
/// credit n'est pas ecrit par cet appel : il arrive par un evenement, quelques
/// instants plus tard. Un livreur qui ouvrirait ses gains dans la seconde
/// pourrait ne rien y voir encore, et conclure a une perte. La phrase dit donc
/// ce qui est vrai — le montant que cette course rapporte — et annonce le
/// delai au lieu de le cacher.
class _Recapitulatif extends StatelessWidget {
  const _Recapitulatif({required this.mission});

  final Mission mission;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    // SAFEAREA POUR LA BARRE DE GESTES, DEFILEMENT POUR LES PETITS ECRANS.
    // Le premier tient le bouton au-dessus du trait de navigation d'Android ;
    // le second fait qu'un telephone court fait defiler au lieu de couper.
    return SafeArea(
      top: false,
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(
          HbaSpacing.gutter,
          HbaSpacing.lg,
          HbaSpacing.gutter,
          HbaSpacing.lg,
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Center(
              child: Container(
                height: 64,
                width: 64,
                decoration: const BoxDecoration(
                  color: HbaColors.successSoft,
                  shape: BoxShape.circle,
                ),
                child: const Icon(
                  Icons.check_rounded,
                  size: 36,
                  color: HbaColors.success,
                ),
              ),
            ),
            const SizedBox(height: HbaSpacing.md),

            Text(
              'Course livrée',
              textAlign: TextAlign.center,
              style: theme.textTheme.headlineMedium,
            ),

            if (mission.reference.isNotEmpty) ...[
              const SizedBox(height: HbaSpacing.xs),
              Text(
                mission.reference,
                textAlign: TextAlign.center,
                style: theme.textTheme.bodySmall,
              ),
            ],

            const SizedBox(height: HbaSpacing.lg),

            // LE MONTANT EN GRAND, SUR LE DEGRADE DE LA CARTE DE SOLDE : c'est
            // le meme objet que l'ecran Gains, et le livreur fait le lien sans
            // qu'on ait a l'ecrire.
            Container(
              padding: const EdgeInsets.symmetric(
                horizontal: HbaSpacing.lg,
                vertical: HbaSpacing.md,
              ),
              decoration: BoxDecoration(
                gradient: const LinearGradient(
                  colors: HbaColors.balanceGradient,
                  begin: Alignment.topLeft,
                  end: Alignment.bottomRight,
                ),
                borderRadius: BorderRadius.circular(HbaRadius.card),
              ),
              child: Column(
                children: [
                  const Text(
                    'Cette course vous rapporte',
                    style: TextStyle(color: Colors.white70, fontSize: 13),
                  ),
                  const SizedBox(height: HbaSpacing.xs),
                  Text(
                    Xof.format(mission.driverEarningXof),
                    style: const TextStyle(
                      color: Colors.white,
                      fontSize: 34,
                      fontWeight: FontWeight.w800,
                      letterSpacing: -0.5,
                    ),
                  ),
                ],
              ),
            ),

            const SizedBox(height: HbaSpacing.md),
            Text(
              'Le montant apparaît dans vos gains dans quelques instants, puis '
              'vous pourrez en demander le versement.',
              textAlign: TextAlign.center,
              style: theme.textTheme.bodySmall,
            ),

            const SizedBox(height: HbaSpacing.lg),
            HbaButton(
              label: 'Terminer',
              onPressed: () => Navigator.of(context).pop(),
            ),
          ],
        ),
      ),
    );
  }
}

/// Saisie du code de remise.
///
/// LE CODE N'EST JAMAIS AFFICHE NI PRE-REMPLI. Le livreur ne l'a pas recu : le
/// destinataire le lui dicte. Rien ici ne le conserve ni ne le journalise.
/// Le choix d'un motif d'incident, puis la confirmation.
///
/// LES MOTIFS PROPOSES NE SONT PAS UN CONTRAT. Le service enregistre un texte
/// libre : aucune liste d'incidents n'est tranchee, et la graver dans le proto
/// ou dans la base avant qu'elle soit decidee la rendrait tres difficile a
/// corriger. Ces quatre-la sont les cas qu'on sait deja possibles ; « Autre »
/// laisse le livreur ecrire ce qu'on n'avait pas prevu, ce qui est exactement
/// ce dont on a besoin pour fixer la liste plus tard.
class _IncidentSheet extends StatefulWidget {
  const _IncidentSheet();

  @override
  State<_IncidentSheet> createState() => _IncidentSheetState();
}

class _IncidentSheetState extends State<_IncidentSheet> {
  static const _motifs = [
    'Destinataire injoignable',
    'Adresse introuvable',
    'Destinataire refuse le colis',
    'Code de remise bloqué',
  ];

  String? _choisi;
  final _autre = TextEditingController();

  @override
  void dispose() {
    _autre.dispose();
    super.dispose();
  }

  bool get _libre => _choisi == null;

  String get _motif => _libre ? _autre.text.trim() : _choisi!;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: EdgeInsets.only(
        left: HbaSpacing.gutter,
        right: HbaSpacing.gutter,
        top: HbaSpacing.lg,
        // LE CLAVIER POUSSE LA FEUILLE, il ne la recouvre pas : le champ libre
        // est en bas, et sans ceci il disparait sous le clavier a la seconde ou
        // on le touche.
        bottom: MediaQuery.of(context).viewInsets.bottom + HbaSpacing.lg,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('Que se passe-t-il ?', style: theme.textTheme.titleLarge),
          const SizedBox(height: HbaSpacing.sm),
          Text(
            'La course sera clôturée en échec et HBA vous recontactera pour le '
            'colis. Ce geste ne s\'annule pas.',
            style: theme.textTheme.bodyMedium
                ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
          ),
          const SizedBox(height: HbaSpacing.lg),
          // LE GROUPE PORTE LE CHOIX, PLUS CHAQUE TUILE.
          //
          // « groupValue » et « onChanged » sont depreciees sur RadioListTile
          // depuis Flutter 3.32, remplacees par un RadioGroup ancetre — arrive
          // en 3.35, d'ou le plancher remonte dans le pubspec de cette seule
          // application.
          //
          // CE N'EST PAS QU'UN DEPLACEMENT DE PARAMETRES. La tuile « Autre »
          // forcait « null » dans son propre gestionnaire, en doublon de sa
          // « value » : deux facons de dire la meme chose, dont l'une pouvait
          // cesser d'etre d'accord avec l'autre. Maintenant c'est la valeur de
          // la tuile qui decide, et il n'y a plus qu'un gestionnaire.
          RadioGroup<String?>(
            groupValue: _choisi,
            onChanged: (valeur) => setState(() => _choisi = valeur),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                for (final motif in _motifs)
                  RadioListTile<String?>(
                    value: motif,
                    title: Text(motif),
                    contentPadding: EdgeInsets.zero,
                  ),
                // CONSTANTE DEPUIS QU'ELLE N'A PLUS DE FERMETURE. Elle
                // portait un « onChanged » qui capturait « this » ; sans lui,
                // rien dans cette tuile ne depend de l'etat, et Flutter peut
                // la construire une fois pour toutes.
                const RadioListTile<String?>(
                  value: null,
                  title: Text('Autre'),
                  contentPadding: EdgeInsets.zero,
                ),
              ],
            ),
          ),
          if (_libre) ...[
            const SizedBox(height: HbaSpacing.sm),
            TextField(
              controller: _autre,
              autofocus: true,
              maxLength: 200,
              onChanged: (_) => setState(() {}),
              decoration: const InputDecoration(
                labelText: 'Décrivez le problème',
              ),
            ),
          ],
          const SizedBox(height: HbaSpacing.lg),
          HbaButton(
            label: 'Signaler et clôturer',
            onPressed: _motif.isEmpty
                ? null
                : () => Navigator.of(context).pop(_motif),
          ),
        ],
      ),
    );
  }
}

class _OtpSheet extends StatefulWidget {
  const _OtpSheet();

  @override
  State<_OtpSheet> createState() => _OtpSheetState();
}

class _OtpSheetState extends State<_OtpSheet> {
  final _controller = TextEditingController();

  @override
  void dispose() {
    _controller.dispose();
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
          Text('Code de remise', style: theme.textTheme.headlineMedium),
          const SizedBox(height: HbaSpacing.sm),
          Text(
            'Demandez le code au destinataire et saisissez-le.',
            style: theme.textTheme.bodyMedium,
          ),
          const SizedBox(height: HbaSpacing.lg),
          TextField(
            controller: _controller,
            autofocus: true,
            keyboardType: TextInputType.number,
            textAlign: TextAlign.center,
            inputFormatters: [
              FilteringTextInputFormatter.digitsOnly,
              LengthLimitingTextInputFormatter(6),
            ],
            style: const TextStyle(
              fontSize: 30,
              fontWeight: FontWeight.w800,
              letterSpacing: 12,
            ),
            decoration: const InputDecoration(hintText: '000000'),
          ),
          const SizedBox(height: HbaSpacing.lg),
          HbaButton(
            label: 'Confirmer la remise',
            onPressed: () => Navigator.of(context).pop(_controller.text),
          ),
        ],
      ),
    );
  }
}
