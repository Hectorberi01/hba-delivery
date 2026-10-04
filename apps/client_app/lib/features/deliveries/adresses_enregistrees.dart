import 'package:flutter/material.dart';
import 'package:hba_ui/hba_ui.dart';

import '../profil/adresses.dart';
import 'point_field.dart';

/// Les quatre noms proposes pour enregistrer un point.
///
/// CE SONT DES LIBELLES, PAS UN TYPE. Les maquettes montrent Domicile, Bureau,
/// Commerce et Autre comme s'il s'agissait d'une nature d'adresse connue du
/// service. Elle ne l'est pas : une adresse favorite ne porte qu'un libelle
/// libre, et rien dans Directory ne distingue un domicile d'un commerce. Ces
/// quatre puces ne font donc qu'une chose — ECRIRE CE MOT-LA dans le libelle au
/// moment d'enregistrer — et c'est exactement ce qui a ete demande le 4 octobre
/// 2026 : « enregistrer ces adresses côté app ».
///
/// CE QUE CELA IMPLIQUE, ET QU'IL FAUT SAVOIR : deux clients peuvent nommer
/// « Bureau » deux choses sans rapport, et personne ne peut filtrer dessus. Le
/// jour ou un filtre ou une icone par nature sera voulu, il faudra un vrai
/// champ cote service — ces puces ne le remplaceront pas.
enum NomDAdresse {
  domicile('Domicile', Icons.home_outlined),
  bureau('Bureau', Icons.work_outline),
  commerce('Commerce', Icons.storefront_outlined),
  autre('Autre', Icons.more_horiz);

  const NomDAdresse(this.libelle, this.icone);

  final String libelle;
  final IconData icone;
}

/// « Enregistrer ce point comme », et la liste de ce qui l'est deja.
///
/// POURQUOI L'ENREGISTREMENT EST ICI ET PAS SEULEMENT DANS LE PROFIL. Le seul
/// moment ou le client CONNAIT le point exact, c'est celui ou il vient de le
/// poser. Lui demander d'aller le recreer dans son profil, de memoire, revient
/// a ne jamais avoir d'adresses enregistrees — ce que montre l'accueil, ou la
/// rangee de raccourcis est vide pour presque tout le monde.
class AdressesEnregistrees extends StatelessWidget {
  const AdressesEnregistrees({
    required this.adresses,
    required this.choisie,
    required this.pointPose,
    required this.onChoisir,
    required this.onEnregistrer,
    required this.enregistrement,
    super.key,
  });

  final List<AdresseFavorite> adresses;

  /// L'adresse enregistree dont le point est celui du formulaire, s'il y en a
  /// une. C'est elle qui porte la pastille pleine.
  final AdresseFavorite? choisie;

  /// Vrai des qu'un point de livraison existe, enregistre ou non.
  final bool pointPose;

  final void Function(AdresseFavorite) onChoisir;

  /// Enregistre le point courant sous ce nom.
  final void Function(NomDAdresse) onEnregistrer;

  /// Le nom en cours d'enregistrement, pour eteindre les puces le temps de
  /// l'aller-retour. Nul au repos.
  final NomDAdresse? enregistrement;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text('Enregistrer ce point comme', style: theme.textTheme.titleMedium),
        const SizedBox(height: HbaSpacing.xs),

