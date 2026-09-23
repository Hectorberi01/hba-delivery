import 'dart:async';

import 'package:dio/dio.dart';
import 'package:uuid/uuid.dart';

import 'api_exception.dart';
import 'token_store.dart';

/// Acces au Driver BFF.
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

  /// UN SEUL RAFRAICHISSEMENT A LA FOIS. Avec une file d'actions qui repart
  /// d'un coup au retour du reseau, dix appels peuvent recevoir 401 en meme
  /// temps. S'ils rafraichissent tous, le deuxieme rejoue un jeton deja
  /// consomme et le service revoque la session entiere. Les suivants attendent
  /// donc le premier.
  Future<void>? _refreshInFlight;

  Future<Map<String, dynamic>> get(String path, {Map<String, dynamic>? query}) =>
      _send(() => _dio.get<Map<String, dynamic>>(path, queryParameters: query));

  /// Appel mutant. La cle d'idempotence est obligatoire : elle est ce qui rend
  /// le rejeu d'une action en file inoffensif.
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

  static String newIdempotencyKey() => _uuid.v4();

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

      final status = error.response?.statusCode;

      if (status == 401 && allowRetry) {
        final refreshed = await _refreshOnce();
        if (refreshed) {
          return _send(call, allowRetry: false);
        }
      }

      throw _toApiException(error);
    }
  }

  /// Renvoie vrai si la session est utilisable apres l'appel.
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
    if (body is Map && body['code'] is String && body['message'] is String) {
      return ApiException(
        code: body['code'] as String,
        message: body['message'] as String,
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

/// Construit le Dio de l'application : base d'URL, delais, jeton porteur.
Dio buildDio({required String baseUrl, required TokenStore tokens}) {
  final dio = Dio(BaseOptions(
    baseUrl: baseUrl,
    connectTimeout: const Duration(seconds: 8),
    receiveTimeout: const Duration(seconds: 15),
    // Les erreurs metier sont traitees par le client, pas levees par Dio.
    validateStatus: (status) => status != null && status < 400,
    contentType: 'application/json',
  ));

  dio.interceptors.add(InterceptorsWrapper(
    onRequest: (options, handler) {
      // Le rafraichissement passe explicitement Authorization a null pour ne
      // pas presenter un jeton expire a une route qui n'en veut pas.
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
