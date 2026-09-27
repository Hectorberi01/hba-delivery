import 'dart:async';

import 'package:flutter/material.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_ui/hba_ui.dart';

import '../../core/epingles.dart';
import '../missions/models.dart';

/// Cotonou, place de l'Etoile Rouge. Centre de repli tant que le telephone
/// n'a pas rendu de position : une carte centree sur 0,0 montrerait l'ocean.
const _cotonou = LatLng(6.3654, 2.4183);

/// Fond de carte de l'accueil.
///
/// ELLE OCCUPE TOUT L'ECRAN et le reste flotte par-dessus. Un livreur qui
/// roule regarde ou il est, pas une carte de statut ; le statut se rappelle
/// a lui dans un bandeau, il ne prend pas la place.
///
/// LA CLE D'API N'EST PAS DANS CE FICHIER ni nulle part dans le depot. Elle
/// est lue par la plateforme : Gradle depuis android/local.properties,
/// AppDelegate depuis ios/Flutter/Secrets.xcconfig. Sans elle, la vue reste
/// grise et le journal natif dit pourquoi.
class CarteLivreur extends StatefulWidget {
  const CarteLivreur({
    required this.position,
    required this.mission,
    required this.enLigne,
    required this.margeBasse,
    this.vehicule,
    super.key,
  });

  /// Derniere position connue, ou null tant qu'aucune n'a ete lue.
  final LatLng? position;

  final Mission? mission;
  final bool enLigne;

  /// Type de vehicule, tel que le serveur le rend — « Motorcycle », ou son
  /// entier. Il decide du pictogramme dessine dans l'epingle du livreur. Nul
  /// tant que le profil n'est pas lu, ou quand aucun vehicule n'est declare.
  final String? vehicule;

  /// Hauteur du panneau flottant. Google Maps decale ses propres commandes
  /// et son logo de cette valeur : sans elle, la mention legale passe sous
  /// le panneau, ce que les conditions d'utilisation interdisent.
  final double margeBasse;

  @override
  State<CarteLivreur> createState() => _CarteLivreurState();
}

class _CarteLivreurState extends State<CarteLivreur> {
  final _controleur = Completer<GoogleMapController>();

  /// LA CARTE NE SE RECENTRE PAS A CHAQUE BATTEMENT. La position part toutes
  /// les vingt secondes ; suivre chacune arracherait la carte des mains du
  /// livreur qui vient de la faire glisser pour regarder plus loin. Elle ne
  /// bouge d'elle-meme qu'a la premiere position et au changement de course.
  bool _cadre = false;
  String? _missionCadree;

  /// Les epingles dessinees. Vides tant qu'elles ne sont pas rasterisees — la
  /// carte affiche alors les ballons de Google, une fraction de seconde.
  JeuDEpingles _epinglesDessinees = const JeuDEpingles();

