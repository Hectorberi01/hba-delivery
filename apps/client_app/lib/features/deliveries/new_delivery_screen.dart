import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import '../../core/paiement_en_attente.dart';
import '../../core/reprise_de_paiement.dart';
import '../commande/elements.dart';
import '../commande/feuille_modale.dart';
import '../profil/adresses.dart';
import 'adresses_enregistrees.dart';
import 'adresses_recentes.dart';
import 'delivery_providers.dart';
import 'etapes_envoi.dart';
import 'formulaire_envoi.dart';
import 'models.dart';
import 'paiement_webview.dart';
import 'point_field.dart';
import 'poids_du_colis.dart';
import 'taille_du_colis.dart';
import 'vehicule_du_colis.dart';

/// Creation d'une livraison : adresses, destinataire, devis, paiement.
class NewDeliveryScreen extends ConsumerStatefulWidget {
  const NewDeliveryScreen({super.key});

  @override
  ConsumerState<NewDeliveryScreen> createState() => _NewDeliveryScreenState();
}

class _NewDeliveryScreenState extends ConsumerState<NewDeliveryScreen> {
  PickedPoint? _pickup;
  PickedPoint? _dropoff;

  final _pickupLandmark = TextEditingController();
  final _pickupContact = TextEditingController();
  final _pickupPhone = TextEditingController();

  final _dropoffLandmark = TextEditingController();

  final _recipientName = TextEditingController();
  final _recipientPhone = TextEditingController();
  final _description = TextEditingController();

  /// LA TAILLE REMPLACE LE CHAMP DE POIDS, et le poids part quand meme.
  /// Personne ne sait ce que pese son colis ; trois objets familiers posent la
  /// meme question dans des termes auxquels on peut repondre. Voir
  /// [TailleDuColis], qui porte les trois poids de reference.
  TailleDuColis _taille = TailleDuColis.petit;

  /// Le vehicule demande, ou nul tant que le client n'en demande aucun.
  ///
  /// NUL AU DEPART, ET CE N'EST PAS UN OUBLI. Preselectionner la moto ferait
  /// d'un choix facultatif un choix subi : le client ne saurait plus s'il a
  /// demande une moto ou si l'application l'a decide pour lui. Voir
  /// [VehiculeDuColis].
  VehiculeDuColis? _vehicule;

  /// Le poids declare, en grammes.
  ///
  /// IL PART AU DEVIS ET A LA CREATION, et c'est le meme des deux cotes : un
  /// prix etabli sur un poids et une course creee sur un autre, c'est le
  /// service qui refuse en QUOTE_TRIP_MISMATCH, ou pire, qui accepte.
  int _poids = TailleDuColis.petit.defautGrammes;

  /// L'adresse enregistree d'ou vient le point de livraison, s'il en vient.
  ///
  /// SERT A LA PASTILLE DE LA LISTE, RIEN DE PLUS. Le formulaire n'envoie que
  /// des coordonnees : une adresse favorite n'est qu'un raccourci pour les
  /// poser, jamais une reference que la course porterait.
  AdresseFavorite? _adresseChoisie;

  /// Le nom en cours d'enregistrement, ou nul au repos.
  NomDAdresse? _enregistrement;

  /// L'etape affichee, de zero a deux.
  int _etape = 0;

  /// LES TROIS ETAPES, DANS L'ORDRE OU LE COLIS VOYAGE. L'enlevement d'abord
  /// parce que c'est le seul point que le client connait a coup sur — il y est
  /// — et que le reste n'a de sens qu'une fois le depart pose.
  static const _etapes = [
    EtapeDEnvoi(titre: 'Enlèvement', sousTitre: 'Choisir sur la carte'),
    EtapeDEnvoi(titre: 'Livraison', sousTitre: 'Adresse ou point de repère'),
    EtapeDEnvoi(titre: 'Détails', sousTitre: 'Colis et véhicule'),
  ];

  Quote? _quote;
  bool _busy = false;
  String? _error;