        // LA PHRASE CHANGE AVEC L'ETAT, PARCE QUE LA RAISON DU GRIS CHANGE.
        // « Posez d'abord un point » dit ce qu'il manque ; sans elle, quatre
        // puces grises passent pour une panne.
        Text(
          pointPose
              ? 'Vous le retrouverez sur l’accueil, en un appui.'
              : 'Posez d’abord un point de livraison.',
          style: theme.textTheme.bodySmall?.copyWith(
            color: HbaColors.inkMuted,
          ),
        ),
        const SizedBox(height: HbaSpacing.sm),
        Wrap(
          spacing: HbaSpacing.sm,
          runSpacing: HbaSpacing.sm,
          children: [
            for (final nom in NomDAdresse.values)
              _Puce(
                nom: nom,
                occupee: enregistrement == nom,

                // TOUTES LES PUCES S'ETEIGNENT PENDANT UN ENREGISTREMENT, pas
                // seulement celle qu'on a pressee : deux appuis rapides
                // creeraient deux adresses sur le meme point.
                onTap: pointPose && enregistrement == null
                    ? () => onEnregistrer(nom)
                    : null,
              ),
          ],
        ),
        if (adresses.isNotEmpty) ...[
          const SizedBox(height: HbaSpacing.lg),
          Text('Mes adresses', style: theme.textTheme.titleMedium),
          const SizedBox(height: HbaSpacing.sm),
          for (final adresse in adresses) ...[
            _LigneAdresse(
              adresse: adresse,
              // PAR L'IDENTIFIANT, PAS PAR L'INSTANCE. La liste est
              // reconstruite a chaque enregistrement : comparer les objets
              // perdrait la selection au moment precis ou l'on vient
              // d'ajouter une adresse.
              choisie: adresse.id == choisie?.id,
              onTap: () => onChoisir(adresse),
            ),
            const SizedBox(height: HbaSpacing.sm),
          ],
        ],
      ],
    );
  }
}

class _Puce extends StatelessWidget {
  const _Puce({required this.nom, required this.occupee, required this.onTap});

  final NomDAdresse nom;
  final bool occupee;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final actif = onTap != null;

    return HbaPressable(
      radius: HbaRadius.chip,
      color: actif ? HbaColors.surface : HbaColors.surfaceSunken,
      elevation: HbaElevation.douce,
      padding: const EdgeInsets.symmetric(
        horizontal: HbaSpacing.md,
        vertical: HbaSpacing.sm,
      ),
      // PAS D'ALIGNEMENT SUR UNE PUCE. HbaPressable.alignment enveloppe son
      // contenu dans un Align, qui prend toute la largeur disponible : chaque
      // puce occuperait alors sa propre ligne du Wrap.
      semantique: 'Enregistrer ce point comme ${nom.libelle}',
      onTap: onTap,
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (occupee)
            const SizedBox(
              height: 16,
              width: 16,
              child: CircularProgressIndicator(strokeWidth: 2),
            )
          else
            Icon(
              nom.icone,
              size: 18,
              color: actif ? HbaColors.primary : HbaColors.inkFaint,
            ),
          const SizedBox(width: HbaSpacing.xs),
          Text(
            nom.libelle,
            style: theme.textTheme.bodyMedium?.copyWith(
              fontWeight: FontWeight.w600,
              color: actif ? HbaColors.ink : HbaColors.inkFaint,
            ),
          ),
        ],
      ),
    );
  }
}

class _LigneAdresse extends StatelessWidget {
  const _LigneAdresse({
    required this.adresse,
    required this.choisie,
    required this.onTap,
  });

  final AdresseFavorite adresse;
  final bool choisie;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final repere = adresse.repere.trim();

    return HbaPressable(
      radius: HbaRadius.field,
      color: choisie ? HbaColors.primarySoft : HbaColors.surface,
      elevation: HbaElevation.douce,
      padding: const EdgeInsets.all(HbaSpacing.sm),
      alignment: Alignment.centerLeft,
      semantique: 'Livrer à ${adresse.libelle}',
      onTap: onTap,
      child: Row(
        children: [
          Icon(
            adresse.parDefaut ? Icons.star_rounded : Icons.place_outlined,
            size: 20,
            color: HbaColors.primary,
          ),
          const SizedBox(width: HbaSpacing.sm),
          Expanded(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  adresse.libelle,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodyMedium
                      ?.copyWith(fontWeight: FontWeight.w600),
                ),
                Text(
                  // LE REPERE S'IL EXISTE, LE POINT SINON. Une ligne vide sous
                  // le libelle ferait croire a une adresse incomplete, alors
                  // que le point, lui, est toujours la.
                  repere.isNotEmpty
                      ? repere
                      : PickedPoint(
                          latitude: adresse.latitude,
                          longitude: adresse.longitude,
                        ).pretty,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodySmall
                      ?.copyWith(color: HbaColors.inkMuted),
                ),
              ],
            ),
          ),
          Icon(
            choisie
                ? Icons.radio_button_checked
                : Icons.radio_button_unchecked,
            size: 22,
            color: choisie ? HbaColors.primary : HbaColors.border,
          ),
        ],
      ),
    );
  }
}
