import 'dart:async';

import 'package:dio/dio.dart';
import 'package:uuid/uuid.dart';

import 'api_exception.dart';
import 'token_store.dart';

/// Ce que vaut une tentative de rafraichissement du jeton d'acces.
///
/// TROIS ISSUES, ET LA TROISIEME EST TOUTE LA CORRECTION DU 30 SEPTEMBRE 2026.
/// Le code n'en connaissait que deux — rafraichi, ou session perdue — et rangeait
/// donc dans « session perdue » tout ce qui n'etait pas un succes : une coupure
/// reseau, un 502 pendant un redeploiement, un 429. Or « je n'ai pas pu
/// rafraichir » ne veut pas dire « la session est morte », et les confondre
/// detruisait la file d'actions du livreur.
enum IssueDeRafraichissement {
  /// Nouveau jeton en main : l'appel d'origine peut etre rejoue.
  rafraichi,

  /// Le SERVEUR a refuse le jeton de rafraichissement. La session est finie, il
  /// faut se reconnecter.
  sessionPerdue,

  /// On ne sait pas. Le service n'a pas repondu, ou a repondu une panne. La
  /// session est peut-etre intacte : on ne touche a rien.
  indisponible,
}

/// Ce qu'il faut conclure d'un rafraichissement refuse.
///
/// LA TABLE EST CALQUEE SUR CE QUE LE SERVEUR ENVOIE VRAIMENT, et cela valait
/// la verification : les quatre chemins de refus d'Identity — jeton inconnu,
/// revoque, expire, rejoue — levent tous une « ForbiddenException », qui sort en
/// 403. Le 401 n'apparait pas sur cette route, qui est anonyme.
///
/// LE 400 NE DECONNECTE PAS, ET C'EST LE PIEGE A EVITER. Il vient d'un
/// « VALIDATION_FAILED », c'est-a-dire d'une requete mal formee — un defaut de
/// l'application, pas du jeton. S'en servir pour fermer la session ferait payer
/// a l'utilisateur, et au livreur sa file, un bug de notre cote. C'est
/// exactement ce que recommandait le rapport d'audit ; il avait tort.
IssueDeRafraichissement issueDUnRefusDeRafraichissement({
  required int? statutHttp,
  required String? codeMetier,
}) {
  // Aucune reponse HTTP : le transport a echoue. On ne conclut rien.
  if (statutHttp == null) {
    return IssueDeRafraichissement.indisponible;
  }

  switch (statutHttp) {
    // Le cas canonique : Identity a refuse le jeton.
    case 403:
    // Le compte a ete efface : la session n'a plus de titulaire.
    case 404:
    // La route est anonyme, donc ceci ne devrait pas arriver — mais un 401 sur
    // le rafraichissement lui-meme ne peut signifier qu'un refus.
    case 401:
      return IssueDeRafraichissement.sessionPerdue;

    // 409 EST AMBIGU, ET NE SE TRAITE DONC PAS AU STATUT SEUL. C'est la
    // traduction par defaut de n'importe quelle regle metier refusee
    // (FailedPrecondition), et non la marque d'une session morte. Un seul code
    // y designe vraiment la fin : le jeton avait deja servi.
    case 409:
      return codeMetier == 'REFRESH_TOKEN_REUSED'
          ? IssueDeRafraichissement.sessionPerdue
          : IssueDeRafraichissement.indisponible;

    default:
      // 400 compris, et tout le reste : 408, 429, 5xx, et l'inattendu. Quand on
      // ne sait pas, on ne detruit rien.
      return IssueDeRafraichissement.indisponible;
  }
}

/// Acces a un BFF HBA.
///
/// Deux responsabilites que rien d'autre ne porte : poser une cle
/// d'idempotence sur chaque appel mutant, et serialiser le rafraichissement du
/// jeton.
class ApiClient {
  ApiClient({
    required Dio dio,
    required TokenStore tokens,
    required Future<void> Function() onSessionLost,
  })  : _dio = dio,
        _tokens = tokens,
        _onSessionLost = onSessionLost;

  static const _uuid = Uuid();

  final Dio _dio;
  final TokenStore _tokens;
  final Future<void> Function() _onSessionLost;

