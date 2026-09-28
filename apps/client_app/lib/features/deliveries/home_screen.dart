import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:geolocator/geolocator.dart';
import 'package:go_router/go_router.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_ui/hba_ui.dart';

import 'courses_screen.dart';
import 'delivery_providers.dart';
import 'livreurs_proches.dart';

/// Accueil : la carte, les livreurs alentour, et de quoi commander.
///
/// LA CARTE OCCUPE TOUT L'ECRAN, ET C'EST POUR CA QUE LA NAVIGATION FLOTTE.
/// Une barre en bas aurait coupe un bandeau a une carte qui n'en a pas de
/// trop ; l'arc se pose par-dessus.
///
/// CE QUE LES PASTILLES MONTRENT, ET CE QU'ELLES NE MONTRENT PAS. Les livreurs
/// sont des points, sans nom, sans plaque, sans identifiant. Le point 24 des
/// points a trancher ouvre au client les POSITIONS ; il ne leve pas la ligne de
/// la matrice de visibilite qui reserve l'identite du livreur a partir de
/// DRIVER_ASSIGNED.
class HomeScreen extends ConsumerStatefulWidget {
  const HomeScreen({super.key});

  @override
  ConsumerState<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends ConsumerState<HomeScreen> {
  /// LE CENTRE PAR DEFAUT EST COTONOU, PAS UN POINT NUL. Une carte qui s'ouvre
  /// au large du golfe de Guinee — latitude zero, longitude zero — pendant que
  /// la localisation se decide donne l'impression d'une application cassee.
  static const _cotonou = LatLng(6.3703, 2.3912);

  final _controleur = Completer<GoogleMapController>();

  LatLng _centre = _cotonou;
  LatLng? _moi;

  /// Le point que la camera vise pendant qu'on la deplace. Il ne devient le
  /// centre de la requete qu'au repos — voir [_recentrer].
  LatLng? _vise;

  @override
  void initState() {
    super.initState();
    unawaited(_seSituer());
  }

  Future<void> _seSituer() async {
    try {
      if (!await Geolocator.isLocationServiceEnabled()) return;

      var permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied) {
        permission = await Geolocator.requestPermission();
      }
      if (permission == LocationPermission.denied ||
          permission == LocationPermission.deniedForever) {
        return;
      }

      final position = await Geolocator.getCurrentPosition();
      if (!mounted) return;

      final ici = LatLng(position.latitude, position.longitude);
      setState(() {
        _moi = ici;
        _centre = ici;
      });

      final carte = await _controleur.future;
      await carte.animateCamera(CameraUpdate.newLatLngZoom(ici, 14));
    } on Object {
      // POSITION REFUSEE OU INDISPONIBLE : LA CARTE RESTE SUR COTONOU. Ce
      // n'est pas une panne — un client peut commander sans partager sa
      // position, il pose ses points a la main sur la carte.
    }
  }

