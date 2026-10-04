import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:geolocator/geolocator.dart';
import 'package:hba_ui/hba_ui.dart';

import '../commande/feuille_modale.dart';
import '../profil/adresses.dart';
import 'adresses_recentes.dart';
import 'map_picker_screen.dart';
import 'point_field.dart';

/// Ce que la feuille rend : un point, et le repere qui va avec s'il en a un.
class PointChoisi {
  const PointChoisi({required this.point, this.repere = ''});

  final PickedPoint point;

  /// Pre-rempli depuis une adresse enregistree ou recente. Vide pour un point
  /// pose a la main ou pour la position courante.
  final String repere;
}

/// Ou le client choisit un point — et les reponses sont la AVANT qu'il tape.
///
/// CE QUE CET ECRAN REPARE. Jusqu'au 4 octobre 2026, choisir un point ouvrait
/// directement la carte : il fallait trainer une epingle, au doigt, a chaque
/// commande, y compris vers un endroit ou l'on va toutes les semaines. C'est le
/// geste le plus couteux du parcours, et il etait impose meme quand la reponse
/// etait connue d'avance.
///
/// L'ORDRE DE LA LISTE EST LE COEUR DE L'ECRAN. « Ma position » d'abord : a
/// l'enlevement, c'est la bonne reponse presque toujours, et elle coute un
/// appui. Puis les adresses enregistrees, puis les dernieres utilisees — dans
/// une application de livraison, la plupart des adresses sont des REPETITIONS.
/// La carte vient en dernier, pour ce qui n'a ni nom ni passe.
///
/// RIEN DE TOUT CELA NE TOUCHE AU RESEAU. Les favorites sont deja chargees, les
/// recentes vivent sur l'appareil, la position vient du telephone : la feuille
/// s'ouvre pleine, instantanement, meme en 3G et meme hors ligne.
class ChoixDuPoint extends ConsumerStatefulWidget {
  const ChoixDuPoint({
    required this.titre,
    this.actuel,
    super.key,
  });

  final String titre;

  /// Le point deja choisi, s'il y en a un : la carte s'ouvrira dessus.
  final PickedPoint? actuel;

  @override
  ConsumerState<ChoixDuPoint> createState() => _ChoixDuPointState();
}

class _ChoixDuPointState extends ConsumerState<ChoixDuPoint> {
  /// Vrai pendant que le telephone cherche ou l'on est.
  bool _localisation = false;

  String? _erreur;

  void _rendre(PointChoisi choix) {
    if (mounted) Navigator.of(context).pop(choix);
  }

