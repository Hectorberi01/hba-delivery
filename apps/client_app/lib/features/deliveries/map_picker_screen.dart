import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:geolocator/geolocator.dart';
import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_ui/hba_ui.dart';

import 'lien_maps.dart';
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
///
/// DEUX FACONS D'ARRIVER AU MEME POINT, ET LA SECONDE N'EST PAS UN RACCOURCI.
/// Cadrer la carte suppose de savoir ou l'on va. Celui qui commande pour
/// quelqu'un d'autre, lui, a recu un lien Google Maps par message — a Cotonou
/// c'est ce qui circule, bien plus qu'une rue et un numero. Le champ du haut
/// lui evite de lire le lien ailleurs, retenir le quartier, et retrouver
/// l'endroit au doigt : c'est la que les points se posent a cent metres de la
/// bonne cour.
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

  final _lien = TextEditingController();

  /// Vrai pendant qu'on suit la redirection d'un lien court.
  bool _resolution = false;

  @override
  void initState() {
    super.initState();
    Future.microtask(_prepareLocation);
  }

  @override
  void dispose() {
    _lien.dispose();
    _controller?.dispose();
    super.dispose();
  }

  /// Prend ce qu'il y a dans le presse-papier et tente de le lire.
  ///
  /// UN BOUTON PLUTOT QUE « APPUI LONG, COLLER ». Le geste natif existe, mais il
  /// demande de savoir que ce champ accepte un lien ; le bouton le dit.
  Future<void> _coller() async {
    final donnees = await Clipboard.getData(Clipboard.kTextPlain);
    final texte = donnees?.text?.trim() ?? '';

    if (texte.isEmpty) {
      _dire('Le presse-papier est vide.');
      return;
    }

    _lien.text = texte;
    await _appliquer(texte);
  }

  /// Deplace la carte sur le point contenu dans [texte], s'il y en a un.
  Future<void> _appliquer(String texte) async {
    if (texte.trim().isEmpty || _resolution) return;

    setState(() => _resolution = true);
    final point = await LienMaps.resoudre(texte);
    if (!mounted) return;
    setState(() => _resolution = false);

    if (point == null) {
      // LE MESSAGE NE DIT PAS « LIEN INVALIDE », et la nuance vaut la peine :
      // un lien court non resolu et un texte sans coordonnees echouent pareil
      // ici, mais pour des raisons opposees — l'un est bon et le reseau a
      // manque, l'autre ne l'a jamais ete. On ne peut pas les distinguer, donc
      // on ne tranche pas, et on rappelle la porte qui marche toujours.
      _dire("Aucun point trouvé dans ce lien. Placez-le sur la carte.");
      return;
    }

    final cible = LatLng(point.latitude, point.longitude);
    setState(() => _target = cible);

    // ZOOM 17 : UN CRAN PLUS PRES QUE L'OUVERTURE. Le lien designe une cour ou
    // un portail, pas un quartier ; rester a 16 laisserait croire que le point
    // est approximatif alors qu'il vient d'etre pose avec precision.
    await _controller?.animateCamera(CameraUpdate.newLatLngZoom(cible, 17));
  }

  void _dire(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message)));
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

          // LE CHAMP DE LIEN EST EN HAUT, ET LE BOUTON DE CONFIRMATION EN BAS.
          // Les deux gestes sont a des moments differents du meme ecran : on
          // arrive, on colle ; on ajuste ; on confirme. Les mettre cote a cote
          // ferait choisir entre eux.
          Positioned(
            top: HbaSpacing.sm,
            left: HbaSpacing.gutter,
            right: HbaSpacing.gutter,
            child: HbaCard(
              padding: const EdgeInsets.symmetric(
                horizontal: HbaSpacing.sm,
                vertical: 2,
              ),
              child: Row(
                children: [
                  const Icon(Icons.link, color: HbaColors.inkFaint, size: 20),
                  const SizedBox(width: HbaSpacing.sm),
                  Expanded(
                    child: TextField(
                      controller: _lien,
                      enabled: !_resolution,
                      textInputAction: TextInputAction.go,
                      onSubmitted: (v) => unawaited(_appliquer(v)),
                      style: theme.textTheme.bodyMedium,
                      decoration: const InputDecoration(
                        hintText: 'Coller un lien Google Maps',
                        border: InputBorder.none,
                        isDense: true,
                      ),
                    ),
                  ),

                  // L'INDICATEUR REMPLACE LE BOUTON, IL NE S'AJOUTE PAS A LUI :
                  // suivre une redirection prend une seconde ou deux, et deux
                  // appuis pendant ce temps lanceraient deux resolutions.
                  if (_resolution)
                    const Padding(
                      padding: EdgeInsets.all(10),
                      child: SizedBox(
                        height: 20,
                        width: 20,
                        child: CircularProgressIndicator(strokeWidth: 2.2),
                      ),
                    )
                  else
                    IconButton(
                      onPressed: () => unawaited(_coller()),
                      icon: const Icon(Icons.content_paste_rounded, size: 20),
                      color: HbaColors.primary,
                      tooltip: 'Coller',
                    ),
                ],
              ),
            ),
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
                    'Déplacez la carte pour placer le point, puis confirmez.',
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
