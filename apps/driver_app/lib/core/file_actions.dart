import 'dart:async';
import 'dart:convert';

import 'package:hba_core/hba_core.dart';

import 'coffre.dart';
import 'plantages.dart';

/// Une action faite par le livreur, qui n'est pas encore partie.
class ActionEnFile {
  const ActionEnFile({
    required this.chemin,
    required this.corps,
    required this.cleIdempotence,
    required this.creeeLe,
    this.proprietaire = '',
  });

  factory ActionEnFile.fromJson(Map<String, dynamic> json) => ActionEnFile(
        chemin: json['chemin'] as String? ?? '',
        corps: (json['corps'] as Map?)?.cast<String, Object?>() ?? const {},
        cleIdempotence: json['cle'] as String? ?? '',
        creeeLe: DateTime.tryParse(json['creeeLe'] as String? ?? '') ?? DateTime.now(),
        proprietaire: json['proprietaire'] as String? ?? '',
      );

  final String chemin;
  final Map<String, Object?> corps;

  /// LA CLE EST FIGEE A LA MISE EN FILE, JAMAIS AU REJEU. C'est tout ce qui
  /// rend le rejeu inoffensif : une cle regeneree a chaque tentative
  /// produirait autant d'effets que de tentatives, et la file deviendrait une
  /// machine a dupliquer plutot qu'une machine a rattraper.
  final String cleIdempotence;

  /// Instant ou le livreur a APPUYE, pas celui de l'envoi.
  final DateTime creeeLe;

  /// IDENTIFIANT DU LIVREUR QUI A FAIT LE GESTE. Vide quand il n'etait pas
  /// encore connu — au tout premier demarrage hors ligne, par exemple.
  ///
  /// IL EXISTE PARCE QU'UNE ACTION N'APPARTIENT PAS AU TELEPHONE, MAIS A UNE
  /// PERSONNE. Sans lui, une remise mise en file par un livreur et rejouee
  /// apres qu'un autre s'est connecte sur le meme appareil partirait avec le
  /// jeton du second : la preuve de livraison porterait le nom de quelqu'un
  /// qui n'y etait pas.
  final String proprietaire;

  Map<String, Object?> toJson() => {
        'chemin': chemin,
        'corps': corps,
        'cle': cleIdempotence,
        'creeeLe': creeeLe.toIso8601String(),
        'proprietaire': proprietaire,
      };
}

/// La file d'actions hors ligne.
///
/// LE README DE CETTE APPLICATION EN FAISAIT « LA CONTRAINTE QUI STRUCTURE
/// TOUT » ALORS QU'ELLE N'EXISTAIT PAS. Les cles d'idempotence etaient bien
/// posees — le SERVEUR etait pret a encaisser un rejeu sans dommage — mais le
/// client ne mettait rien en file : une action qui echouait affichait un
/// bandeau et disparaissait. Un livreur qui confirmait une remise dans un
/// sous-sol devait y repenser lui-meme.
///
/// ELLE NE PREND PAS TOUT, ET C'EST LE POINT LE PLUS IMPORTANT DE CE FICHIER.
/// Une file universelle serait nuisible :
///
///  - rejouer « je passe en ligne » vingt minutes plus tard mettrait en ligne
///    un livreur rentre chez lui ;
///  - rejouer « j'accepte l'offre » apres son expiration n'a aucun sens, et le
///    serveur la refuserait de toute facon ;
///  - rejouer une POSITION perimee serait pire que de ne rien envoyer : elle
///    placerait le livreur la ou il n'est plus.
///
/// Trois actions seulement entrent ici — arrivee, collecte, remise. Ce sont
/// les seules ou le livreur a fait quelque chose DANS LE MONDE PHYSIQUE, qui
/// reste vrai quel que soit le temps ecoule, et que le serveur borne lui-meme
/// par l'horodatage client.
///
/// L'ORDRE EST RESPECTE ET LA FILE S'ARRETE AU PREMIER ECHEC RESEAU. Les trois
/// etapes d'une course forment une machine a etats : envoyer « remise » avant
/// « collecte » se ferait refuser. On ne saute donc jamais une entree pour
/// passer a la suivante.
class FileDActions {
  FileDActions(this._coffre);

  static const _cle = 'hba.driver.file_actions';

  /// Identifiant du livreur actuellement connecte, ecrit par la session.
  ///
  /// IL VIT A COTE DE LA FILE, PAS DANS L'ETAT DE L'APPLICATION, et c'est
  /// necessaire : la file est relue au tout premier instant du demarrage,
  /// avant meme que « /me » ait repondu — et hors ligne, il ne repondra pas du
  /// tout. Un proprietaire garde en memoire vive serait donc vide exactement
  /// quand on en a besoin.
  static const _cleProprietaire = 'hba.driver.proprietaire';

  /// PLAFOND. Une file qui grandit sans borne finit par contenir des actions
  /// d'il y a trois jours que le serveur refusera toutes — l'horodatage client
  /// est borne a vingt-quatre heures. Mieux vaut perdre les plus anciennes,
  /// bruyamment, que de garder une file qu'on ne videra jamais.
  static const _plafond = 50;

