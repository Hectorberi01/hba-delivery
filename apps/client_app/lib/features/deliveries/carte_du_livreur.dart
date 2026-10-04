import 'dart:async';

import 'package:flutter/material.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_ui/hba_ui.dart';

import 'suivi_temps_reel.dart';
import 'trace_itineraire.dart';

/// La carte du suivi : les deux points de la course, et le livreur qui bouge.
///
/// CE QUI N'EXISTAIT PAS AVANT LE 1er OCTOBRE 2026. L'ecran de suivi affichait un
/// nom, un vehicule, une plaque et un code de remise — jamais ou se trouvait le
/// livreur. Le sondage de dix secondes relisait la mission, pas une position : le
/// service ne savait pas la donner au client, et la matrice de visibilite ne
/// disait pas s'il y avait droit. C'est le point 37 qui l'a tranche.
///
/// PAS DE CLIPRRECT, PAS DE CLIPPATH, PAS DE BORDERRADIUS AVEC clipBehavior, PAS
/// DE MATERIAL AVEC UNE FORME autour de cette carte : sur iOS, tout ce qui est
/// peint apres un rognage de vue native disparait. Verifie au pixel le
/// 28 septembre 2026 sur l'ecran de recherche, et la lecon vaut ici.
///
/// ELLE NE S'AFFICHE QUE QUAND IL Y A QUELQUE CHOSE A MONTRER. Sans position, une
/// carte vide avec deux epingles n'apprend rien que l'adresse ecrite en dessous ne
/// dise mieux — et elle couterait le chargement d'une vue native.
class CarteDuLivreur extends StatefulWidget {
  const CarteDuLivreur({
    required this.depart,
    required this.arrivee,
    required this.suivi,
    this.trace = '',
    this.vehicule,
    super.key,
  });

  /// Les deux bouts de la mission. NULL EST UNE ABSENCE, PAS UN ZERO : la
  /// passerelle rend « 0 » quand le point manque, et une epingle au large du
  /// Ghana ne montre pas une collecte a Cotonou. Les appelants filtrent avant.
  ///
  /// TROIS CHAMPS PLUTOT QU'UNE MISSION, DEPUIS LE 1er OCTOBRE 2026. Cette carte
  /// prenait un Delivery, dont elle ne lisait que ces trois choses : les deux
  /// points et le type de vehicule. Les courses de personnes ont le meme besoin
  /// et un autre modele — recopier la carte aurait fait deux cartes a corriger.
  final LatLng? depart;
  final LatLng? arrivee;

  final SuiviEnDirect suivi;

  /// Le trace de l'itineraire restant, encode au format polyline Google. Vide
  /// quand l'ecran ne l'a pas demande, ou quand la mesure vient du repli a vol
  /// d'oiseau, qui ne dessine aucune rue.
  ///
  /// IL EST REMPLACE, JAMAIS EMPILE (point 43) : la carte n'affiche que le
  /// dernier. Accumuler les traces successifs reconstituerait la piste de
  /// deplacement que le point 37 refuse.
  final String trace;

  /// Le type brut rendu par le serveur — « Motorcycle », ou son numero. NULL
  /// QUAND RIEN N'EST DECLARE : l'epingle prend alors sa forme par defaut, au
  /// lieu d'afficher un vehicule que personne n'a declare.
  final String? vehicule;

  @override
  State<CarteDuLivreur> createState() => _CarteDuLivreurState();
}

class _CarteDuLivreurState extends State<CarteDuLivreur> {
  /// Hauteur de la carte dans la liste de suivi.
  ///
  /// ASSEZ POUR LIRE UN DEPLACEMENT, PAS ASSEZ POUR MANGER L'ECRAN. En dessous de
  /// deux cents points, deux epingles distantes de trois kilometres se touchent et
  /// la carte ne dit plus rien ; au-dela de deux cent quatre-vingts, le nom du
  /// livreur et le code de remise passent sous le pli, et ce sont eux qu'on ouvre
  /// cet ecran pour lire.
  static const double _hauteur = 240;