  /// La cle d'idempotence de la TENTATIVE EN COURS.
  ///
  /// REJOUER LA MEME CLE REND LA MEME LIVRAISON, et c'est ce qui rend la reprise
  /// d'un paiement interrompu sans risque : la meme adresse de paiement, jamais
  /// une seconde course.
  ///
  /// ELLE ETAIT TIREE UNE SEULE FOIS A LA CONSTRUCTION DE L'ECRAN, ET C'ETAIT UN
  /// PIEGE. L'ecran ne se ferme plus apres un abandon de paiement : le client
  /// corrigeait son point de livraison ou le telephone du destinataire,
  /// redemandait un devis, reconfirmait — et le service, reconnaissant la cle,
  /// lui rendait la PREMIERE course, avec l'ancienne adresse et l'ancien prix,
  /// sans un mot. Le colis serait parti au mauvais endroit.
  ///
  /// La cle vit donc le temps d'une tentative : elle est renouvelee des que le
  /// contenu change APRES un envoi. Voir _contenuModifie.
  String _idempotencyKey = ApiClient.newIdempotencyKey();

  /// Une creation a deja ete envoyee avec la cle courante.
  bool _commandeEnvoyee = false;

  @override
  void initState() {
    super.initState();

    // TOUS LES CHAMPS, PAS SEULEMENT LES POINTS. Le devis ne depend que des
    // deux points, mais la course, elle, porte aussi le destinataire, son
    // telephone et la description : les changer sans renouveler la cle
    // ramenerait exactement le defaut qu'on corrige.
    for (final controller in _champs) {
      controller.addListener(_contenuModifie);
    }

    // LES DEUX REPERES S'AFFICHENT DANS LA CARTE DU TRAJET, DONC ILS DOIVENT LA
    // REDESSINER. _contenuModifie ne le fait pas : il ne renouvelle la cle
    // qu'apres un envoi, et sort sans rien toucher le reste du temps. Sans ces
    // deux ecoutes, le client tape « en face de la pharmacie » et la carte
    // continue d'afficher ses coordonnees jusqu'au prochain geste.
    _pickupLandmark.addListener(_redessiner);
    _dropoffLandmark.addListener(_redessiner);
  }

  void _redessiner() {
    if (mounted) setState(() {});
  }

  @override
  void dispose() {
    _pickupLandmark.removeListener(_redessiner);
    _dropoffLandmark.removeListener(_redessiner);

    for (final controller in _champs) {
      controller
        ..removeListener(_contenuModifie)
        ..dispose();
    }
    super.dispose();
  }

  List<TextEditingController> get _champs => [
        _pickupLandmark, _pickupContact, _pickupPhone, _dropoffLandmark,
        _recipientName, _recipientPhone, _description,
      ];

  /// Le devis depend des points : des qu'ils bougent, il ne vaut plus rien.
  void _invalidateQuote() {
    _contenuModifie();
    if (_quote != null) setState(() => _quote = null);
  }

  /// Le contenu de la commande a change : la tentative precedente n'est plus
  /// celle qu'on veut retrouver.
  ///
  /// SANS ENVOI PRECEDENT, IL N'Y A RIEN A RENOUVELER. Tirer une cle a chaque
  /// frappe ne couterait rien de visible, mais ferait perdre la reprise du
  /// paiement dans le cas ou le client corrige une faute de frappe sans avoir
  /// rien envoye.
  void _contenuModifie() {
    if (!_commandeEnvoyee) return;

    _idempotencyKey = ApiClient.newIdempotencyKey();
    _commandeEnvoyee = false;
  }

