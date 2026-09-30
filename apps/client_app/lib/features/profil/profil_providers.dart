import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';

import '../../core/providers.dart';

/// Le profil du client, tel que le service l'a.
///
/// CE QUE L'ECRAN MONTRE OU MODIFIE, ET RIEN D'AUTRE. Le contrat declare
/// davantage — identifiant, adresses favorites, date de creation — mais le
/// formulaire ne touche que ce que « PUT /me » accepte : le nom et le courriel.
/// Le telephone est l'identifiant du compte ; il se change en changeant de
/// compte, pas dans un formulaire.
///
/// L'IDENTIFIANT DE LA PHOTO EST LA, SON ADRESSE NON. Le service rend un
/// identifiant de media, pas une URL : l'adresse est signee et expire en
/// quelques minutes, donc la garder dans le profil reviendrait a garder une
/// valeur perimee. L'ecran demande le lien quand il a une photo a afficher, par
/// « GET /me/photo ».
class Profil {
  const Profil({
    required this.nom,
    required this.telephone,
    required this.courriel,
    required this.photoMediaId,
  });

  final String nom;
  final String telephone;
  final String courriel;

  /// Vide quand le client n'a pas de photo.
  final String photoMediaId;

  bool get aUnePhoto => photoMediaId.trim().isNotEmpty;

  bool get complet => nom.trim().isNotEmpty && courriel.trim().isNotEmpty;

  static Profil depuis(Map<String, dynamic> json) => Profil(
        nom: json['displayName'] is String ? json['displayName'] as String : '',
        telephone: json['phone'] is String ? json['phone'] as String : '',
        courriel: json['email'] is String ? json['email'] as String : '',
        photoMediaId:
            json['photoMediaId'] is String ? json['photoMediaId'] as String : '',
      );
}

class ProfilRepository {
  ProfilRepository(this._api);

  final ApiClient _api;

  Future<Profil> lire() async => Profil.depuis(await _api.get('/me'));

  /// Cree la fiche manquante, a partir du jeton seul.
  ///
  /// POURQUOI L'APPLICATION PEUT LE DEMANDER, ALORS QUE LE CHEMIN NORMAL EST UN
  /// EVENEMENT. Une fiche naît de « AccountRegistered », publie par Identity a
  /// l'inscription. Cet evenement peut se perdre : si Directory etait arrete
  /// quand il est passe, Kafka ne le rejoue pas pour un groupe de
  /// consommateurs qui n'existait pas encore. Le client a alors un compte
  /// valide, des livraisons qui marchent, et aucun profil.
  ///
  /// AUCUN CORPS N'EST ENVOYE. Le nom, le telephone et le courriel sont lus par
  /// le service dans le jeton qu'il a lui-meme valide : l'application n'ecrit
  /// pas l'identite, elle demande seulement la reparation.
  ///
  /// LA CLE D'IDEMPOTENCE EST UNE FORMALITE ICI — la route l'est deja cote
  /// service, qui rend la fiche existante au lieu d'en creer une seconde.
  Future<Profil> creer() async => Profil.depuis(
        await _api.post('/me', idempotencyKey: 'fiche-client'),
      );

  /// LES DEUX CHAMPS PARTENT ENSEMBLE, MEME INCHANGES. « UpdateCustomer »
  /// remplace ce qu'on lui donne : n'envoyer que le nom effacerait le
  /// courriel, et l'ecran presenterait alors comme une reussite une perte de
  /// donnee.
  Future<Profil> enregistrer({
    required String nom,
    required String courriel,
  }) async {
    final data = await _api.put(
      '/me',
      body: {'displayName': nom, 'email': courriel},
    );
    return Profil.depuis(data);
  }