  final Coffre _coffre;

  List<ActionEnFile>? _memoire;

  /// LE COFFRE PLUTOT QU'UN SIMPLE FICHIER DE PREFERENCES, et ce n'est pas du
  /// zele : une remise en attente porte le CODE DE REMISE dicte par le
  /// destinataire. Ce code est la preuve qu'un colis a ete remis ; le laisser
  /// en clair sur le telephone reviendrait a le rendre recopiable.
  Future<List<ActionEnFile>> _lire() async {
    final connu = _memoire;
    if (connu != null) return connu;

    try {
      final brut = await _coffre.read(key: _cle);
      if (brut == null || brut.isEmpty) return _memoire = [];

      final liste = jsonDecode(brut);
      return _memoire = [
        if (liste is List)
          for (final e in liste)
            if (e is Map<String, dynamic>) ActionEnFile.fromJson(e),
      ];
    } on Object catch (erreur, pile) {
      // Une file illisible ne doit pas empecher l'application de demarrer. On
      // repart a vide, et on le signale plutot que de le taire.
      Plantages.noter(erreur, pile, contexte: 'lecture de la file d\'actions');
      return _memoire = [];
    }
  }

  Future<void> _ecrire(List<ActionEnFile> entrees) async {
    _memoire = entrees;
    await _coffre.write(
      key: _cle,
      value: jsonEncode([for (final e in entrees) e.toJson()]),
    );
  }

  /// Combien d'actions attendent. Sert a le DIRE au livreur.
  Future<int> get nombre async => (await _lire()).length;

  /// Le livreur a qui appartient ce qui est en file. Vide s'il est inconnu.
  Future<String> proprietaireCourant() async {
    try {
      return await _coffre.read(key: _cleProprietaire) ?? '';
    } on Object {
      return '';
    }
  }

  /// Retient a qui appartiennent les actions a venir. Appele par la session
  /// des que « /me » a repondu.
  Future<void> noterLeProprietaire(String id) async {
    if (id.isEmpty) return;
    if (await proprietaireCourant() == id) return;

    try {
      await _coffre.write(key: _cleProprietaire, value: id);
    } on Object catch (erreur, pile) {
      Plantages.noter(erreur, pile, contexte: 'ecriture du proprietaire de la file');
    }
  }

  /// EFFACE TOUT, ET C'EST UNE OPERATION DE DECONNEXION.
  ///
  /// DEUX RAISONS, ET LA SECONDE EST LA PLUS GRAVE. La premiere : une remise
  /// en attente porte le CODE DE REMISE dicte par le destinataire, c'est-a-
  /// dire la preuve qu'un colis a ete remis ; le laisser sur le telephone
  /// apres le depart de son proprietaire n'a aucune justification. La seconde :
  /// ce qui reste en file serait rejoue par le PROCHAIN livreur a se
  /// connecter sur cet appareil, avec SON jeton.
  ///
  /// LES ACTIONS PERDUES SONT SIGNALEES, PAS OUBLIEES EN SILENCE. Se
  /// deconnecter en laissant une remise non partie est un vrai incident : la
  /// course restera ouverte cote serveur, et quelqu'un devra s'en apercevoir.
  Future<void> purger() async {
    final restantes = await _lire();

    for (final perdue in restantes) {
      Plantages.noter(
        StateError('déconnexion avec une action en attente'),
        StackTrace.current,
        contexte: 'action perdue a la deconnexion : ${perdue.chemin}',
      );
    }

    _memoire = [];

    try {
      await _coffre.delete(key: _cle);
      await _coffre.delete(key: _cleProprietaire);
    } on Object catch (erreur, pile) {
      Plantages.noter(erreur, pile, contexte: 'purge de la file d\'actions');
    }
  }

  /// Met une action en file.
  ///
  /// LA MEME ACTION NE S'EMPILE PAS DEUX FOIS. Un livreur sans reseau appuie
  /// souvent plusieurs fois sur le meme bouton — c'est le reflexe de tout le
  /// monde. La cle d'idempotence identifie l'action, pas la tentative : la
  /// nouvelle entree remplace l'ancienne au lieu de s'ajouter.
  Future<void> pousser(ActionEnFile action) async {
    // L'ESTAMPILLE SE POSE ICI, AU MOMENT DU GESTE, et jamais au rejeu :
    // rejouee, l'action pourrait etre attribuee a celui qui est connecte a ce
    // moment-la, ce qui est precisement l'erreur qu'on veut rendre
    // impossible.
    final signee = ActionEnFile(
      chemin: action.chemin,
      corps: action.corps,
      cleIdempotence: action.cleIdempotence,
      creeeLe: action.creeeLe,
      proprietaire: action.proprietaire.isNotEmpty
          ? action.proprietaire
          : await proprietaireCourant(),
    );

    final entrees = [...await _lire()]
      ..removeWhere((e) => e.cleIdempotence == signee.cleIdempotence)
      ..add(signee);

    while (entrees.length > _plafond) {
      final perdue = entrees.removeAt(0);
      Plantages.noter(
        StateError('file pleine'),
        StackTrace.current,
        contexte: 'action abandonnee : ${perdue.chemin}',
      );
    }

    await _ecrire(entrees);
    Plantages.trace('action mise en file : ${signee.chemin}');
  }

