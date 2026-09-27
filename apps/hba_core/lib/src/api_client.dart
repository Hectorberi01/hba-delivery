import 'dart:async';

import 'package:dio/dio.dart';
import 'package:uuid/uuid.dart';

import 'api_exception.dart';
import 'token_store.dart';

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
  Future<void>? _refreshInFlight;

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
  /// SON DELAI EST A PART, ET C'EST TOUT L'INTERET DE CETTE METHODE. Le
  /// receiveTimeout general est a quinze secondes : correct pour du JSON,
  /// ridicule pour cinq megaoctets depuis une 3G a Cotonou. Passer par
  /// « post » ferait echouer des envois parfaitement en cours.
  Future<Map<String, dynamic>> upload(
    String path, {
    required FormData formulaire,
    required String idempotencyKey,
    Duration delai = const Duration(minutes: 3),
    void Function(int envoye, int total)? progression,
  }) =>
      _send(() => _dio.post<Map<String, dynamic>>(
            path,
            data: formulaire,
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
          error.type == DioExceptionType.receiveTimeout) {
        throw const OfflineException();
      }

      if (error.response?.statusCode == 401 && allowRetry) {
        if (await _refreshOnce()) {
          return _send(call, allowRetry: false);
        }
      }

      throw _toApiException(error);
    }
  }

  /// Vrai si la session est utilisable apres l'appel.
  Future<bool> _refreshOnce() async {
    final pending = _refreshInFlight;
    if (pending != null) {
      await pending;
      return _tokens.accessToken != null;
    }

    final completer = Completer<void>();
    _refreshInFlight = completer.future;

    try {
      final refreshToken = await _tokens.readRefreshToken();
      if (refreshToken == null) {
        await _onSessionLost();
        return false;
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
        await _onSessionLost();
        return false;
      }

      // Ecrit avant tout autre appel : un jeton recu mais non ecrit est un
      // jeton perdu si l'application s'arrete maintenant.
      await _tokens.save(accessToken: access, refreshToken: refresh);
      return true;
    } on DioException {
      await _onSessionLost();
      return false;
    } finally {
      completer.complete();
      _refreshInFlight = null;
    }
  }

  static ApiException _toApiException(DioException error) {
    final body = error.response?.data;
    if (body is Map && body['code'] is String) {
      return ApiException(
        code: body['code'] as String,
        message: body['message'] as String? ?? 'Action refusee.',
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