  /// La position du telephone, en un appui.
  ///
  /// ELLE NE PORTE PAS DE REPERE, ET C'EST VOLONTAIRE. « Ma position » dit un
  /// point, pas un lieu : ecrire « ma position » dans le champ que lira le
  /// livreur ne lui apprendrait rien. Le client ecrit ce qu'il veut dessous.
  Future<void> _maPosition() async {
    setState(() {
      _localisation = true;
      _erreur = null;
    });

    try {
      if (!await Geolocator.isLocationServiceEnabled()) {
        _dire('Activez la localisation sur votre téléphone.');
        return;
      }

      var permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied) {
        permission = await Geolocator.requestPermission();
      }

      if (permission == LocationPermission.denied ||
          permission == LocationPermission.deniedForever) {
        // ON NE RENVOIE PAS VERS LES REGLAGES DU SYSTEME. Un refus de position
        // est un choix, pas une panne : la carte reste ouverte juste en dessous,
        // et elle repond a la meme question sans rien demander.
        _dire('Position refusée. Choisissez le point sur la carte.');
        return;
      }

      final position = await Geolocator.getCurrentPosition();
      if (!mounted) return;

      _rendre(
        PointChoisi(
          point: PickedPoint(
            latitude: position.latitude,
            longitude: position.longitude,
          ),
        ),
      );
    } on Object {
      _dire('Position indisponible. Choisissez le point sur la carte.');
    } finally {
      if (mounted) setState(() => _localisation = false);
    }
  }

  void _dire(String message) {
    if (mounted) setState(() => _erreur = message);
  }

  Future<void> _surLaCarte() async {
    final choisi = await Navigator.of(context).push<PickedPoint>(
      MaterialPageRoute(
        builder: (_) => MapPickerScreen(
          title: widget.titre,
          initial: widget.actuel,
        ),
      ),
    );

    if (choisi != null) _rendre(PointChoisi(point: choisi));
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    final favorites =
        ref.watch(adressesProvider).valueOrNull ?? const <AdresseFavorite>[];
    final recentes = ref.watch(adressesRecentesProvider).valueOrNull ??
        const <AdresseRecente>[];

    return FeuilleModale(
      titre: widget.titre,
      icone: Icons.place_outlined,
      enfant: ListView(
        shrinkWrap: true,
        padding: const EdgeInsets.fromLTRB(
          HbaSpacing.gutter,
          HbaSpacing.sm,
          HbaSpacing.gutter,
          HbaSpacing.md,
        ),
        children: [
          _Ligne(
            icone: Icons.my_location,
            titre: 'Ma position',
            detail: 'Là où vous êtes maintenant',
            occupee: _localisation,
            onTap: _localisation ? null : () => unawaited(_maPosition()),
          ),
          if (_erreur != null) ...[
            const SizedBox(height: HbaSpacing.sm),
            Text(
              _erreur!,
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: HbaColors.danger),
            ),
          ],
          const SizedBox(height: HbaSpacing.sm),
          _Ligne(
            icone: Icons.map_outlined,
            titre: 'Choisir sur la carte',
            detail: 'Pour un endroit sans nom',
            onTap: () => unawaited(_surLaCarte()),
          ),
          if (favorites.isNotEmpty) ...[
            const SizedBox(height: HbaSpacing.lg),
            const _Titre('Mes adresses'),
            const SizedBox(height: HbaSpacing.sm),
            for (final adresse in favorites) ...[
              _Ligne(
                icone: adresse.parDefaut
                    ? Icons.star_rounded
                    : Icons.bookmark_outline,
                titre: adresse.libelle,
                detail: adresse.repere.trim().isEmpty
                    ? PickedPoint(
                        latitude: adresse.latitude,
                        longitude: adresse.longitude,
                      ).pretty
                    : adresse.repere,
                onTap: () => _rendre(
                  PointChoisi(
                    point: PickedPoint(
                      latitude: adresse.latitude,
                      longitude: adresse.longitude,
                    ),
                    repere: adresse.repere,
                  ),
                ),
              ),
              const SizedBox(height: HbaSpacing.sm),
            ],
          ],
          if (recentes.isNotEmpty) ...[
            const SizedBox(height: HbaSpacing.lg),
            const _Titre('Dernières utilisées'),
            const SizedBox(height: HbaSpacing.sm),
            for (final recente in recentes) ...[
              _Ligne(
                icone: Icons.history,
                titre: recente.repere.trim().isEmpty
                    ? recente.point.pretty
                    : recente.repere,

                // LA DATE PLUTOT QUE LES COORDONNEES. Deux lignes de chiffres
                // sous un repere ne distinguent rien a l'oeil ; « il y a deux
                // jours » situe la commande dont on se souvient.
                detail: _quand(recente.utiliseeLe),
                onTap: () => _rendre(
                  PointChoisi(
                    point: recente.point,
                    repere: recente.repere,
                  ),
                ),
              ),
              const SizedBox(height: HbaSpacing.sm),
            ],
          ],
        ],
      ),
    );
  }

  /// « aujourd'hui », « hier », « il y a 3 jours », « le 12/09 ».
  static String _quand(DateTime instant) {
    final jours = DateTime.now().toUtc().difference(instant).inDays;

    return switch (jours) {
      <= 0 => 'aujourd’hui',
      1 => 'hier',
      < 7 => 'il y a $jours jours',
      _ => 'le ${instant.toLocal().day.toString().padLeft(2, '0')}/'
          '${instant.toLocal().month.toString().padLeft(2, '0')}',
    };
  }
}

class _Titre extends StatelessWidget {
  const _Titre(this.texte);

  final String texte;

  @override
  Widget build(BuildContext context) => Text(
        texte,
        style: Theme.of(context).textTheme.titleMedium,
      );
}

/// Une reponse possible, en une ligne pressable.
class _Ligne extends StatelessWidget {
  const _Ligne({
    required this.icone,
    required this.titre,
    required this.detail,
    required this.onTap,
    this.occupee = false,
  });

  final IconData icone;
  final String titre;
  final String detail;
  final VoidCallback? onTap;
  final bool occupee;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return HbaPressable(
      radius: HbaRadius.field,
      color: HbaColors.surface,
      elevation: HbaElevation.douce,
      padding: const EdgeInsets.all(HbaSpacing.sm),
      alignment: Alignment.centerLeft,
      semantique: titre,
      onTap: onTap,
      child: Row(
        children: [
          SizedBox(
            height: 40,
            width: 40,
            child: occupee
                ? const Padding(
                    padding: EdgeInsets.all(10),
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : DecoratedBox(
                    decoration: const BoxDecoration(
                      color: HbaColors.primarySoft,
                      shape: BoxShape.circle,
                    ),
                    child: Icon(icone, size: 20, color: HbaColors.primary),
                  ),
          ),
          const SizedBox(width: HbaSpacing.sm),
          Expanded(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  titre,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodyMedium
                      ?.copyWith(fontWeight: FontWeight.w600),
                ),
                Text(
                  detail,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodySmall
                      ?.copyWith(color: HbaColors.inkMuted),
                ),
              ],
            ),
          ),
          const Icon(Icons.chevron_right, color: HbaColors.inkFaint),
        ],
      ),
    );
  }
}
