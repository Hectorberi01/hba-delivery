/// Un document juridique affiche dans l'application.
///
/// LE TEXTE VIT DANS L'APPLICATION, PAS SUR UN SITE. Trois raisons, et aucune
/// n'est le confort : un livreur consulte ses conditions dans un endroit ou le
/// reseau est mauvais ; une page web change sans que personne ne sache quelle
/// version il avait sous les yeux le jour ou il a accepte ; et une application
/// qui renvoie vers un site pour ses conditions n'en a plus aucune le jour ou
/// le site tombe.
///
/// L'ADRESSE EN LIGNE RESTE POSSIBLE EN PLUS, pas a la place : les deux
/// magasins d'applications exigent une URL de politique de confidentialite
/// dans la fiche du produit, et cette URL-la n'est pas un ecran.
class DocumentLegal {
  const DocumentLegal({
    required this.titre,
    required this.chapeau,
    required this.version,
    required this.miseAJour,
    required this.sections,
    this.brouillon = true,
  });

  final String titre;

  /// Une phrase, avant les sections : de quoi parle ce document et a qui il
  /// s'adresse.
  final String chapeau;

  /// Version du texte. ELLE COMPTE PLUS QU'IL N'Y PARAIT : le jour ou un
  /// livreur conteste une regle, la question est de savoir quelle version il
  /// avait acceptee. La tracer commence par la nommer.
  final String version;

  final String miseAJour;

  final List<SectionLegale> sections;

  /// LE TEXTE N'A PAS ETE RELU PAR UN JURISTE.
  ///
  /// Tant que ce drapeau est vrai, l'ecran affiche un bandeau qui le dit. Ce
  /// n'est pas une precaution decorative : une politique de confidentialite
  /// est un ENGAGEMENT de l'entreprise sur ce qu'elle fait des donnees, et des
  /// conditions sont un contrat. Les afficher sans reserve a de vrais livreurs
  /// engagerait HBA sur un texte que personne n'a valide.
  ///
  /// Le passer a faux est une decision de l'entreprise, pas une correction de
  /// code : cela se fait quand le texte a ete relu, et a ce moment-la
  /// seulement.
  final bool brouillon;
}

class SectionLegale {
  const SectionLegale({
    required this.titre,
    this.paragraphes = const [],
    this.points = const [],
    this.aTrancher,
  });

  final String titre;
  final List<String> paragraphes;

  /// Liste a puces, quand l'enumeration se lit mieux que la phrase.
  final List<String> points;

  /// CE QUE LE SYSTEME NE TRANCHE PAS ENCORE, dit dans le document lui-meme.
  ///
  /// Une politique qui passe sous silence ce qui n'est pas decide — une duree
  /// de conservation, par exemple — laisse croire qu'une regle existe. Mieux
  /// vaut ecrire « ce point n'est pas arrete » que de l'inventer : c'est
  /// verifiable, et cela cesse d'etre vrai le jour ou la regle est posee.
  final String? aTrancher;
}