  /// UN SEUL RAFRAICHISSEMENT A LA FOIS. Au retour du reseau, dix appels
  /// peuvent recevoir 401 en meme temps. S'ils rafraichissent tous, le
  /// deuxieme rejoue un jeton deja consomme et le service revoque la session
  /// entiere. Les suivants attendent donc le premier.
  Future<IssueDeRafraichissement>? _refreshInFlight;

  static String newIdempotencyKey() => _uuid.v4();

  Future<Map<String, dynamic>> get(String path, {Map<String, dynamic>? query}) =>
      _send(() => _dio.get<Map<String, dynamic>>(path, queryParameters: query));

  /// Appel mutant. La cle d'idempotence est obligatoire : elle rend le rejeu
  /// d'une action mise en file inoffensif.
  Future<Map<String, dynamic>> post(
    String path, {
    Object? body,
    required String idempotencyKey,
  }) =>
      _send(() => _dio.post<Map<String, dynamic>>(
            path,
            data: body,
            options: Options(headers: {'Idempotency-Key': idempotencyKey}),
          ));

  /// Envoi d'un fichier, en multipart.
  ///
  /// SON DELAI EST A PART, ET C'EST UNE DES DEUX RAISONS D'ETRE DE CETTE
  /// METHODE. Le receiveTimeout general est a quinze secondes : correct pour du
  /// JSON, ridicule pour cinq megaoctets depuis une 3G a Cotonou. Passer par
  /// « post » ferait echouer des envois parfaitement en cours.
  ///
  /// LA SECONDE EST LA FABRIQUE, ET ELLE CORRIGE UN DEFAUT QUI SE VOYAIT MAL.
  ///
  /// Ce parametre etait une FormData toute faite. Or « _send » REJOUE son appel
  /// apres un rafraichissement de jeton reussi — et une FormData Dio est un
  /// flux a USAGE UNIQUE : le second envoi levait « Cannot finalize », que
  /// l'application traduisait en « Une erreur inattendue s'est produite ».
  ///
  /// LE SCENARIO EST CELUI DE TOUS LES JOURS. Le livreur remplit son dossier,
  /// prend ses photos, appuie sur envoyer un quart d'heure apres sa derniere
  /// requete : le jeton a expire. Premier envoi 401, rafraichissement reussi,
  /// rejeu ECHOUE. Il recommence, et cette fois cela marche — sans qu'il
  /// comprenne pourquoi. Meme chose pour la photo de profil du client et pour
  /// la photo d'une etape de course.
  ///
  /// LA DOCUMENTATION DE DIO DIT EXACTEMENT CE QU'IL FAUT FAIRE : « You should
  /// make a new FormData or MultipartFile every time in repeated requests ».
  /// D'ou une fabrique plutot qu'une instance — c'est la seule forme ou
  /// l'appelant NE PEUT PAS se tromper, alors qu'avec une instance il fallait y
  /// penser a chaque nouvel appelant.
  ///
  /// ELLE EST ASYNCHRONE parce que MultipartFile.fromFile l'est : une fabrique
  /// synchrone aurait oblige chaque appelant a construire ses fichiers dehors,
  /// c'est-a-dire une seule fois, c'est-a-dire a reintroduire le defaut.
  Future<Map<String, dynamic>> upload(
    String path, {
    required Future<FormData> Function() formulaire,
    required String idempotencyKey,
    Duration delai = const Duration(minutes: 3),
    void Function(int envoye, int total)? progression,
  }) =>
      _send(() async => _dio.post<Map<String, dynamic>>(
            path,
            // APPELEE A CHAQUE TENTATIVE, rejeu compris. C'est tout le
            // correctif : la fabrique est ici, dans la closure que « _send »
            // rappelle, et non dehors.
            data: await formulaire(),
            onSendProgress: progression,
            options: Options(
              headers: {'Idempotency-Key': idempotencyKey},
              sendTimeout: delai,
              receiveTimeout: delai,
              // Dio pose lui-meme la frontiere du multipart ; imposer
              // application/json comme le fait BaseOptions rendrait le corps
              // illisible pour le serveur.
              contentType: 'multipart/form-data',
            ),
          ));