  @override
  Widget build(BuildContext context) {
    final livreurs = ref.watch(livreursProchesProvider(_centre));
    final courses = ref.watch(deliveriesProvider).valueOrNull ?? const [];
    final enCours = courses.where((c) => !c.status.isClosed).toList();

    return Scaffold(
      body: Stack(
        children: [
          Positioned.fill(
            child: GoogleMap(
              style: HbaCarte.jour,
              initialCameraPosition:
                  CameraPosition(target: _centre, zoom: _moi == null ? 12 : 14),
              onMapCreated: (carte) {
                if (!_controleur.isCompleted) _controleur.complete(carte);
              },
              myLocationEnabled: _moi != null,
              myLocationButtonEnabled: false,
              zoomControlsEnabled: false,
              mapToolbarEnabled: false,

              // LA CARTE NE RECENTRE PAS LA REQUETE A CHAQUE PIXEL. On attend
              // que le doigt se leve : « onCameraIdle » ne part qu'une fois le
              // geste fini, la ou « onCameraMove » enverrait des dizaines de
              // requetes pour un seul deplacement.
              onCameraIdle: _recentrer,
              onCameraMove: (position) => _vise = position.target,

              markers: {
                for (final (i, point) in livreurs.valueOrNull
                        ?.indexed ??
                    const <(int, LatLng)>[])
                  Marker(
                    // L'INDEX, PAS UN IDENTIFIANT DE LIVREUR — le service n'en
                    // rend pas, et c'est voulu. Voir livreurs_proches.dart.
                    markerId: MarkerId('livreur-$i'),
                    position: point,
                    anchor: const Offset(0.5, 0.5),
                    icon: BitmapDescriptor.defaultMarkerWithHue(
                      BitmapDescriptor.hueOrange,
                    ),
                  ),
              },
            ),
          ),

          // LE BANDEAU DU HAUT RESTE A GAUCHE, loin de l'arc : les deux se
          // recouvriraient sur un petit ecran.
          Positioned(
            top: 0,
            left: 0,
            right: 96,
            child: SafeArea(
              child: Padding(
                padding: const EdgeInsets.all(HbaSpacing.gutter),
                child: _Compteur(livreurs: livreurs),
              ),
            ),
          ),

          Positioned(
            left: 0,
            right: 0,
            bottom: 0,
            child: SafeArea(
              child: Padding(
                padding: const EdgeInsets.all(HbaSpacing.gutter),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    // LA COURSE EN COURS PASSE AVANT LE BOUTON DE COMMANDE. Un
                    // client qui a une livraison en route ouvre l'application
                    // pour la SUIVRE, pas pour en commander une autre.
                    if (enCours.isNotEmpty) ...[
                      CarteCourse(course: enCours.first, enAvant: true),
                      const SizedBox(height: HbaSpacing.sm),
                    ],
                    HbaButton(
                      label: 'Envoyer un colis',
                      icon: Icons.add,
                      onPressed: () => context.push('/nouvelle'),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }

  void _recentrer() {
    final cible = _vise;
    if (cible == null || !mounted) return;

    // ON NE REDEMANDE PAS POUR TROIS METRES. Sous cinq cents metres, la reponse
    // serait la meme a quelques pastilles pres, et chaque requete coute au
    // client des donnees qu'il paie.
    if (_distanceApprox(cible, _centre) < 500) return;

    setState(() => _centre = cible);
  }

  /// Distance approchee en metres, suffisante pour decider d'un rafraichissement.
  static double _distanceApprox(LatLng a, LatLng b) {
    const metresParDegre = 111320.0;
    final dLat = (a.latitude - b.latitude).abs() * metresParDegre;
    final dLng = (a.longitude - b.longitude).abs() * metresParDegre * 0.62;
    return dLat + dLng;
  }
}

/// Combien de livreurs sont autour du client.
///
/// LE NOMBRE EXACT A ETE DEMANDE, ET IL EST AFFICHE. La version precedente
/// disait « des livreurs sont disponibles » sans chiffre, pour une raison qui
/// reste vraie : un nombre releve d'heure en heure renseigne sur l'activite
/// reelle d'un quartier, ce qui n'est ni au client ni a un concurrent. Le
/// choix inverse a ete fait sciemment — un chiffre convainc la ou une formule
/// vague laisse douter.
///
/// LE PLAFOND DE LA ROUTE BORNE CE QUI EST DIT. Le service ne rend jamais plus
/// de quinze positions ; « 15 » ne veut donc pas dire quinze, mais « au moins
/// quinze ». L'ecran l'ecrit ainsi plutot que d'affirmer un nombre faux.
class _Compteur extends StatelessWidget {
  const _Compteur({required this.livreurs});

  /// Doit valoir le plafond de /api/client/v1/drivers/nearby. Les deux se
  /// desynchroniseraient sans bruit ; c'est pour ca que la phrase change de
  /// forme au lieu de mentir sur un nombre.
  static const _plafondRoute = 15;

  final AsyncValue<List<LatLng>> livreurs;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    final texte = switch (livreurs) {
      AsyncData(:final value) when value.isEmpty =>
        'Aucun livreur dans ce secteur pour le moment.',
      AsyncData(:final value) when value.length >= _plafondRoute =>
        'Plus de $_plafondRoute livreurs autour de vous.',
      AsyncData(:final value) when value.length == 1 =>
        '1 livreur autour de vous.',
      AsyncData(:final value) => '${value.length} livreurs autour de vous.',
      AsyncError() => 'Livreurs alentour indisponibles.',
      _ => 'Recherche des livreurs alentour…',
    };

    return HbaCard(
      padding: const EdgeInsets.symmetric(
        horizontal: HbaSpacing.md,
        vertical: HbaSpacing.sm,
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(
            switch (livreurs) {
              AsyncData(:final value) when value.isEmpty =>
                Icons.location_off_outlined,
              AsyncData() => Icons.two_wheeler_outlined,
              AsyncError() => Icons.cloud_off_outlined,
              _ => Icons.more_horiz,
            },
            size: 18,
            color: HbaColors.primary,
          ),
          const SizedBox(width: HbaSpacing.sm),
          Flexible(child: Text(texte, style: theme.textTheme.bodyMedium)),
        ],
      ),
    );
  }
}
