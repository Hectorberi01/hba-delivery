import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_ui/hba_ui.dart';

import 'point_field.dart';

/// Choix d'un point sur la carte.
///
/// LA CARTE BOUGE, L'EPINGLE RESTE AU CENTRE. C'est le seul geste qui marche
/// d'une main, sur un ecran modeste, sans reseau nerveux : on cadre, on
/// confirme. Poser une epingle par appui long demande de la precision que le
/// pouce n'a pas.
///
/// Aucun geocodage n'est fait : le repere ecrit est saisi a part, et c'est lui
/// qui parle au livreur. La carte ne sert qu'a produire deux nombres.
class MapPickerScreen extends StatefulWidget {
  const MapPickerScreen({required this.title, this.initial, super.key});

  final String title;
  final PickedPoint? initial;

  @override
  State<MapPickerScreen> createState() => _MapPickerScreenState();
}

class _MapPickerScreenState extends State<MapPickerScreen> {
  /// Centre de Cotonou. Sert de position de repli quand la localisation est
  /// refusee ou indisponible : une carte qui s'ouvre au milieu de l'ocean ne
  /// sert a personne.
  static const _cotonou = LatLng(6.3703, 2.3912);

  GoogleMapController? _controller;
  late LatLng _target =
      widget.initial == null ? _cotonou : LatLng(widget.initial!.latitude, widget.initial!.longitude);

  /// Faux tant que la permission n'est pas accordee.
  ///
  /// C'EST CE QUI PILOTE myLocationEnabled. Activer le point bleu sans la
  /// permission fait lever le plugin cote Android — et le cas se produisait des
  /// qu'un point initial etait fourni, puisque la permission n'etait alors
  /// jamais demandee.
  bool _locationGranted = false;

  @override
  void initState() {
    super.initState();
    Future.microtask(_prepareLocation);
  }

  @override
  void dispose() {
    _controller?.dispose();
    super.dispose();
  }

  /// Demande la permission, puis recentre — mais seulement si l'appelant n'a
  /// pas deja fourni un point. Modifier un point existant ne doit pas ramener
  /// la carte ailleurs sous les doigts de l'utilisateur.
  Future<void> _prepareLocation() async {
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

      if (!mounted) return;
      setState(() => _locationGranted = true);

      if (widget.initial != null) return;

      final position = await Geolocator.getCurrentPosition();
      if (!mounted) return;

      final here = LatLng(position.latitude, position.longitude);
      setState(() => _target = here);
      await _controller?.animateCamera(CameraUpdate.newLatLngZoom(here, 16));
    } on Object {
      // Localisation indisponible : la carte reste sur Cotonou, ce qui est un
      // point de depart utilisable.
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      appBar: AppBar(title: Text(widget.title)),
      body: Stack(
        alignment: Alignment.center,
        children: [
          GoogleMap(
            initialCameraPosition: CameraPosition(target: _target, zoom: 16),
            onMapCreated: (controller) => _controller = controller,
            onCameraMove: (position) => _target = position.target,
            myLocationEnabled: _locationGranted,
            myLocationButtonEnabled: _locationGranted,
            zoomControlsEnabled: false,
            mapToolbarEnabled: false,
          ),

          // L'epingle est posee sur la carte, pas dedans : elle ne bouge jamais,
          // c'est la carte qui glisse dessous.
          const Padding(
            // Decale vers le haut de la moitie de la hauteur de l'icone, pour
            // que la POINTE tombe au centre et non le milieu du dessin.
            padding: EdgeInsets.only(bottom: 40),
            child: Icon(Icons.place, size: 44, color: HbaColors.primary),
          ),

          Positioned(
            left: HbaSpacing.gutter,
            right: HbaSpacing.gutter,
            bottom: HbaSpacing.lg,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                HbaCard(
                  padding: const EdgeInsets.all(HbaSpacing.md),
                  child: Text(
                    'Deplacez la carte pour placer le point, puis confirmez.',
                    textAlign: TextAlign.center,
                    style: theme.textTheme.bodyMedium,
                  ),
                ),
                const SizedBox(height: HbaSpacing.sm),
                HbaButton(
                  label: 'Confirmer ce point',
                  onPressed: () => Navigator.of(context).pop(
                    PickedPoint(
                      latitude: _target.latitude,
                      longitude: _target.longitude,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