  GoogleMapController? _controleur;

  BitmapDescriptor? _collecte;
  BitmapDescriptor? _livraison;
  BitmapDescriptor? _vehicule;
  double? _dessineeA;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();

    final ratio = MediaQuery.devicePixelRatioOf(context);
    if (ratio == _dessineeA) return;

    _dessineeA = ratio;
    unawaited(_dessiner(ratio));
  }

  @override
  void didUpdateWidget(CarteDuLivreur ancien) {
    super.didUpdateWidget(ancien);

    final point = widget.suivi.position;

    // LA CAMERA SUIT LE LIVREUR, ET SEULEMENT LUI. Recadrer sur les trois points a
    // chaque battement ferait sauter la carte vingt fois par minute ; suivre le
    // livreur garde le mouvement lisible, et le client peut toujours deplacer la
    // vue a la main — « animateCamera » ne verrouille rien.
    if (point != null && point != ancien.suivi.position) {
      unawaited(_controleur?.animateCamera(CameraUpdate.newLatLng(point)));
    }
  }

  Future<void> _dessiner(double ratio) async {
    final collecte = await Epingles.goutte(couleur: HbaColors.primary, ratio: ratio);
    final livraison = await Epingles.goutte(couleur: HbaColors.success, ratio: ratio);

    // LE VEHICULE EN RELIEF, ET C'EST UNE DECISION DE PRODUIT DU 2 OCTOBRE 2026.
    //
    // CE CODE MONTRAIT UN DISQUE, et le commentaire qui le justifiait disait
    // ceci : le relief est fait pour la carte de l'exploitation, ou l'on compare
    // des dizaines de livreurs, tandis qu'ici il n'y en a qu'un. L'argument tient
    // pour la lisibilite ; il manquait l'autre moitie — la carte d'accueil, elle,
    // montre DEJA les livreurs alentour en relief (home_screen). Le client voyait
    // donc des vehicules en relief avant de commander, et une pastille plate des
    // qu'un livreur lui etait affecte : le meme objet changeait d'apparence au
    // moment ou il devenait le sien.
    //
    // UNE SEULE REPRESENTATION DU VEHICULE DANS TOUTE L'APPLICATION, donc, et
    // c'est celle en relief. Elle sert aussi la course de personnes : cet ecran
    // est partage avec le suivi de course (suivi_course_screen).
    //
    // LE DISQUE RESTE LE REPLI, ET IL N'EST PAS DE LA POLITESSE. Trois cas y
    // tombent : la camionnette, qui n'a pas d'image en relief ; un type que le
    // serveur ne nomme pas encore ; et un asset qui ne se charge pas. Dans les
    // trois, un disque portant le bon pictogramme vaut mieux qu'une carte sans
    // livreur.
    final fichier = Epingles.fichierDuVehicule(widget.vehicule);

    final vehicule = fichier == null
        ? await Epingles.disque(
            couleur: HbaColors.primary,
            ratio: ratio,
            vehicule: Epingles.iconeDuVehicule(widget.vehicule),
          )
        : await Epingles.vehiculeEnRelief(
              chemin: fichier,
              ratio: ratio,

              // UN LIVREUR AFFECTE TRAVAILLE : il est en ligne par definition,
              // puisqu'il a accepte cette course. L'etat « en pause » n'existe
              // que sur la carte du livreur lui-meme.
              enLigne: true,
            ) ??
            // L'ASSET N'A PAS PU SE CHARGER — « vehiculeEnRelief » rend null
            // dans ce cas plutot que de lever. On retombe sur le disque.
            await Epingles.disque(
              couleur: HbaColors.primary,
              ratio: ratio,
              vehicule: Epingles.iconeDuVehicule(widget.vehicule),
            );

    if (!mounted || ratio != _dessineeA) return;

    setState(() {
      _collecte = collecte;
      _livraison = livraison;
      _vehicule = vehicule;
    });
  }

  Set<Marker> get _reperes {
    final marqueurs = <Marker>{};
    final depart = widget.depart;
    final arrivee = widget.arrivee;

    if (depart != null) {
      marqueurs.add(Marker(
        markerId: const MarkerId('depart'),
        position: depart,
        icon: _collecte ??
            BitmapDescriptor.defaultMarkerWithHue(BitmapDescriptor.hueOrange),
      ));
    }

    if (arrivee != null) {
      marqueurs.add(Marker(
        markerId: const MarkerId('arrivee'),
        position: arrivee,
        icon: _livraison ??
            BitmapDescriptor.defaultMarkerWithHue(BitmapDescriptor.hueGreen),
      ));
    }

    final livreur = widget.suivi.position;

    if (livreur != null) {
      marqueurs.add(Marker(
        markerId: const MarkerId('livreur'),
        position: livreur,
        // LE BALLON DE GOOGLE RESTE LE SECOURS tant que le dessin n'est pas pret :
        // une carte sans le livreur serait pire qu'une carte mal habillee, puisque
        // c'est lui qu'on vient voir.
        icon: _vehicule ??
            BitmapDescriptor.defaultMarkerWithHue(BitmapDescriptor.hueAzure),

        // LE VEHICULE SE POSE PAR SON CENTRE, PAS PAR SA POINTE. Un marqueur
        // Google s'ancre par defaut en bas au milieu — ce qui est juste pour
        // une goutte, dont la pointe DESIGNE un endroit, et faux pour un
        // vehicule vu de dessus : il flottait d'une demi-hauteur au-dessus de
        // sa position reelle, soit plusieurs metres a l'echelle de la rue.
        //
        // LA CARTE D'ACCUEIL LE FAISAIT DEJA (home_screen) : c'est le meme
        // dessin, il lui faut le meme ancrage.
        anchor: const Offset(0.5, 0.5),

        // LE LIVREUR PASSE DEVANT LES DEUX EPINGLES DE LA COURSE. Sans cela, il
        // disparait sous le point de collecte des qu'il y arrive — c'est-a-dire au
        // moment ou le client le cherche le plus.
        //
        // « zIndexInt » ET NON « zIndex » : le second est deprecie parce qu'il est
        // tronque en entier sur certaines plateformes, ce qui rend l'ordre instable.
        zIndexInt: 2,
      ));
    }

    return marqueurs;
  }

  /// LA ROUTE REELLE, PAS UN TRAIT ENTRE DEUX POINTS.
  ///
  /// L'application livreur trace encore une ligne droite entre la collecte et le
  /// depot — elle n'a jamais eu mieux. Ici, le trace vient d'OSRM par Delivery :
  /// il suit les rues, donc il montre ou le livreur passe VRAIMENT, et sa
  /// longueur correspond a la distance annoncee.
  ///
  /// VIDE QUAND IL N'Y A RIEN A DESSINER, et c'est frequent : l'ecran n'a pas
  /// demande la geometrie, le moteur n'a pas repondu, ou la mesure vient du
  /// repli a vol d'oiseau — auquel cas dessiner la ligne droite qu'il a calculee
  /// ferait passer une approximation pour un itineraire.
  Set<Polyline> get _itineraire {
    final points = decoderLaPolyligne(widget.trace);
    if (points.length < 2) return const <Polyline>{};

    return {
      Polyline(
        polylineId: const PolylineId('itineraire'),
        points: points,
        color: HbaColors.primary,
        width: 5,

        // DES BOUTS RONDS : a cinq points de large, un trait a bouts carres
        // laisse des marches a chaque virage serre, et une rue de Cotonou en
        // compte beaucoup.
        startCap: Cap.roundCap,
        endCap: Cap.roundCap,
      ),
    };
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final livreur = widget.suivi.position;

    if (livreur == null) {
      return const SizedBox.shrink();
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          height: _hauteur,
          child: GoogleMap(
            initialCameraPosition: CameraPosition(target: livreur, zoom: 15),
            markers: _reperes,
            polylines: _itineraire,
            onMapCreated: (controleur) => _controleur = controleur,
            myLocationEnabled: false,
            myLocationButtonEnabled: false,
            zoomControlsEnabled: false,
            mapToolbarEnabled: false,

            // LA CARTE NE PREND PAS LE GESTE VERTICAL. Elle vit dans une liste qui
            // defile : sans cela, un pouce pose dessus ferait glisser la carte au
            // lieu de l'ecran, et le client ne pourrait plus atteindre le code de
            // remise. Le pincement et le glissement a deux doigts restent.
            gestureRecognizers: const {},
          ),
        ),
        const SizedBox(height: HbaSpacing.xs),
        _Age(suivi: widget.suivi, style: theme.textTheme.bodySmall),
      ],
    );
  }
}

