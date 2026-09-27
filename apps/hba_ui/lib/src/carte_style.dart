import 'dart:convert';

/// Habillage des cartes Google.
///
/// POURQUOI UNE CARTE PAR DEFAUT NE VA PAS. Celle de Google est faite pour
/// qu'on la regarde SEULE : elle porte des dizaines de pastilles de lieux, des
/// lignes de bus, des aplats satures. Posee sous notre interface, elle gagne le
/// concours de l'attention a chaque fois — nos epingles se perdent parmi les
/// siennes, et nos cartes flottantes se lisent sur un fond qui bouge de
/// couleur a chaque glissement.
///
/// CE QUE CE STYLE ENLEVE, ET CE QU'IL GARDE. Il eteint tout ce qui ne sert pas
/// a se reperer en livrant : les commerces, les transports, les parcelles
/// cadastrales. Il GARDE les noms de rue, les axes et l'eau — sans quoi un
/// livreur ne saurait plus ou il est, ce qui serait un bien plus grand defaut
/// que le bruit qu'on vient d'enlever.
///
/// LES COULEURS SONT CELLES DE L'APPLICATION, D'UN CRAN PLUS SOMBRES. La terre
/// est a #EFE9E3 quand le fond des ecrans est a #F1ECE7 : c'est ce demi-ton
/// d'ecart qui fait flotter les cartes blanches posees dessus. Les routes, a
/// l'inverse, sont plus CLAIRES que la terre — elles se lisent en positif, ce
/// qui est plus lisible au soleil qu'un trait sombre sur fond clair.
///
/// LE STYLE EST CONSTRUIT EN DART, PAS COLLE EN JSON. Une chaine JSON de deux
/// mille caracteres ne se relit pas, ne se commente pas, et la moindre virgule
/// deplacee la fait rejeter en silence — Google n'affiche pas d'erreur, il
/// rend la carte par defaut. Ecrite ainsi, chaque regle porte sa raison, et
/// l'encodage se fait une seule fois.
abstract final class HbaCarte {
  // --- Palette ---

  static const _terre = '#EFE9E3';
  static const _bati = '#E7E0D8';
  static const _parc = '#E1E8DB';
  static const _eau = '#CBD8E1';
  static const _route = '#FBF8F5';
  static const _routeBord = '#E4DCD3';
  static const _axe = '#FFFFFF';
  static const _axeBord = '#DED5CB';
  static const _voieRapide = '#F6E6D2';
  static const _voieRapideBord = '#E3CFAE';
  static const _encre = '#5A6273';
  static const _encrePale = '#7C8494';
  static const _halo = '#FAF7F4';

  static const _off = {'visibility': 'off'};
  static const _simplifie = {'visibility': 'simplified'};

  static Map<String, Object?> _regle(
    String? feature,
    String? element,
    List<Map<String, Object?>> stylers,
  ) =>
      {
        if (feature != null) 'featureType': feature,
        if (element != null) 'elementType': element,
        'stylers': stylers,
      };

  static Map<String, Object?> _couleur(String valeur) => {'color': valeur};

  static final List<Map<String, Object?>> _commun = [
    _regle(null, 'geometry', [_couleur(_terre)]),
    _regle(null, 'labels.text.fill', [_couleur(_encre)]),

    // UN HALO SOUS CHAQUE LIBELLE. Un nom de rue posé sans contour devient
    // illisible des qu'il traverse un parc ou une voie rapide ; le halo le
    // detache de n'importe quel fond.
    _regle(null, 'labels.text.stroke', [_couleur(_halo), {'weight': 3}]),

    // TOUTES LES ICONES DE LIEUX DISPARAISSENT. C'est la principale source de
    // bruit d'une carte Google, et surtout celle qui entre en concurrence
    // directe avec nos propres epingles : le livreur doit pouvoir distinguer
    // son point de collecte d'un restaurant au premier coup d'oeil.
    _regle(null, 'labels.icon', [_off]),

    // Limites administratives : les traits s'effacent, les noms de quartier
    // restent — c'est par eux qu'un client decrit une adresse a Cotonou.
    _regle('administrative', 'geometry', [_off]),
    _regle('administrative.land_parcel', null, [_off]),
    _regle('administrative.neighborhood', 'labels.text.fill', [_couleur(_encrePale)]),

    _regle('landscape.man_made', 'geometry', [_couleur(_bati)]),
    _regle('landscape.natural', 'geometry', [_couleur(_terre)]),

    // Les commerces s'eteignent, les parcs gardent leur aplat : ce sont des
    // reperes de terrain, pas des annonces.
    _regle('poi', null, [_off]),
    _regle('poi.park', 'geometry', [_couleur(_parc)]),
    _regle('poi.park', 'labels.text', [_simplifie]),
    _regle('poi.park', 'labels.text.fill', [_couleur('#6E7B63')]),

    // LES ROUTES SONT PLUS CLAIRES QUE LA TERRE, avec un filet plus sombre.
    // C'est ce qui reste lisible quand l'ecran est lave par le soleil : un
    // reseau clair sur fond sourd tient mieux qu'un trait fin sombre.
    _regle('road', 'geometry.fill', [_couleur(_route)]),
    _regle('road', 'geometry.stroke', [_couleur(_routeBord)]),
    _regle('road', 'labels.icon', [_off]),
    _regle('road.arterial', 'geometry.fill', [_couleur(_axe)]),
    _regle('road.arterial', 'geometry.stroke', [_couleur(_axeBord)]),
    _regle('road.highway', 'geometry.fill', [_couleur(_voieRapide)]),
    _regle('road.highway', 'geometry.stroke', [_couleur(_voieRapideBord)]),
    _regle('road.highway.controlled_access', 'geometry.fill', [_couleur(_voieRapide)]),
    _regle('road.local', 'labels.text.fill', [_couleur(_encrePale)]),

    // Transports en commun eteints : un livreur a moto ne prend pas le bus, et
    // les lignes colorees traversent tout.
    _regle('transit', null, [_off]),

    _regle('water', 'geometry', [_couleur(_eau)]),
    _regle('water', 'labels.text.fill', [_couleur('#6E8496')]),
    _regle('water', 'labels.text.stroke', [_off]),
  ];

  static String? _jourEncode;
  static String? _vignetteEncode;

  /// Le style de la carte plein ecran. Le livreur s'y repere et s'y deplace :
  /// les noms de rue restent.
  static String get jour => _jourEncode ??= jsonEncode(_commun);

  /// Le style des petites cartes — offre, course, feuille.
  ///
  /// ELLES NE SERVENT QU'A SITUER, jamais a naviguer : c'est ecrit dans
  /// CarteCourse, et les gestes y sont coupes pour cette raison. Des noms de
  /// rue sur cent-soixante pixels de haut ne se lisent pas ; ils ne font
  /// qu'encombrer les deux points qu'on veut montrer. Ils s'eteignent donc,
  /// et seuls les axes principaux restent nommes.
  static String get vignette => _vignetteEncode ??= jsonEncode([
        ..._commun,
        _regle('road.local', 'labels', [_off]),
        _regle('road.arterial', 'labels', [_off]),
        _regle('administrative', 'labels', [_off]),
        _regle('poi.park', 'labels', [_off]),
        _regle('landscape.man_made', 'geometry', [_couleur('#EAE3DC')]),
      ]);
}