  /// Ce qui a servi a les dessiner. DEUX CHOSES, PAS UNE : le ratio de pixels
  /// et le vehicule. Le profil arrive apres le premier rendu, donc l'epingle
  /// est d'abord nue puis se remplit ; ne comparer que le ratio la laisserait
  /// nue pour toujours.
  ({double ratio, String? vehicule})? _dessineAvec;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _rafraichirLesEpingles();
  }

  @override
  void didUpdateWidget(CarteLivreur ancien) {
    super.didUpdateWidget(ancien);
    _rafraichirLesEpingles();

    final idMission = widget.mission?.id;

    if (!_cadre && widget.position != null) {
      _cadre = true;
      unawaited(_recentrer());
    } else if (idMission != _missionCadree) {
      _missionCadree = idMission;
      unawaited(_recentrer());
    }
  }

  /// Redessine les epingles quand ce dont elles dependent a change.
  ///
  /// LE RATIO DE PIXELS NE SE CONNAIT PAS AVANT D'ETRE DANS L'ARBRE, d'ou
  /// didChangeDependencies plutot qu'initState. Il peut changer en cours de
  /// route — un telephone branche sur un ecran externe —, donc on compare au
  /// lieu de charger une seule fois.
  void _rafraichirLesEpingles() {
    final cle = (
      ratio: MediaQuery.devicePixelRatioOf(context),
      vehicule: widget.vehicule,
    );

    if (cle == _dessineAvec) return;

    _dessineAvec = cle;
    unawaited(_chargerLesEpingles(cle));
  }

  Future<void> _chargerLesEpingles(({double ratio, String? vehicule}) cle) async {
    final jeu = await JeuDEpingles.charger(cle.ratio, vehicule: cle.vehicule);

    // LA COMPARAISON APRES L'ATTENTE N'EST PAS DU ZELE : deux chargements
    // peuvent se croiser — le profil arrive pendant que le premier dessin
    // finit — et le plus lent ecraserait le plus recent.
    if (!mounted || cle != _dessineAvec) return;

    setState(() => _epinglesDessinees = jeu);
  }

  Future<void> _recentrer() async {
    final points = _points();
    if (points.isEmpty) return;

    final carte = await _controleur.future;
    if (!mounted) return;

    if (points.length == 1) {
      await carte.animateCamera(
        CameraUpdate.newLatLngZoom(points.first, 15.5),
      );
      return;
    }

    await carte.animateCamera(
      CameraUpdate.newLatLngBounds(_cadrer(points), 64),
    );
  }

  /// Rectangle englobant. LatLngBounds EXIGE southwest <= northeast sur les
  /// deux axes : construit a l'envers, il jette a l'execution.
  static LatLngBounds _cadrer(List<LatLng> points) {
    var sudMin = points.first.latitude;
    var sudMax = points.first.latitude;
    var ouestMin = points.first.longitude;
    var ouestMax = points.first.longitude;

    for (final p in points) {
      if (p.latitude < sudMin) sudMin = p.latitude;
      if (p.latitude > sudMax) sudMax = p.latitude;
      if (p.longitude < ouestMin) ouestMin = p.longitude;
      if (p.longitude > ouestMax) ouestMax = p.longitude;
    }

    return LatLngBounds(
      southwest: LatLng(sudMin, ouestMin),
      northeast: LatLng(sudMax, ouestMax),
    );
  }

  List<LatLng> _points() => [
        if (widget.position != null) widget.position!,
        ..._pointsMission(),
      ];

  List<LatLng> _pointsMission() {
    final mission = widget.mission;
    if (mission == null) return const [];

    return [
      for (final lieu in [mission.pickup, mission.dropoff])
        // 0,0 signale une adresse absente de la reponse, pas un point au
        // large du Benin.
        if (lieu != null && (lieu.latitude != 0 || lieu.longitude != 0))
          LatLng(lieu.latitude, lieu.longitude),
    ];
  }

  Set<Marker> _epingles() {
    final mission = widget.mission;
    final depart = mission?.pickup;
    final arrivee = mission?.dropoff;

    return {
      if (widget.position != null)
        Marker(
          markerId: const MarkerId('moi'),
          position: widget.position!,

          // UN DISQUE VERT QUAND IL TRAVAILLE, GRIS SINON — la meme couleur et
          // le meme sens que le point de la bascule flottante. La carte redit
          // d'un coup d'oeil ce que dit le bouton, et c'est la seule epingle
          // ronde : sa forme la distingue des points de la course meme quand
          // la lumiere a mange les teintes.
          //
          // LE BALLON DE GOOGLE RESTE LE SECOURS tant que le dessin n'est pas
          // pret, ou s'il a echoue.
          icon: (widget.enLigne
                  ? _epinglesDessinees.moiEnLigne
                  : _epinglesDessinees.moiHorsLigne) ??
              BitmapDescriptor.defaultMarkerWithHue(
                widget.enLigne ? BitmapDescriptor.hueGreen : BitmapDescriptor.hueAzure,
              ),
          infoWindow: InfoWindow(
            title: widget.enLigne ? 'Vous, en ligne' : 'Vous, hors ligne',
          ),
          anchor: const Offset(0.5, 0.5),
        ),
      if (depart != null && (depart.latitude != 0 || depart.longitude != 0))
        Marker(
          markerId: const MarkerId('collecte'),
          position: LatLng(depart.latitude, depart.longitude),
          icon: _epinglesDessinees.collecte ??
              BitmapDescriptor.defaultMarkerWithHue(BitmapDescriptor.hueOrange),
          infoWindow: InfoWindow(
            title: 'Collecte',
            snippet: depart.landmark.isEmpty ? null : depart.landmark,
          ),
        ),
      if (arrivee != null && (arrivee.latitude != 0 || arrivee.longitude != 0))
        Marker(
          markerId: const MarkerId('livraison'),
          position: LatLng(arrivee.latitude, arrivee.longitude),
          icon: _epinglesDessinees.livraison ??
              BitmapDescriptor.defaultMarkerWithHue(BitmapDescriptor.hueGreen),
          infoWindow: InfoWindow(
            title: 'Livraison',
            snippet: arrivee.landmark.isEmpty ? null : arrivee.landmark,
          ),
        ),
    };
  }

  @override
  Widget build(BuildContext context) => GoogleMap(
        // L'HABILLAGE PASSE PAR LE PARAMETRE, PLUS PAR setMapStyle. La methode
        // du controleur est depreciee depuis google_maps_flutter 2.6 : elle
        // s'appliquait APRES le premier rendu, donc la carte apparaissait aux
        // couleurs de Google avant de basculer aux notres, a chaque ouverture.
        style: HbaCarte.jour,

        initialCameraPosition: CameraPosition(
          target: widget.position ?? _cotonou,
          zoom: widget.position == null ? 12 : 15.5,
        ),
        markers: _epingles(),
        onMapCreated: (carte) {
          if (!_controleur.isCompleted) _controleur.complete(carte);
        },

        // LE POINT BLEU DE GOOGLE EST DESACTIVE : il doublerait notre propre
        // epingle, et il se met a jour en continu — donc il reveille le GPS
        // en permanence, alors que le battement le fait deja toutes les
        // vingt secondes et que c'est lui qui fait foi cote serveur.
        myLocationEnabled: false,
        myLocationButtonEnabled: false,

        // Rotation et inclinaison coupees : une carte manipulee d'une main
        // pivote par accident, et on ne sait plus la remettre au nord.
        rotateGesturesEnabled: false,
        tiltGesturesEnabled: false,
        compassEnabled: false,
        zoomControlsEnabled: false,
        mapToolbarEnabled: false,

        padding: EdgeInsets.only(bottom: widget.margeBasse),
      );
}