  Future<void> _getQuote() async {
    final pickup = _pickup;
    final dropoff = _dropoff;

    if (pickup == null || dropoff == null) {
      setState(() => _error = 'Choisissez les deux points sur la carte.');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    try {
      final quote = await ref.read(deliveryRepositoryProvider).quote(
            pickupLatitude: pickup.latitude,
            pickupLongitude: pickup.longitude,
            dropoffLatitude: dropoff.latitude,
            dropoffLongitude: dropoff.longitude,
            packageWeightGrams: _poids,

            // LE VEHICULE PART S'IL A ETE DEMANDE, ET SEULEMENT ALORS.
            //
            // CE QU'IL CHANGE, ET CE QU'IL NE CHANGE PAS. Il ne sert qu'a
            // choisir la GRILLE de tarification ; il ne figure pas dans le
            // corps de creation et le dispatch ne l'a jamais lu — demander une
            // voiture ne garantit donc pas qu'une voiture vienne. Ce qui fait
            // l'ecart entre une enveloppe et une charge de tricycle, c'est le
            // POIDS, qui part toujours : au-dela du poids inclus, la grille
            // facture au kilogramme.
            //
            // SANS DEMANDE, PRICING APPLIQUE SA REGLE ECRITE : la moto, la
            // grille la moins chere. Ne rien choisir ne coute jamais plus cher
            // au client, et c'est pour cela que le selecteur peut rester vide.
            vehicleType: _vehicule?.nom,

            // DIT EXPLICITEMENT, MEME SI C'EST LE DEFAUT. Cet ecran envoie des
            // colis ; le jour ou le meme devis servira aux courses, l'oubli ne
            // se verra pas a la creation mais ici.
            serviceType: 'colis',
          );

      // LE DEVIS RENDU DOIT ETRE UN DEVIS DE COLIS. S'il revient avec une autre
      // nature, le creer echouerait en QUOTE_SERVICE_MISMATCH sans que le client
      // comprenne : mieux vaut le dire tout de suite et ne pas l'afficher.
      if (!quote.estUneLivraison) {
        if (mounted) {
          setState(() => _error =
              "Ce prix n'est pas celui d'une livraison de colis. Réessayez.");
        }
        return;
      }

      if (mounted) setState(() => _quote = quote);
    } on OfflineException {
      if (mounted) setState(() => _error = 'Pas de réseau. Réessayez.');
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _create() async {
    final quote = _quote;
    if (quote == null) return;

    // Un devis expire. Mieux vaut en redemander un que d'envoyer au service
    // une creation qu'il refusera.
    if (quote.hasExpiredAt(DateTime.now())) {
      setState(() {
        _quote = null;
        _error = 'Le devis a expire. Demandez-en un nouveau.';
      });
      return;
    }

    if (_recipientName.text.trim().isEmpty || _recipientPhone.text.trim().isEmpty) {
      setState(() => _error = 'Le destinataire et son téléphone sont obligatoires.');
      return;
    }

    setState(() {
      _busy = true;
      _error = null;
    });

    final pickup = _pickup!;
    final dropoff = _dropoff!;

    // LE CORPS EST COMPOSE UNE FOIS, PUIS ENVOYE **ET** GARDE. C'est ce qui rend
    // la reprise possible apres un redemarrage : rejouer la meme cle avec le meme
    // corps rend la MEME livraison et la MEME adresse de paiement, sans rien
    // creer. Le recomposer plus tard depuis les champs de l'ecran ne marcherait
    // pas — l'ecran n'existe plus.
    final corps = <String, Object?>{
      'quoteId': quote.id,
      'pickup': {
        'latitude': pickup.latitude,
        'longitude': pickup.longitude,
        'landmark': _pickupLandmark.text.trim(),
        'phone': _pickupPhone.text.trim(),
        'contactName': _pickupContact.text.trim(),
        'notes': null,
      },
      'dropoff': {
        'latitude': dropoff.latitude,
        'longitude': dropoff.longitude,
        'landmark': _dropoffLandmark.text.trim(),
        'phone': _recipientPhone.text.trim(),
        'contactName': _recipientName.text.trim(),
        'notes': null,
      },
      'recipientName': _recipientName.text.trim(),
      'recipientPhone': _recipientPhone.text.trim(),
      'packageDescription': _description.text.trim(),
      'packageWeightGrams': _poids,

    };

    try {
      final (delivery, adresse) = await ref
          .read(deliveryRepositoryProvider)
          .envoyer(idempotencyKey: _idempotencyKey, corps: corps);

      // LA COURSE EXISTE MAINTENANT SOUS CETTE CLE. A partir d'ici, toute
      // modification d'un champ doit en tirer une nouvelle : sans cela, le
      // service rendrait cette course-ci a la tentative suivante.
      _commandeEnvoyee = true;

      // LES DEUX POINTS ENTRENT DANS LES RECENTES, ICI ET PAS AVANT.
      //
      // A LA CREATION, PAS AU CHOIX DU POINT. Un client qui ouvre la carte,
      // tatonne, puis renonce ne doit pas retrouver ses hesitations dans sa
      // liste — elle ne vaut que si elle ne contient que des endroits ou il a
      // VRAIMENT envoye quelque chose.
      //
      // SANS ATTENDRE, ET SANS POUVOIR FAIRE ECHOUER L'ENVOI. C'est un confort
      // local : une ecriture de stockage qui tourne mal ne doit pas emporter
      // une commande qui vient de partir.
      final recentes = ref.read(adressesRecentesStoreProvider);
      unawaited(recentes.retenir(
        point: pickup,
        repere: _pickupLandmark.text,
      ));
      unawaited(recentes.retenir(
        point: dropoff,
        repere: _dropoffLandmark.text,
      ));
      ref.invalidate(adressesRecentesProvider);

      // GARDE AVANT D'OUVRIR LA PAGE DE PAIEMENT, ET L'ORDRE EST TOUT
      // L'INTERET : c'est pendant cette page que le systeme peut tuer
      // l'application, et le cas a couvrir est justement celui ou l'on ne
      // revient pas.
      await ref.read(paiementsEnAttenteProvider).poser(CommandeAPayer(
            nature: 'colis',
            id: delivery.id,
            cle: _idempotencyKey,
            corps: corps,
            envoyeeLe: DateTime.now().toUtc(),
          ));

      ref
        ..invalidate(deliveriesProvider)
        ..invalidate(commandesAPayerProvider);
      if (!mounted) return;

      // AUCUNE ADRESSE VEUT DIRE QU'IL N'Y A RIEN A OUVRIR. La commande existe
      // quand meme — le devis etait consomme — et la reprise est le chemin pour
      // la relancer.
      if (adresse == null) {
        setState(() => _error =
            "Le paiement n'a pas pu s'ouvrir. La commande sera abandonnée dans "
            '$delaiDePaiementMinutes minutes : reprenez le paiement pour la '
            'lancer.');
        return;
      }

      // SORTIR DE CETTE PAGE NE PROUVE AUCUN PAIEMENT, ni encaisse ni refuse :
      // seul le webhook signe fait foi (ADR 0017). On n'en lit donc pas l'issue
      // pour decider quoi que ce soit.
      await Navigator.of(context).push<IssuePaiement>(
        MaterialPageRoute(builder: (_) => PaiementWebView(url: adresse)),
      );

      if (!mounted) return;

      // L'ENTREE LOCALE N'A PLUS D'OBJET DES QUE LE SERVICE A ENCAISSE, et
      // c'est a lui qu'on le demande : l'application ne voit jamais le paiement
      // se faire (ADR 0017). Sans cette ligne, l'accueil proposerait de
      // reprendre le paiement d'une commande deja reglee jusqu'a la purge du
      // quart d'heure.
      unawaited(oublierSiReglee(ref, estUneCourse: false, id: delivery.id));

      // ON CONFIE L'IDENTIFIANT A L'ACCUEIL AVANT DE L'Y ENVOYER. Une course
      // non payee ne figure plus dans la liste rendue au client : sans ce
      // marqueur, l'accueil ne saurait rien d'elle et le client, revenant de
      // l'invite de son operateur, ne verrait plus aucune trace de sa commande.
      ref.read(courseEnAttenteProvider.notifier).state = delivery.id;

      // L'ACCUEIL, ET PLUS LE SUIVI. C'est la que l'attente se regarde
      // desormais ; le suivi reprend la main des qu'un livreur a accepte.
      context.go('/');
    } on OfflineException {
      if (mounted) setState(() => _error = 'Pas de réseau. Réessayez.');
    } on ApiException catch (error) {
      if (!mounted) return;

      setState(() {
        _error = error.message;

        // UN DEVIS REFUSE NE RESSERVIRA JAMAIS, et le laisser a l'ecran envoie
        // le client se cogner au meme mur. Expire, deja consomme, ou etabli
        // pour un autre trajet : dans les trois cas la seule issue est un
        // nouveau prix, et c'est ce que dit le message. On efface donc le devis
        // pour que le bouton en redemande un au lieu de reessayer celui-la.
        if (const {'QUOTE_EXPIRED', 'QUOTE_NOT_USABLE', 'QUOTE_TRIP_MISMATCH'}
            .contains(error.code)) {
          _quote = null;
        }
      });
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  /// Ce qui manque pour quitter cette etape, ou nul si elle est complete.
  ///
  /// LA VALIDATION EST PAR ETAPE, ET C'EST TOUT L'INTERET DU DECOUPAGE. Un
  /// formulaire d'un seul tenant ne peut dire qu'a la fin ce qui manque au
  /// debut ; ici, on ne passe pas devant un champ vide.
  String? _manque(int etape) => switch (etape) {
        0 => _pickup == null
            ? "Posez le point d'enlèvement sur la carte."
            : null,
        1 => switch ((
            _dropoff,
            _recipientName.text.trim(),
            _recipientPhone.text.trim(),
          )) {
            (null, _, _) => 'Posez le point de livraison sur la carte.',
            (_, '', _) || (_, _, '') =>
              'Le destinataire et son téléphone sont obligatoires.',
            _ => null,
          },
        _ => null,
      };

  void _continuer() {
    final manque = _manque(_etape);
    if (manque != null) {
      setState(() => _error = manque);
      return;
    }

    setState(() {
      _error = null;
      _etape += 1;
    });
  }

  /// Revient a une etape DEJA FAITE.
  ///
  /// L'ERREUR EST EFFACEE AU PASSAGE. Elle parlait de l'etape qu'on quitte :
  /// la laisser afficherait « posez le point de livraison » au-dessus du
  /// formulaire d'enlevement.
  void _revenir(int etape) => setState(() {
        _error = null;
        _etape = etape;
      });

  /// Recopie le repere qui accompagne un point choisi dans la feuille.
  ///
  /// ON N'ECRASE JAMAIS CE QUE LE CLIENT A DEJA TAPE. Il peut avoir precise
  /// « portail bleu, deuxieme etage » avant de corriger le point ; lui rendre
  /// le repere brut de l'adresse enregistree effacerait son ajout sans
  /// prevenir.
  ///
  /// PAR LA VALEUR ENTIERE, PAS PAR .text : ecrire dans « text » invalide la
  /// selection du controleur, et le curseur repart au debut du champ des que le
  /// client y touche.
  void _ecrireLeRepere(TextEditingController champ, String repere) {
    final texte = repere.trim();
    if (texte.isEmpty || champ.text.trim().isNotEmpty) return;

    champ.value = TextEditingValue(
      text: texte,
      selection: TextSelection.collapsed(offset: texte.length),
    );
  }

  /// Pose le point de livraison depuis une adresse enregistree.
  void _choisirAdresse(AdresseFavorite adresse) {
    setState(() {
      _adresseChoisie = adresse;
      _dropoff = PickedPoint(
        latitude: adresse.latitude,
        longitude: adresse.longitude,
      );

      final repere = adresse.repere.trim();
      if (repere.isNotEmpty) {
        // PAR LA VALEUR ENTIERE, PAS PAR .text. Ecrire dans « text » invalide
        // la selection du controleur, et le curseur repart au debut du champ
        // des que le client y touche.
        _dropoffLandmark.value = TextEditingValue(
          text: repere,
          selection: TextSelection.collapsed(offset: repere.length),
        );
      }
    });

    _invalidateQuote();
  }

  /// Enregistre le point de livraison courant sous un nom.
  ///
  /// LE LIBELLE EST LE SEUL CHAMP QUI PORTE LE NOM. Voir [NomDAdresse] : le
  /// service ne connait pas de nature d'adresse, donc « Domicile » n'est qu'un
  /// mot ecrit dans le libelle.
  Future<void> _enregistrerAdresse(NomDAdresse nom) async {
    final point = _dropoff;
    if (point == null) return;

    final libelle =
        nom == NomDAdresse.autre ? await _demanderUnNom() : nom.libelle;
    if (libelle == null || libelle.isEmpty || !mounted) return;

    setState(() {
      _enregistrement = nom;
      _error = null;
    });

    try {
      await ref.read(adressesRepositoryProvider).ajouter(
            libelle: libelle,
            repere: _dropoffLandmark.text.trim(),
            latitude: point.latitude,
            longitude: point.longitude,

            // JAMAIS PAR DEFAUT DEPUIS CET ECRAN. Poser une adresse par defaut
            // retire le drapeau de l'ancienne : ce n'est pas un effet qu'on
            // produit en passant, au milieu d'une commande.
            parDefaut: false,
          );
      ref.invalidate(adressesProvider);
    } on OfflineException {
      if (mounted) setState(() => _error = 'Pas de réseau. Réessayez.');
    } on ApiException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } finally {
      if (mounted) setState(() => _enregistrement = null);
    }
  }

  /// Demande un nom libre pour « Autre ».
  Future<String?> _demanderUnNom() async {
    final champ = TextEditingController();

    try {
      final nom = await showDialog<String>(
        context: context,
        builder: (contexte) => AlertDialog(
          title: const Text('Nom de l’adresse'),
          content: TextField(
            controller: champ,
            autofocus: true,
            textCapitalization: TextCapitalization.words,
            decoration: const InputDecoration(hintText: 'ex. Chez ma mère'),
            onSubmitted: (texte) =>
                Navigator.of(contexte).pop(texte.trim()),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(contexte).pop(),
              child: const Text('Annuler'),
            ),
            TextButton(
              onPressed: () =>
                  Navigator.of(contexte).pop(champ.text.trim()),
              child: const Text('Enregistrer'),
            ),
          ],
        ),
      );
      return nom;
    } finally {
      champ.dispose();
    }
  }

  @override
  Widget build(BuildContext context) {
    final quote = _quote;
    final adresses =
        ref.watch(adressesProvider).valueOrNull ?? const <AdresseFavorite>[];

    return FeuilleModale(
      titre: 'Envoyer un colis',
      icone: Icons.inventory_2_outlined,

      // LA FEUILLE NE SE FERME PAS PENDANT L'ENVOI. La detruire a cet instant
      // emporterait l'ecran qui attend la reponse, et avec lui l'adresse de
      // paiement qu'il allait ouvrir.
      retourPossible: !_busy,

      // LE PIED EST HORS DU DEFILEMENT. Le bouton vivait en bas d'un formulaire
      // de dix champs : il fallait defiler pour le retrouver, et le prix avait
      // quitte l'ecran au moment de s'engager.
      pied: _pied(quote),
      enfant: ListView(
        shrinkWrap: true,
        padding: const EdgeInsets.fromLTRB(
          HbaSpacing.gutter,
          HbaSpacing.sm,
          HbaSpacing.gutter,
          HbaSpacing.md,
        ),
        children: [
          BandeauDEtapes(
            etapes: _etapes,
            courante: _etape,
            onRevenir: _revenir,
          ),
          const SizedBox(height: HbaSpacing.lg),

          // UNE SEULE ETAPE EST CONSTRUITE A LA FOIS, et les champs des autres
          // survivent quand meme : ce sont des TextEditingController portes par
          // l'etat de l'ecran, pas par les widgets. Rien n'est perdu en
          // revenant en arriere.
          ...switch (_etape) {
            0 => _etapeEnlevement(),
            1 => _etapeLivraison(adresses),
            _ => _etapeDetails(),
          },
          if (_error != null) ...[
            const SizedBox(height: HbaSpacing.md),
            Text(
              _error!,
              style: Theme.of(context)
                  .textTheme
                  .bodyMedium
                  ?.copyWith(color: HbaColors.danger),
            ),
          ],
        ],
      ),
    );
  }

  /// Le pied, qui change de nature a la derniere etape.
  ///
  /// LE RETOUR N'EST PAS DANS LE PIED DE LA DERNIERE ETAPE, et c'est voulu :
  /// le prix y occupe la moitie gauche, et trois elements sur une ligne de
  /// telephone donnent trois cibles trop etroites. On revient en arriere en
  /// appuyant sur une etape du bandeau, qui est fait pour cela.
  Widget _pied(Quote? quote) {
    if (_etape < _etapes.length - 1) {
      return PiedDeCommande(
        action: Row(
          children: [
            if (_etape > 0) ...[
              Expanded(
                child: HbaButton(
                  label: 'Retour',
                  tone: HbaButtonTone.neutral,
                  onPressed: () => _revenir(_etape - 1),
                ),
              ),
              const SizedBox(width: HbaSpacing.sm),
            ],
            Expanded(
              flex: 2,
              child: HbaButton(label: 'Continuer', onPressed: _continuer),
            ),
          ],
        ),
      );
    }

    return PiedDeCommande(
      montant: quote == null ? null : Xof.format(quote.totalXof),

      // CE QUI A FAIT CE PRIX, EN TROIS MOTS. Le poids plutot que la taille :
      // c'est lui qui part a la tarification, et deux colis « Moyen » de deux
      // et de dix-huit kilos ne coutent pas la meme chose. Le vehicule
      // seulement s'il a ete demande.
      detail: quote == null
          ? null
          : [
              SelecteurDePoids.libelle(_poids),
              if (_vehicule != null) _vehicule!.libelle,
              'payé par vous',
            ].join(' · '),
      action: quote == null
          ? HbaButton(label: 'Voir le prix', busy: _busy, onPressed: _getQuote)
          : HbaButton(label: 'Confirmer', busy: _busy, onPressed: _create),
    );
  }

  List<Widget> _etapeEnlevement() => [
        PointField(
          label: "Point d'enlèvement",
          hint: 'Choisir sur la carte',
          value: _pickup,

          // ACCENTUE : c'est le premier geste de la premiere etape, et rien
          // d'autre sur cet ecran ne se dispute l'attention.
          accentue: true,
          onChanged: (choisi) {
            setState(() => _pickup = choisi.point);
            _ecrireLeRepere(_pickupLandmark, choisi.repere);
            _invalidateQuote();
          },
        ),
        const SizedBox(height: HbaSpacing.md),
        ChampEnvoi(
          intitule: "Repère de l'enlèvement",
          enfant: TextField(
            controller: _pickupLandmark,
            decoration: const InputDecoration(
              hintText: 'ex. Carre 442 en face de la pharmacie',
            ),
          ),
        ),
        const SizedBox(height: HbaSpacing.md),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: ChampEnvoi(
                intitule: 'Contact sur place',
                enfant: TextField(
                  controller: _pickupContact,
                  textCapitalization: TextCapitalization.words,
                  decoration: const InputDecoration(hintText: 'Nom'),
                ),
              ),
            ),
            const SizedBox(width: HbaSpacing.sm),
            Expanded(
              child: ChampEnvoi(
                intitule: 'Téléphone',
                enfant: TextField(
                  controller: _pickupPhone,
                  keyboardType: TextInputType.phone,
                  decoration: const InputDecoration(hintText: '+229 01 …'),
                ),
              ),
            ),
          ],
        ),
      ];