  Future<Map<String, dynamic>> put(String path, {Object? body}) =>
      _send(() => _dio.put<Map<String, dynamic>>(path, data: body));

  Future<void> delete(String path) async {
    await _send(() => _dio.delete<Map<String, dynamic>>(path));
  }

  Future<Map<String, dynamic>> _send(
    Future<Response<Map<String, dynamic>>> Function() call, {
    bool allowRetry = true,
  }) async {
    try {
      final response = await call();
      return response.data ?? const <String, dynamic>{};
    } on DioException catch (error) {
      if (error.type == DioExceptionType.connectionError ||
          error.type == DioExceptionType.connectionTimeout ||
          error.type == DioExceptionType.receiveTimeout ||
          // sendTimeout manquait, et il ne concerne qu'un seul appelant :
          // « upload », qui pose son propre delai d'envoi. Un depot de piece
          // interrompu en 3G ressortait donc en « erreur inattendue » au lieu
          // d'une absence de reseau.
          error.type == DioExceptionType.sendTimeout) {
        throw const OfflineException();
      }

      if (error.response?.statusCode == 401 && allowRetry) {
        switch (await _refreshOnce()) {
          case IssueDeRafraichissement.rafraichi:
            // LE REJEU RAPPELLE LA MEME CLOSURE, il ne renvoie pas le meme
            // corps : tout ce qui est a usage unique doit etre construit A
            // L'INTERIEUR de « call ». C'est ce que fait « upload » avec sa
            // fabrique, et c'est ce que doit faire tout nouvel appel qui
            // porterait un flux.
            return _send(call, allowRetry: false);

          case IssueDeRafraichissement.indisponible:
            // ON NE DECONNECTE PAS, ET ON NE REND PAS LE 401 D'ORIGINE.
            //
            // Rien ne dit que la session est morte : le rafraichissement n'a
            // pas abouti, voila tout. « OfflineException » est le diagnostic
            // honnete, et il a une deuxieme vertu, decisive cote livreur : les
            // ecrans METTENT EN FILE ce qui echoue ainsi, au lieu de
            // l'abandonner. Une remise prise dans ce cas part en file et
            // repartira ; elle etait perdue avant.
            throw const OfflineException();

          case IssueDeRafraichissement.sessionPerdue:
            // « _onSessionLost » a deja fait le necessaire. Le 401 d'origine
            // remonte tel quel, et le routeur renvoie vers la connexion.
            break;
        }
      }

      throw _toApiException(error);
    }
  }

  /// Rafraichit le jeton, en n'en faisant qu'un a la fois.
  ///
  /// LES APPELANTS CONCURRENTS PARTAGENT LA MEME ISSUE. La version precedente
  /// attendait le rafraichissement en cours puis DEVINAIT son resultat en
  /// regardant si un jeton d'acces existait encore — or un echec ne l'effacait
  /// pas : les appels en attente concluaient « c'est bon », rejouaient avec le
  /// jeton expire et reprenaient un 401. On rend maintenant l'issue reelle.
  Future<IssueDeRafraichissement> _refreshOnce() async {
    final enCours = _refreshInFlight;
    if (enCours != null) {
      return enCours;
    }

    final travail = _rafraichir();
    _refreshInFlight = travail;

    try {
      return await travail;
    } finally {
      _refreshInFlight = null;
    }
  }