  /// Envoie une photo de profil.
  ///
  /// NE REND RIEN : l'appelant invalide le profil, qui porte l'identifiant du
  /// media, et c'est cette relecture qui fait apparaître la nouvelle photo.
  ///
  /// UNE SEULE REQUETE, ALORS QUE LE SERVEUR EN FAIT DEUX. La passerelle
  /// depose les octets dans Media, recupere l'identifiant, puis l'attache au
  /// profil. Enchainer ces deux appels ICI aurait laisse un etat a mi-chemin a
  /// chaque coupure : un fichier depose que le profil ne reclamerait jamais.
  /// Sur une 3G a Cotonou, ce n'est pas un cas rare.
  ///
  /// ON RECOIT DES OCTETS, PLUS UN FICHIER, DEPUIS LE CADRAGE. L'ecran de
  /// cadrage redessine la portion choisie en carre et la compresse lui-meme :
  /// il n'y a plus de fichier a reduire, et en ecrire un sur le disque pour le
  /// relire aussitot ne servirait qu'a laisser un dechet de plus.
  Future<void> deposerPhoto(Uint8List octets) async {
    await _api.upload(
      '/me/photo',
      // LES OCTETS SE RELISENT, LA FORMDATA NON. Elle est donc reconstruite a
      // chaque tentative : le rejeu qui suit un rafraichissement de jeton
      // renvoyait la meme, deja finalisee, et echouait en « erreur inattendue ».
      formulaire: () async => FormData.fromMap({
        'fichier': MultipartFile.fromBytes(octets, filename: 'profil.jpg'),
      }),
      // UNE CLE NEUVE A CHAQUE ENVOI. Remplacer sa photo, c'est refaire
      // exprement la meme requete : avec une cle fixe, le jour ou cette route
      // honorera l'en-tete, le second envoi passerait pour un doublon et la
      // nouvelle photo ne remplacerait jamais l'ancienne — sans qu'aucune
      // erreur ne s'affiche.
      idempotencyKey: ApiClient.newIdempotencyKey(),
    );
  }

  /// Retire la photo, et fait effacer le fichier.
  ///
  /// NE REND RIEN, ALORS QUE LA ROUTE REND LA FICHE. L'appelant invalide le
  /// profil juste apres : lire la reponse ICI puis la relire LA ferait deux
  /// requetes pour une seule verite, et la premiere serait celle qu'on jette.
  Future<void> supprimerPhoto() => _api.delete('/me/photo');

  /// L'adresse signee pour afficher la photo.
  ///
  /// ELLE SE REDEMANDE, ELLE NE SE GARDE PAS. Le service la signe pour quelques
  /// minutes : une adresse mise de cote et ressortie plus tard rend une erreur,
  /// et l'ecran afficherait une image cassee sans savoir pourquoi.
  Future<String> lienPhoto() async {
    final data = await _api.get('/me/photo');
    return data['url'] is String ? data['url'] as String : '';
  }

}

final profilRepositoryProvider = Provider<ProfilRepository>(
  (ref) => ProfilRepository(ref.watch(apiClientProvider)),
);

final profilProvider =
    FutureProvider<Profil>((ref) => ref.watch(profilRepositoryProvider).lire());

/// L'identifiant de la photo, isole du reste du profil.
///
/// POURQUOI LE SORTIR PLUTOT QUE DE SURVEILLER LE PROFIL ENTIER. Le lien
/// d'affichage se redemande quand la PHOTO change — pas quand le client corrige
/// son courriel. Surveiller le profil complet ferait repartir la requete a
/// chaque enregistrement du formulaire, et le portrait clignoterait a chaque
/// fois pour revenir identique.
final idPhotoProvider = Provider<String>(
  (ref) => ref.watch(profilProvider).valueOrNull?.photoMediaId ?? '',
);

/// L'adresse signee pour afficher la photo, ou null quand il n'y en a pas.
///
/// ELLE EXPIRE, ET C'EST ASSUME. Le service la signe pour quelques minutes.
/// L'image est chargee une fois et reste affichee ; si un rebuild tombe apres
/// l'expiration, l'ecran retombe sur les initiales plutot que sur une image
/// cassee, et un tirage vers le bas redemande un lien neuf.
final photoProvider = FutureProvider<String?>((ref) async {
  final id = ref.watch(idPhotoProvider);
  if (id.trim().isEmpty) return null;

  return ref.watch(profilRepositoryProvider).lienPhoto();
});