  List<Widget> _etapeLivraison(List<AdresseFavorite> adresses) => [
        PointField(
          label: 'Point de livraison',
          hint: 'Adresse ou point de repère',
          value: _dropoff,
          accentue: true,
          onChanged: (choisi) {
            setState(() {
              _dropoff = choisi.point;

              // POSER UN POINT A LA MAIN DETACHE L'ADRESSE ENREGISTREE. Sans
              // cela, la pastille resterait allumee sur « Domicile » alors
              // que le colis part ailleurs.
              _adresseChoisie = null;
            });
            _ecrireLeRepere(_dropoffLandmark, choisi.repere);
            _invalidateQuote();
          },
        ),
        const SizedBox(height: HbaSpacing.md),
        ChampEnvoi(
          intitule: 'Repère de la livraison',
          enfant: TextField(
            controller: _dropoffLandmark,
            decoration: const InputDecoration(
              hintText: 'ex. A cote du portail bleu',
            ),
          ),
        ),
        const SizedBox(height: HbaSpacing.md),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Expanded(
              child: ChampEnvoi(
                intitule: 'Destinataire',
                enfant: TextField(
                  controller: _recipientName,
                  textCapitalization: TextCapitalization.words,
                  decoration: const InputDecoration(hintText: 'Nom'),
                ),
              ),
            ),
            const SizedBox(width: HbaSpacing.sm),
            Expanded(
              child: ChampEnvoi(
                intitule: 'Téléphone',
                enfant: TextField(
                  controller: _recipientPhone,
                  keyboardType: TextInputType.phone,
                  decoration: const InputDecoration(hintText: '+229 01 …'),
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: HbaSpacing.lg),
        AdressesEnregistrees(
          adresses: adresses,
          choisie: _adresseChoisie,
          pointPose: _dropoff != null,
          enregistrement: _enregistrement,
          onChoisir: _choisirAdresse,
          onEnregistrer: _enregistrerAdresse,
        ),
      ];

  List<Widget> _etapeDetails() => [
        ChampEnvoi(
          intitule: 'Contenu',
          enfant: TextField(
            controller: _description,
            decoration: const InputDecoration(hintText: 'ex. documents'),
          ),
        ),
        const SizedBox(height: HbaSpacing.lg),
        const _Intitule('Taille du colis'),
        const SizedBox(height: HbaSpacing.sm),

        // LA TAILLE BORNE LE POIDS ET POSE SA VALEUR DE DEPART. Elle ne part
        // plus telle quelle a la tarification : c'est le poids qui part.
        for (final taille in TailleDuColis.values) ...[
          CarteDeChoix(
            icone: taille.icone,
            titre: taille.libelle,
            detail: taille.exemple,
            valeur: taille.plafondLibelle,
            choisie: _taille == taille,
            onTap: () {
              setState(() {
                _taille = taille;

                // LE POIDS REPART DU DEFAUT DE LA NOUVELLE TAILLE. Le garder
                // laisserait quinze kilos sous « Petit », dont le plafond est
                // cinq : le selecteur s'ouvrirait hors de ses bornes.
                _poids = taille.defautGrammes;
              });
              _invalidateQuote();
            },
          ),
          const SizedBox(height: HbaSpacing.sm),
        ],
        const SizedBox(height: HbaSpacing.md),
        const _Intitule('Poids estimé'),
        const SizedBox(height: HbaSpacing.xs),
        _Explication(
          'Entre 100 g et ${SelecteurDePoids.libelle(_taille.plafondGrammes)}. '
          'C’est ce poids qui fixe le prix.',
        ),
        const SizedBox(height: HbaSpacing.sm),
        SelecteurDePoids(
          grammes: _poids,
          plafondGrammes: _taille.plafondGrammes,
          onChange: (grammes) {
            setState(() => _poids = grammes);
            _invalidateQuote();
          },
        ),
        const SizedBox(height: HbaSpacing.lg),
        const _Intitule('Véhicule souhaité'),
        const SizedBox(height: HbaSpacing.xs),

        // CE QUE FAIT L'ABSENCE DE CHOIX EST ECRIT, PAS DEVINE. « Facultatif »
        // seul laisse croire qu'on renonce a quelque chose ; la phrase dit
        // exactement ce qui se passe, et que cela ne coute pas plus cher.
        const _Explication(
          'Facultatif. Sans choix, le prix est celui de la moto, le moins cher.',
        ),
        const SizedBox(height: HbaSpacing.sm),

        // LE VEHICULE CHANGE LA GRILLE, DONC LE PRIX, DONC LE DEVIS — comme le
        // poids. Sans _invalidateQuote, le client verrait un tarif moto sous
        // une demande de voiture.
        for (final vehicule in VehiculeDuColis.values) ...[
          CarteDeChoix(
            icone: vehicule.icone,
            titre: vehicule.libelle,
            detail: vehicule.detail,
            choisie: _vehicule == vehicule,

            // PRESSER LE CHOIX DEJA FAIT LE RELACHE. C'est le seul geste par
            // lequel le client peut revenir a « aucun » : sans lui, le
            // caractere facultatif ne tiendrait que jusqu'au premier appui.
            onTap: () {
              setState(
                () => _vehicule = _vehicule == vehicule ? null : vehicule,
              );
              _invalidateQuote();
            },
          ),
          const SizedBox(height: HbaSpacing.sm),
        ],
      ];
}

/// Le titre d'une section du formulaire.
class _Intitule extends StatelessWidget {
  const _Intitule(this.texte);

  final String texte;

  @override
  Widget build(BuildContext context) =>
      Text(texte, style: Theme.of(context).textTheme.titleMedium);
}

/// La ligne grise qui suit un intitule et dit la regle.
class _Explication extends StatelessWidget {
  const _Explication(this.texte);

  final String texte;

  @override
  Widget build(BuildContext context) => Text(
        texte,
        style: Theme.of(context)
            .textTheme
            .bodySmall
            ?.copyWith(color: HbaColors.inkMuted),
      );
}