/// « Position il y a 8 s », et ce que ca devient quand le flux se tait.
///
/// CE TEXTE EST LA MOITIE DE L'HONNETETE DE LA CARTE. Une epingle immobile se lit
/// de deux facons — le livreur est arrete, ou l'application ne recoit plus rien —
/// et l'ecran doit dire laquelle. Sans cet age, un client regarderait pendant deux
/// minutes un point mort en croyant voir son livreur.
class _Age extends StatefulWidget {
  const _Age({required this.suivi, this.style});

  final SuiviEnDirect suivi;
  final TextStyle? style;

  @override
  State<_Age> createState() => _AgeState();
}

/// L'ETAT N'EXISTE QUE POUR FAIRE VIEILLIR LE TEXTE.
///
/// SANS MINUTERIE, CE LIBELLE MENTIRAIT EXACTEMENT QUAND IL COMPTE. Il ne se
/// reconstruit que lorsqu'une position arrive — or le cas qu'il doit signaler est
/// celui ou plus rien n'arrive. « Position en direct » resterait affiche sous une
/// epingle morte, indefiniment, ce qui est pire que de ne rien afficher.
///
/// CINQ SECONDES, ET PAS UNE : on affiche un ordre de grandeur, pas un chronometre.
/// Reconstruire ce fragment de texte toutes les cinq secondes ne coute rien ; le
/// faire chaque seconde reveillerait l'ecran soixante fois par minute pour changer
/// un chiffre que personne ne regarde.
class _AgeState extends State<_Age> {
  static const _cadence = Duration(seconds: 5);

