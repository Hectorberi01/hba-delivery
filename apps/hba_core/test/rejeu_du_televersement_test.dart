import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:hba_core/hba_core.dart';

/// Le rejeu d'un televersement apres un jeton expire.
///
/// POURQUOI CE TEST EXISTE. « _send » rejoue son appel quand un 401 est suivi
/// d'un rafraichissement reussi. Tant que « upload » recevait une FormData TOUTE
/// FAITE, ce rejeu renvoyait la MEME — or une FormData Dio est un flux a usage
/// unique : le second envoi levait « Cannot finalize », que l'application
/// traduisait en « Une erreur inattendue s'est produite ».
///
/// LE SCENARIO N'A RIEN D'EXOTIQUE, c'est le constat S7 de l'audit du
/// 30 septembre 2026 : le livreur remplit son dossier, prend ses photos, appuie
/// sur envoyer un quart d'heure apres sa derniere requete. Le jeton a expire.
/// Premier envoi 401, rafraichissement reussi, rejeu ECHOUE. Il recommence, et
/// cette fois cela marche — sans qu'il comprenne pourquoi. Meme chose pour la
/// photo de profil du client et pour la photo d'une etape de course.
///
/// CE QUI EST EPROUVE ICI EST UNE PROPRIETE, PAS UN CAS : le corps est
/// reconstruit A CHAQUE TENTATIVE. C'est ce que demande la documentation de Dio
/// — « You should make a new FormData or MultipartFile every time in repeated
/// requests » —, et la fabrique est la seule forme ou l'appelant ne peut pas
/// l'oublier.
void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  ResponseBody reponse(Map<String, Object?> corps, int statut) =>
      ResponseBody.fromString(
        jsonEncode(corps),
        statut,
        headers: {
          Headers.contentTypeHeader: [Headers.jsonContentType],
        },
      );

  test('un depot refuse en 401 repart avec un corps NEUF', () async {
    FlutterSecureStorage.setMockInitialValues({'test.refresh': 'r1'});

    var depots = 0;
    var rafraichissements = 0;

    final adaptateur = _AdaptateurFactice((requete) {
      if (requete.path == '/auth/refresh') {
        rafraichissements += 1;
        return reponse(const {'accessToken': 'a2', 'refreshToken': 'r2'}, 200);
      }

      depots += 1;

      // Le premier depot tombe sur un jeton expire ; le second doit passer —
      // et il ne passe QUE si son corps a ete reconstruit.
      return depots == 1
          ? reponse(const {'code': 'TOKEN_EXPIRED'}, 401)
          : reponse(const {'id': 'media-1'}, 200);
    });

    final dio = Dio(BaseOptions(baseUrl: 'http://exemple.invalide'))
      ..httpClientAdapter = adaptateur;

    final coffre = TokenStore(const FlutterSecureStorage(), prefix: 'test');
    await coffre.load();

    var sessionPerdue = false;

    final api = ApiClient(
      dio: dio,
      tokens: coffre,
      onSessionLost: () async => sessionPerdue = true,
    );

    var fabriques = 0;

    final rendu = await api.upload(
      '/documents',
      formulaire: () async {
        fabriques += 1;
        return FormData.fromMap({
          'fichier': MultipartFile.fromString(
            'des octets',
            filename: 'piece.jpg',
          ),
        });
      },
      idempotencyKey: 'cle-1',
    );

    expect(
      fabriques,
      2,
      reason: 'la fabrique doit etre rappelee au rejeu, sans quoi le corps '
          'renvoye est celui qui a deja ete finalise',
    );
    expect(depots, 2, reason: 'le second envoi doit atteindre le serveur');
    expect(rafraichissements, 1);
    expect(rendu['id'], 'media-1', reason: 'c\'est la reponse du SECOND envoi');

    // UN 401 SUIVI D'UN RAFRAICHISSEMENT REUSSI NE FERME RIEN. Cote livreur,
    // fermer la session PURGE la file d'actions hors ligne.
    expect(sessionPerdue, isFalse);
  });

  test('sans 401, la fabrique n\'est appelee qu\'une fois', () async {
    FlutterSecureStorage.setMockInitialValues({'test.refresh': 'r1'});

    final adaptateur =
        _AdaptateurFactice((_) => reponse(const {'id': 'media-2'}, 200));

    final dio = Dio(BaseOptions(baseUrl: 'http://exemple.invalide'))
      ..httpClientAdapter = adaptateur;

    final api = ApiClient(
      dio: dio,
      tokens: TokenStore(const FlutterSecureStorage(), prefix: 'test'),
      onSessionLost: () async {},
    );

    var fabriques = 0;

    await api.upload(
      '/documents',
      formulaire: () async {
        fabriques += 1;
        return FormData.fromMap({
          'fichier': MultipartFile.fromString('des octets', filename: 'p.jpg'),
        });
      },
      idempotencyKey: 'cle-2',
    );

    // LE CAS NORMAL NE DOIT RIEN PAYER. Une fabrique appelee deux fois a chaque
    // envoi doublerait la lecture des fichiers sur un telephone d'entree de
    // gamme, pour un rejeu qui n'arrive presque jamais.
    expect(fabriques, 1);
  });
}

/// Un adaptateur HTTP qui repond ce qu'on lui dit, sans reseau.
class _AdaptateurFactice implements HttpClientAdapter {
  _AdaptateurFactice(this._repondre);

  final ResponseBody Function(RequestOptions requete) _repondre;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    // ON CONSOMME LE FLUX, ET PAS PAR PROPRETE : c'est sa lecture qui finalise
    // la FormData. Un adaptateur qui l'ignore laisserait passer le defaut meme
    // s'il revenait.
    if (requestStream != null) {
      await requestStream.toList();
    }

    return _repondre(options);
  }

  @override
  void close({bool force = false}) {}
}
