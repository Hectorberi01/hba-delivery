import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:hba_core/hba_core.dart';
import 'package:hba_ui/hba_ui.dart';

import 'delivery_providers.dart';
import 'formulaire_envoi.dart';
import 'models.dart';
import 'paiement_webview.dart';
import 'point_field.dart';

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
  final _weight = TextEditingController(text: '1000');

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
  }

  @override
  void dispose() {
    for (final controller in _champs) {
      controller
        ..removeListener(_contenuModifie)
        ..dispose();
    }
    super.dispose();
  }

  List<TextEditingController> get _champs => [
        _pickupLandmark, _pickupContact, _pickupPhone, _dropoffLandmark,
        _recipientName, _recipientPhone, _description, _weight,
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
            packageWeightGrams: int.tryParse(_weight.text) ?? 1000,
          );

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

    try {
      final (delivery, redirectUrl) =
          await ref.read(deliveryRepositoryProvider).create(
                idempotencyKey: _idempotencyKey,
                quoteId: quote.id,
                pickup: {
                  'latitude': pickup.latitude,
                  'longitude': pickup.longitude,
                  'landmark': _pickupLandmark.text.trim(),
                  'phone': _pickupPhone.text.trim(),
                  'contactName': _pickupContact.text.trim(),
                  'notes': null,
                },
                dropoff: {
                  'latitude': dropoff.latitude,
                  'longitude': dropoff.longitude,
                  'landmark': _dropoffLandmark.text.trim(),
                  'phone': _recipientPhone.text.trim(),
                  'contactName': _recipientName.text.trim(),
                  'notes': null,
                },
                recipientName: _recipientName.text.trim(),
                recipientPhone: _recipientPhone.text.trim(),
                packageDescription: _description.text.trim(),
                packageWeightGrams: int.tryParse(_weight.text) ?? 1000,
              );

      // LA COURSE EXISTE MAINTENANT SOUS CETTE CLE. A partir d'ici, toute
      // modification d'un champ doit en tirer une nouvelle : sans cela, le
      // service rendrait cette course-ci a la tentative suivante.
      _commandeEnvoyee = true;

      ref.invalidate(deliveriesProvider);
      if (!mounted) return;

      final issue = redirectUrl == null ? null : await _ouvrirPagePaiement(redirectUrl);

      if (!mounted) return;

      // TANT QUE LE PAIEMENT N'EST PAS ENGAGE, ON NE QUITTE PAS CET ECRAN.
      //
      // Le code precedent envoyait vers le suivi QUOI QU'IL ARRIVE, abandon
      // compris — le commentaire juste au-dessus expliquait pourtant qu'il ne
      // fallait pas : rien ne cherche de livreur avant PAID, et le client
      // attendait donc devant un suivi qui n'avancerait jamais.
      //
      // ET LA COMMANDE NE SURVIT PAS A CETTE SORTIE : le service l'abandonne
      // au bout d'un quart d'heure, exactement le temps de vie du devis. Le
      // message le dit, parce qu'un client qui revient deux heures plus tard
      // ne doit pas croire que sa course l'attend.
      //
      // REPRENDRE EST SANS RISQUE : la meme cle d'idempotence rend la meme
      // livraison ET la meme adresse de paiement, jamais une seconde course.
      if (redirectUrl != null && issue != IssuePaiement.retour) {
        setState(() => _error = switch (issue) {
              IssuePaiement.abandon =>
                'Paiement interrompu. La commande sera abandonnée dans '
                    '$delaiDePaiementMinutes minutes : reprenez le paiement '
                    'pour la lancer.',
              IssuePaiement.erreur =>
                "La page de paiement n'a pas pu se charger. Réessayez.",
              _ => "Impossible d'ouvrir la page de paiement. Réessayez.",
            });
        return;
      }

      if (redirectUrl != null) {
        // ALLE AU BOUT DE L'ECRAN, ET RIEN DE PLUS. Le client n'a toujours
        // rien prouve : le suivi ne passera en PAID que sur le webhook signe,
        // relu chez le fournisseur (ADR 0017). « Confirmation en cours » est
        // la chose la plus forte qu'on ait le droit d'ecrire.
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Paiement en cours de confirmation.')),
        );
      }

      // ON CONFIE L'IDENTIFIANT A L'ACCUEIL AVANT DE L'Y ENVOYER. Une course
      // non payee ne figure plus dans la liste rendue au client : sans ce
      // marqueur, l'accueil ne saurait rien d'elle et le client, revenant de
      // sa page de paiement, ne verrait plus aucune trace de sa commande.
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

  /// Ouvre la page de paiement DANS l'application, et rend ce qu'il en est
  /// advenu.
  ///
  /// C'ETAIT LE NAVIGATEUR DU TELEPHONE, ET LA RAISON ECRITE ICI ETAIT :
  /// « le paiement mobile money passe souvent par l'application de
  /// l'operateur, qui a besoin de reprendre la main sur l'ecran ». La crainte
  /// etait juste ; elle est desormais traitee dans [PaiementWebView], qui
  /// confie au systeme tout ce qui n'est pas http(s) — un code USSD, le schema
  /// propre d'un operateur — au lieu de tenter de l'afficher.
  ///
  /// CE QUE LE NAVIGATEUR EXTERNE COUTAIT. Quand l'operateur rendait la main,
  /// le client revenait dans Safari, devant une page « Merci », et devait
  /// remarquer tout seul le lien de retour vers l'application. Rien ne l'y
  /// ramenait : aucun schema d'URL n'est declare, ni sur iOS ni sur Android.
  /// Un client qui ne revient pas ne voit jamais sa course partir.
  Future<IssuePaiement?> _ouvrirPagePaiement(String url) {
    if (Uri.tryParse(url) == null) return Future.value(null);

    return Navigator.of(context).push<IssuePaiement>(
      MaterialPageRoute(builder: (_) => PaiementWebView(url: url)),
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final quote = _quote;

    return Scaffold(
      appBar: AppBar(title: const Text('Envoyer un colis'), centerTitle: true),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(
            HbaSpacing.gutter,
            HbaSpacing.md,
            HbaSpacing.gutter,
            HbaSpacing.xl,
          ),
          children: [
            // LES DEUX POINTS SONT RELIES, LE COLIS NE L'EST PAS. Le trait dit
            // un trajet ; l'enrouler autour de la troisieme carte laisserait
            // croire que le contenu du colis est une etape du chemin.
            Chronologie(
              child: Column(
                children: [
                  SectionEnvoi(
                    icone: Icons.place_outlined,
                    titre: 'Point de collecte',
                    enfants: [
                      PointField(
                        label: 'Point de départ',
                        hint: 'Choisir sur la carte',
                        value: _pickup,
                        // LE PROCHAIN GESTE EST MIS EN AVANT. La maquette
                        // montrait la carte de livraison encadree alors que les
                        // deux points etaient vides : je l'ai lue comme un etat
                        // de focus, pas comme une regle. Ce qui est encadre
                        // ici, c'est ce qu'il reste a faire.
                        accentue: _pickup == null,
                        onChanged: (point) {
                          setState(() => _pickup = point);
                          _invalidateQuote();
                        },
                      ),
                      const SizedBox(height: HbaSpacing.md),
                      ChampEnvoi(
                        intitule: 'Repère',
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
                                decoration: const InputDecoration(
                                  hintText: 'Nom du contact',
                                ),
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
                                decoration:
                                    const InputDecoration(hintText: '+229...'),
                              ),
                            ),
                          ),
                        ],
                      ),
                    ],
                  ),
                  const SizedBox(height: HbaSpacing.md),
                  SectionEnvoi(
                    icone: Icons.flag_outlined,
                    titre: 'Point de livraison',
                    enfants: [
                      PointField(
                        label: 'Destination de livraison',
                        hint: 'Choisir sur la carte',
                        value: _dropoff,
                        accentue: _pickup != null && _dropoff == null,
                        onChanged: (point) {
                          setState(() => _dropoff = point);
                          _invalidateQuote();
                        },
                      ),
                      const SizedBox(height: HbaSpacing.md),
                      ChampEnvoi(
                        intitule: 'Repère de livraison',
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
                                decoration: const InputDecoration(
                                  hintText: 'Nom complet',
                                ),
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
                                decoration:
                                    const InputDecoration(hintText: '+229...'),
                              ),
                            ),
                          ),
                        ],
                      ),
                    ],
                  ),
                ],
              ),
            ),

            const SizedBox(height: HbaSpacing.md),
            Padding(
              padding: const EdgeInsets.only(left: Chronologie.gouttiere),
              child: SectionEnvoi(
                icone: Icons.inventory_2_outlined,
                titre: 'Informations colis',
                enfants: [
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Expanded(
                        flex: 3,
                        child: ChampEnvoi(
                          intitule: 'Contenu',
                          enfant: TextField(
                            controller: _description,
                            decoration: const InputDecoration(
                              hintText: 'ex. documents',
                            ),
                          ),
                        ),
                      ),
                      const SizedBox(width: HbaSpacing.sm),
                      Expanded(
                        flex: 2,
                        child: ChampEnvoi(
                          intitule: 'Poids',
                          enfant: TextField(
                            controller: _weight,
                            keyboardType: TextInputType.number,
                            inputFormatters: [
                              FilteringTextInputFormatter.digitsOnly,
                            ],
                            onChanged: (_) => _invalidateQuote(),
                            decoration: const InputDecoration(
                              hintText: '1000',
                              suffixText: 'g',
                            ),
                          ),
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),

            if (_error != null) ...[
              const SizedBox(height: HbaSpacing.md),
              Text(
                _error!,
                style:
                    theme.textTheme.bodyMedium?.copyWith(color: HbaColors.danger),
              ),
            ],

            const SizedBox(height: HbaSpacing.lg),
            if (quote == null)
              HbaButton(label: 'Voir le prix', busy: _busy, onPressed: _getQuote)
            else ...[
              HbaCard(
                highlighted: true,
                padding: const EdgeInsets.all(HbaSpacing.lg),
                child: Row(
                  children: [
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text('Prix de la course',
                              style: theme.textTheme.bodyMedium),
                          Text(
                            Xof.format(quote.totalXof),
                            style: theme.textTheme.displaySmall
                                ?.copyWith(color: HbaColors.primary),
                          ),
                        ],
                      ),
                    ),
                    Text(
                      '${(quote.distanceMeters / 1000).toStringAsFixed(1).replaceAll('.', ',')} km',
                      style: theme.textTheme.bodyMedium,
                    ),
                  ],
                ),
              ),
              const SizedBox(height: HbaSpacing.md),
              HbaButton(
                label: 'Confirmer et payer',
                busy: _busy,
                onPressed: _create,
              ),
            ],
          ],
        ),
      ),
    );
  }
}
