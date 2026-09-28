import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';

import '../../core/providers.dart';

/// Le profil du client, tel que le service l'a.
///
/// TROIS CHAMPS, ET TROIS SEULEMENT. Le contrat en declare davantage —
/// identifiant, adresses favorites, date de creation — mais l'ecran ne modifie
/// que ce que « PUT /me » accepte : le nom et le courriel. Le telephone est
/// l'identifiant du compte ; il se change en changeant de compte, pas dans un
/// formulaire.
class Profil {
  const Profil({
    required this.nom,
    required this.telephone,
    required this.courriel,
  });

  final String nom;
  final String telephone;
  final String courriel;

  bool get complet => nom.trim().isNotEmpty && courriel.trim().isNotEmpty;

  static Profil depuis(Map<String, dynamic> json) => Profil(
        nom: json['displayName'] is String ? json['displayName'] as String : '',
        telephone: json['phone'] is String ? json['phone'] as String : '',
        courriel: json['email'] is String ? json['email'] as String : '',
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
}

final profilRepositoryProvider = Provider<ProfilRepository>(
  (ref) => ProfilRepository(ref.watch(apiClientProvider)),
);

final profilProvider =
    FutureProvider<Profil>((ref) => ref.watch(profilRepositoryProvider).lire());
