import 'package:google_maps_flutter/google_maps_flutter.dart';
import 'package:hba_ui/hba_ui.dart';

/// Ce que l'application livreur met sur sa carte.
///
/// LE DESSIN ET LES TABLES SONT DANS LE SOCLE, LA COMPOSITION EST ICI.
/// « Epingles » (hba_ui) sait faire un disque, une goutte, un vehicule en
/// relief, et quel dessin va avec quel type. Ce fichier decide de ce qu'on en
/// fait : deux apparences du LIVREUR LUI-MEME — en ligne, hors ligne — et les
/// deux points de sa course.
///
/// Les quatre epingles se chargent ENSEMBLE, en un appel : la carte les veut
/// toutes au meme instant, et les demander une par une la ferait se peupler par
/// morceaux.
class JeuDEpingles {
  const JeuDEpingles({this.moiEnLigne, this.moiHorsLigne, this.collecte, this.livraison});

  final BitmapDescriptor? moiEnLigne;
  final BitmapDescriptor? moiHorsLigne;
  final BitmapDescriptor? collecte;
  final BitmapDescriptor? livraison;

  /// [vehicule] est le type brut rendu par le serveur — « Motorcycle », ou son
  /// entier.
  ///
  /// DEUX APPARENCES, ET LA PREMIERE QUI SE PRESENTE GAGNE.
  ///
  /// Quand le vehicule a une image, l'epingle est le VEHICULE EN RELIEF, seul :
  /// ni disque ni anneau. L'etat passe alors dans l'image — desaturee et
  /// effacee hors ligne — parce que la couleur n'a plus de support.
  ///
  /// Sinon — camionnette, vehicule non declare, asset introuvable — on retombe
  /// sur LE DISQUE DESSINE, qui porte la couleur de l'etat et le pictogramme
  /// quand il y en a un. Ce n'est pas un pis-aller : c'est ce qui permet
  /// d'ajouter un type de vehicule sans image sans casser la carte.
  static Future<JeuDEpingles> charger(double ratio, {String? vehicule}) async {
    final icone = Epingles.iconeDuVehicule(vehicule);
    final chemin = Epingles.fichierDuVehicule(vehicule);

    final enRelief = chemin == null
        ? null
        : await Epingles.vehiculeEnRelief(chemin: chemin, ratio: ratio, enLigne: true);

    final enReliefEteint = enRelief == null || chemin == null
        ? null
        : await Epingles.vehiculeEnRelief(chemin: chemin, ratio: ratio, enLigne: false);

    return JeuDEpingles(
      moiEnLigne: enRelief ??
          await Epingles.disque(
            couleur: HbaColors.success,
            ratio: ratio,
            vehicule: icone,
          ),

      // HORS LIGNE, LE VEHICULE RESTE MONTRE. Il ne dit pas « je travaille »,
      // il dit « avec quoi » — et cela ne change pas quand on se met en pause.
      // Ce qui change, c'est sa couleur : eteinte sur le relief, grise sur le
      // disque.
      moiHorsLigne: enReliefEteint ??
          await Epingles.disque(
            couleur: HbaColors.inkMuted,
            ratio: ratio,
            vehicule: icone,
          ),

      // LA COLLECTE PORTE L'ORANGE DE LA MARQUE, LA LIVRAISON LE VERT DE
      // L'ARRIVEE. C'est le meme code couleur que les deux lignes de la carte
      // de course, et il ne doit pas diverger : le livreur apprend une fois
      // « orange = ou je vais chercher ». L'application cliente dit maintenant
      // la meme chose, avec les memes formes.
      collecte: await Epingles.goutte(couleur: HbaColors.primary, ratio: ratio),
      livraison: await Epingles.goutte(couleur: HbaColors.success, ratio: ratio),
    );
  }
}