  Timer? _horloge;

  @override
  void initState() {
    super.initState();
    _horloge = Timer.periodic(_cadence, (_) {
      if (mounted) setState(() {});
    });
  }

  @override
  void dispose() {
    _horloge?.cancel();
    super.dispose();
  }

  SuiviEnDirect get suivi => widget.suivi;

  TextStyle? get style => widget.style;

  /// Au-dela de ce delai, le point n'est plus « en direct » mais « datant de ».
  ///
  /// QUARANTE-CINQ SECONDES, PARCE QUE LE BATTEMENT EST DE VINGT. Deux battements
  /// manques, c'est un creux de reseau ordinaire ; trois, c'est que quelque chose
  /// ne va pas et le client doit le savoir.
  static const _tiede = Duration(seconds: 45);

  @override
  Widget build(BuildContext context) {
    final vuA = suivi.vuA;

    if (vuA == null) {
      return const SizedBox.shrink();
    }

    final age = vuA.age;
    final perime = age > _tiede;

    return Row(
      children: [
        Icon(
          perime ? Icons.cloud_off_outlined : Icons.circle,
          size: perime ? 14 : 8,
          color: perime ? HbaColors.inkMuted : HbaColors.success,
        ),
        const SizedBox(width: HbaSpacing.xs),
        Expanded(
          child: Text(
            perime
                ? 'Dernière position il y a ${_lisible(age)}.'
                : 'Position en direct.',
            style: style?.copyWith(color: perime ? HbaColors.inkMuted : null),
          ),
        ),
      ],
    );
  }

  static String _lisible(Duration age) {
    if (age.inMinutes < 1) return '${age.inSeconds} s';
    if (age.inMinutes < 60) return '${age.inMinutes} min';

    return '${age.inHours} h';
  }
}