  /// UNE SEULE CHOSE COMPTE ICI : NE JAMAIS CONCLURE A TORT QUE LA SESSION EST
  /// MORTE. C'etait le defaut du 29 septembre 2026, et il coutait une preuve de
  /// livraison.
  ///
  /// Tout « DioException » declenchait la deconnexion — coupure reseau,
  /// expiration, 502 pendant un redeploiement, 429. Cote livreur, la
  /// deconnexion PURGE la file d'actions hors ligne : le livreur qui remontait
  /// d'un parking, jeton expire et remise en attente, se retrouvait a l'ecran
  /// de connexion, sa remise detruite, la course ouverte et le client livre.
  /// Cote client, chaque redeploiement de la passerelle ejectait tout le monde.
  ///
  /// ELLE NE LEVE PAS. Un appelant qui recoit une exception d'ici ne saurait
  /// qu'en faire, et le defaut par defaut doit etre le plus prudent : quand on
  /// ne sait pas, « indisponible ».
  Future<IssueDeRafraichissement> _rafraichir() async {
    try {
      final refreshToken = await _tokens.readRefreshToken();
      if (refreshToken == null) {
        // Sans jeton de rafraichissement, il n'y a rien a tenter : c'est le
        // seul cas ou l'absence, et non le serveur, prononce la fin.
        await _onSessionLost();
        return IssueDeRafraichissement.sessionPerdue;
      }

      final deviceId = await _tokens.deviceId(_uuid.v4);

      final response = await _dio.post<Map<String, dynamic>>(
        '/auth/refresh',
        data: {'refreshToken': refreshToken, 'deviceId': deviceId},
        options: Options(headers: {'Authorization': null}),
      );

      final data = response.data ?? const <String, dynamic>{};
      final access = data['accessToken'] as String?;
      final refresh = data['refreshToken'] as String?;

      if (access == null || refresh == null) {
        // UN 200 SANS JETONS EST UNE PANNE DE SERVICE, PAS UNE SESSION MORTE,
        // et la nuance vaut une remise : deconnecter ici detruirait la file
        // pour un defaut qui n'est pas celui de l'utilisateur.
        return IssueDeRafraichissement.indisponible;
      }

      // Ecrit avant tout autre appel : un jeton recu mais non ecrit est un
      // jeton perdu si l'application s'arrete maintenant.
      await _tokens.save(accessToken: access, refreshToken: refresh);
      return IssueDeRafraichissement.rafraichi;
    } on DioException catch (erreur) {
      final corps = erreur.response?.data;

      final issue = issueDUnRefusDeRafraichissement(
        statutHttp: erreur.response?.statusCode,
        codeMetier: corps is Map && corps['code'] is String ? corps['code'] as String : null,
      );

      if (issue == IssueDeRafraichissement.sessionPerdue) {
        // HORS DU « try » PRINCIPAL, DONC PROTEGE A PART. Une exception ici
        // sortirait de cette methode, et son issue est PARTAGEE par tous les
        // appels concurrents qui attendent le meme rafraichissement : ils la
        // recevraient tous. La promesse de ne pas lever ne tient que si elle
        // tient partout.
        try {
          await _onSessionLost();
        } on Object {
          // La purge a echoue ; la session est finie quand meme.
        }
      }

      return issue;
    } on Object {
      // Coffre illisible, reponse inattendue : on ne detruit rien.
      return IssueDeRafraichissement.indisponible;
    }
  }

  static ApiException _toApiException(DioException error) {
    final body = error.response?.data;
    if (body is Map && body['code'] is String) {
      return ApiException(
        code: body['code'] as String,
        message: body['message'] as String? ?? 'Action refusée.',
        statusCode: error.response?.statusCode,
      );
    }

    return ApiException(
      code: 'UNEXPECTED',
      message: "Une erreur inattendue s'est produite. Reessayez.",
      statusCode: error.response?.statusCode,
    );
  }
}

/// Construit le Dio d'une application : base d'URL, delais, jeton porteur.
Dio buildDio({required String baseUrl, required TokenStore tokens}) {
  final dio = Dio(BaseOptions(
    baseUrl: baseUrl,
    connectTimeout: const Duration(seconds: 8),
    receiveTimeout: const Duration(seconds: 15),
    validateStatus: (status) => status != null && status < 400,
    contentType: 'application/json',
  ));

  dio.interceptors.add(InterceptorsWrapper(
    onRequest: (options, handler) {
      // Le rafraichissement passe Authorization a null pour ne pas presenter
      // un jeton expire a une route qui n'en veut pas.
      if (!options.headers.containsKey('Authorization')) {
        final token = tokens.accessToken;
        if (token != null) {
          options.headers['Authorization'] = 'Bearer $token';
        }
      } else if (options.headers['Authorization'] == null) {
        options.headers.remove('Authorization');
      }

      handler.next(options);
    },
  ));

  return dio;
}
