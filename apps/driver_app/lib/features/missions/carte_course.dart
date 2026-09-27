import 'dart:async';

import 'package:flutter/material.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_ui/hba_ui.dart';

import '../../core/epingles.dart';

/// Petite carte d'une course, posee dans une feuille ou un ecran.
///
/// CE N'EST PAS LA CARTE DE L'ACCUEIL. Celle-la occupe l'ecran et se
/// manipule ; celle-ci sert a SITUER, en un coup d'oeil, et rien d'autre.
/// D'ou les gestes coupes : sur la feuille d'offre, un doigt pose sur la
/// carte volerait le glissement destine a la feuille, et le livreur perdrait
/// a explorer une carte les secondes qu'il a pour repondre.
///
/// LE TRAIT ENTRE LES DEUX POINTS EST A VOL D'OISEAU, ET IL LE DIT. Aucun
/// service d'itineraire n'est appele : dessiner un trait plein laisserait
/// croire a une route, et un livreur qui la suivrait finirait dans un mur.
/// Il est donc pointille, et l'ecran qui l'affiche le legende.
class CarteCourse extends StatefulWidget {
  const CarteCourse({
    required this.collecte,
    this.depot,
    this.moi,
    this.vehicule,
    this.hauteur = 172,
    super.key,
  });

  /// Point de collecte. C'est le seul point garanti present : avant
  /// acceptation, le contrat ne transporte pas la destination.
  final LatLng collecte;

  /// Point de livraison, connu seulement apres acceptation.
  final LatLng? depot;

  /// Position du livreur, quand l'ecran la connait.
  final LatLng? moi;

  /// Son type de vehicule, pour le pictogramme de l'epingle. Nul : disque nu.
  ///
  /// LA FEUILLE D'OFFRE NE LE PASSE PAS, ET C'EST DELIBERE. Le lire
  /// demanderait d'appeler /me au moment precis ou le livreur a trente
  /// secondes pour repondre — un aller-retour reseau pour un ornement, sur
  /// l'ecran le plus presse de l'application. L'ecran de course, lui, s'ouvre
  /// une fois la course acceptee : la, le profil est deja en memoire.
  final String? vehicule;

  final double hauteur;

  @override
  State<CarteCourse> createState() => _CarteCourseState();
}

class _CarteCourseState extends State<CarteCourse> {
  final _controleur = Completer<GoogleMapController>();

  JeuDEpingles _epinglesDessinees = const JeuDEpingles();
  ({double ratio, String? vehicule})? _dessineAvec;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _rafraichirLesEpingles();
  }

  @override
  void didUpdateWidget(CarteCourse ancien) {
    super.didUpdateWidget(ancien);
    _rafraichirLesEpingles();
  }

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
    if (!mounted || cle != _dessineAvec) return;
    setState(() => _epinglesDessinees = jeu);
  }

  List<LatLng> get _points => [
        widget.collecte,
        if (widget.depot != null) widget.depot!,
        if (widget.moi != null) widget.moi!,
      ];

  Future<void> _cadrer() async {
    final points = _points;
    final carte = await _controleur.future;
    if (!mounted) return;

    if (points.length == 1) {
      await carte.animateCamera(CameraUpdate.newLatLngZoom(points.first, 15));
      return;
    }

    // LE CADRAGE EST DIFFERE D'UNE IMAGE. animateCamera avec des bornes
    // echoue tant que la vue n'a pas de taille connue — elle vient d'etre
    // creee. Un cadre sans marge collerait par ailleurs les epingles au
    // bord, ou elles se recouvrent.
    await Future<void>.delayed(const Duration(milliseconds: 240));
    if (!mounted) return;

    await carte.animateCamera(CameraUpdate.newLatLngBounds(_bornes(points), 48));
  }

  /// Rectangle englobant. LatLngBounds EXIGE southwest <= northeast sur les
  /// deux axes : construit a l'envers, il jette a l'execution.
  static LatLngBounds _bornes(List<LatLng> points) {
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

  Set<Marker> get _epingles => {
        Marker(
          markerId: const MarkerId('collecte'),
          position: widget.collecte,
          icon: _epinglesDessinees.collecte ??
              BitmapDescriptor.defaultMarkerWithHue(BitmapDescriptor.hueOrange),
          infoWindow: const InfoWindow(title: 'Collecte'),
        ),
        if (widget.depot != null)
          Marker(
            markerId: const MarkerId('dépôt'),
            position: widget.depot!,
            icon: _epinglesDessinees.livraison ??
                BitmapDescriptor.defaultMarkerWithHue(BitmapDescriptor.hueGreen),
            infoWindow: const InfoWindow(title: 'Livraison'),
          ),
        if (widget.moi != null)
          Marker(
            markerId: const MarkerId('moi'),
            position: widget.moi!,
            icon: _epinglesDessinees.moiEnLigne ??
                BitmapDescriptor.defaultMarkerWithHue(BitmapDescriptor.hueGreen),
            infoWindow: const InfoWindow(title: 'Vous'),
            anchor: const Offset(0.5, 0.5),
          ),
      };

  Set<Polyline> get _liaisons {
    final depot = widget.depot;
    // LE TYPE EST EXPLICITE : « const {} » se lit comme une Map vide tant
    // qu'aucun type de contexte ne tranche, et le jour ou cette methode
    // changera de signature l'erreur serait obscure.
    if (depot == null) return const <Polyline>{};

    return {
      Polyline(
        polylineId: const PolylineId('a-vol-d-oiseau'),
        points: [widget.collecte, depot],
        // L'OPACITE MONTE DE 0,75 A 0,9 : le fond de carte est desormais
        // beaucoup plus sourd, et un trait a moitie transparent qui tenait sur
        // les aplats satures de Google s'y effacerait.
        color: HbaColors.primary.withValues(alpha: 0.9),
        width: 4,
        // PAS DE « const » ICI : PatternItem.dash et .gap sont des fabriques,
        // pas des constructeurs constants. Le compilateur le refuse, et c'est
        // lui qui a raison.
        patterns: [PatternItem.dash(18), PatternItem.gap(10)],
      ),
    };
  }

  @override
  Widget build(BuildContext context) => ClipRRect(
        borderRadius: BorderRadius.circular(HbaRadius.card),
        child: SizedBox(
          height: widget.hauteur,
          child: GoogleMap(
            // LA VIGNETTE, PAS LE STYLE DE L'ACCUEIL : sur cent-soixante
            // pixels de haut, les noms de rue n'encombrent que les deux points
            // qu'on veut montrer.
            style: HbaCarte.vignette,

            initialCameraPosition: CameraPosition(target: widget.collecte, zoom: 14),
            markers: _epingles,
            polylines: _liaisons,
            onMapCreated: (carte) {
              if (!_controleur.isCompleted) _controleur.complete(carte);
              unawaited(_cadrer());
            },

            // TOUT EST FIGE. Voir le commentaire de classe : cette carte se
            // regarde, elle ne se manipule pas.
            scrollGesturesEnabled: false,
            zoomGesturesEnabled: false,
            rotateGesturesEnabled: false,
            tiltGesturesEnabled: false,
            myLocationEnabled: false,
            myLocationButtonEnabled: false,
            zoomControlsEnabled: false,
            compassEnabled: false,
            mapToolbarEnabled: false,
            liteModeEnabled: false,
          ),
        ),
      );
}