  /// Separe ce qui m'appartient de ce qui appartient a un autre.
  ///
  /// FONCTION PURE, ET C'EST VOLONTAIRE : c'est la regle la plus dangereuse de
  /// ce fichier — elle decide ce qui part sous le nom du livreur connecte — et
  /// c'est donc celle qu'il faut pouvoir eprouver sans coffre, sans reseau et
  /// sans telephone.
  ///
  /// UNE ESTAMPILLE VIDE M'APPARTIENT. Elle vient d'une action mise en file
  /// avant que « /me » ait jamais repondu, c'est-a-dire d'un premier demarrage
  /// hors ligne. La deconnexion purge la file : ce qui porte une estampille
  /// vide ne peut donc venir que de la session en cours.
  ///
  /// UN PROPRIETAIRE INCONNU NE JETTE RIEN. Si je ne sais pas qui je suis, je
  /// ne suis pas en position de declarer qu'une action appartient a un autre.
  static ({List<ActionEnFile> miennes, List<ActionEnFile> autres}) trier(
    List<ActionEnFile> entrees,
    String moi,
  ) {
    if (moi.isEmpty) return (miennes: [...entrees], autres: const []);

    final miennes = <ActionEnFile>[];
    final autres = <ActionEnFile>[];

    for (final e in entrees) {
      if (e.proprietaire.isEmpty || e.proprietaire == moi) {
        miennes.add(e);
      } else {
        autres.add(e);
      }
    }

    return (miennes: miennes, autres: autres);
  }

  /// Rejoue ce qui attend, dans l'ordre.
  ///
  /// Rend le nombre d'actions parties. ELLE NE LEVE JAMAIS : elle est appelee
  /// depuis des endroits — retour au premier plan, fin d'un autre appel — ou
  /// une exception n'aurait personne pour l'attraper.
  Future<int> vider(ApiClient api) async {
    final entrees = [...await _lire()];
    if (entrees.isEmpty) return 0;

    // CEINTURE ET BRETELLES. La deconnexion purge deja la file ; ce filtre
    // rattrape ce qu'elle n'a pas pu faire — application tuee au milieu de la
    // deconnexion, coffre qui refuse l'ecriture, session perdue sur un 401
    // qui n'a pas pu etre rafraichi. Rejouer l'action d'un autre avec le
    // jeton de celui qui est la falsifierait une preuve de livraison ; la
    // perdre ne fait qu'un incident visible.
    final moi = await proprietaireCourant();
    final tri = trier(entrees, moi);

    for (final ecartee in tri.autres) {
      Plantages.noter(
        StateError('action d\'un autre livreur en file'),
        StackTrace.current,
        contexte: 'action ecartee : ${ecartee.chemin}',
      );
    }

    entrees
      ..clear()
      ..addAll(tri.miennes);

    if (entrees.isEmpty) {
      await _ecrire(entrees);
      return 0;
    }

    var parties = 0;

    while (entrees.isNotEmpty) {
      final entree = entrees.first;

      try {
        await api.post(
          entree.chemin,
          body: entree.corps,
          idempotencyKey: entree.cleIdempotence,
        );

        entrees.removeAt(0);
        parties++;
      } on OfflineException {
        // TOUJOURS PAS DE RESEAU : on s'arrete la, on ne perd rien, et on
        // reessaiera. Ne pas continuer est volontaire — les suivantes
        // echoueraient pareil, et chaque tentative coute une attente.
        break;
      } on ApiException catch (erreur, pile) {
        // LE SERVEUR A REPONDU, ET IL A REFUSE. Rejouer indefiniment
        // bloquerait toute la file derriere cette entree : on l'abandonne.
        //
        // C'EST LE CAS LE PLUS DELICAT DE TOUT CE FICHIER. Un refus est
        // souvent benin — la course avait deja avance, l'action etait donc
        // deja faite. Mais il peut aussi etre grave : un code de remise
        // refuse signifie que la livraison n'est PAS enregistree. On ne peut
        // pas le distinguer ici, alors on abandonne l'entree ET on la
        // signale, pour que le cas grave finisse par se voir.
        Plantages.noter(
          erreur,
          pile,
          contexte: 'action rejouee refusee : ${entree.chemin} (${erreur.code})',
        );

        entrees.removeAt(0);
      } on Object catch (erreur, pile) {
        Plantages.noter(erreur, pile, contexte: 'rejeu de ${entree.chemin}');
        break;
      }
    }

    await _ecrire(entrees);

    if (parties > 0) Plantages.trace('$parties action(s) rejouee(s)');
    return parties;
  }
}
