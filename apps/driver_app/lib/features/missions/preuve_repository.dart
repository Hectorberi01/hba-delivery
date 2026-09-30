import 'dart:io';

import 'package:flutter_image_compress/flutter_image_compress.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';
import 'package:path/path.dart' as chemin;

import '../../core/providers.dart';

/// A quelle etape une photo se rapporte.
///
/// LE CODE EST CELUI QUE LA PASSERELLE ATTEND, et il est lu sans tenir compte
/// de la casse cote service. Meme lecon que les pieces du dossier : le nom du
/// contrat et celui que le serveur deserialise ne sont pas toujours les memes,
/// et une nature inconnue fait echouer l'envoi sans rien dire d'utile.
enum EtapeDeLaPreuve {
  collecte('collecte'),
  remise('remise');

  const EtapeDeLaPreuve(this.code);

  final String code;
}

/// Le depot d'une photo de course.
///
/// ELLE NE PASSE PAS PAR LA FILE HORS LIGNE, ET C'EST LA DECISION ELLE-MEME.
/// La file range du JSON dans le coffre chiffre, plafonnee a cinquante
/// entrees : elle ne sait pas transporter un fichier. C'est ce fait qui a fait
/// ecarter « photo exigee » (point 7), et c'est lui qui impose ici qu'un envoi
/// rate ne soit PAS reessaye plus tard — une photo rattrapee une heure apres ne
/// prouverait plus le meme instant. Le service la refuserait de toute facon :
/// il borne le depot a une demi-heure apres l'etape.
///
/// LA CLE D'IDEMPOTENCE EST NEUVE A CHAQUE ENVOI, et le serveur ne la lit pas
/// sur cette route : c'est l'agregat qui tranche le rejeu — le meme media deux
/// fois est sans effet, un autre est refuse. Une cle FIXE serait le vrai
/// danger : le jour ou cette route honorerait l'en-tete, un second envoi apres
/// une coupure passerait pour un doublon et la photo n'arriverait jamais, sans
/// qu'aucune erreur ne s'affiche. Meme lecon que le redepot d'une piece du
/// dossier.
class PreuveRepository {
  PreuveRepository(this._api);

  final ApiClient _api;

  /// Cote le plus long apres reduction.
  ///
  /// PLUS PETIT QUE POUR UNE PIECE D'IDENTITE, et pour une raison : une CNI
  /// doit rester LISIBLE — on y cherche un numero —, une photo de remise doit
  /// seulement etre RECONNAISSABLE. Mille deux cents pixels montrent une porte,
  /// un portail, un colis pose ; et le livreur paie ses donnees a la recharge.
  static const _coteMaximal = 1200;

  /// Qualite JPEG. Au-dela de 85 le poids monte sans que l'oeil y gagne.
  static const _qualite = 80;

  Future<void> deposer({
    required String missionId,
    required EtapeDeLaPreuve etape,
    required File fichier,
  }) async {
    final reduit = await _reduire(fichier);

    await _api.upload(
      '/missions/$missionId/proof?etape=${etape.code}',
      formulaire: () async => FormData.fromMap({
        'fichier': await MultipartFile.fromFile(reduit.path, filename: 'preuve.jpg'),
      }),
      idempotencyKey: ApiClient.newIdempotencyKey(),
    );
  }

  /// Reduit et recompresse.
  ///
  /// RENVOIE L'ORIGINAL SI LA REDUCTION ECHOUE plutot que de faire echouer
  /// l'envoi : le serveur plafonne a cinq megaoctets et le dira lui-meme, avec
  /// un message que le livreur peut suivre. Meme raisonnement que pour les
  /// pieces du dossier — une photo refusee par l'application pour une raison
  /// technique, le livreur ne peut rien en faire.
  Future<File> _reduire(File source) async {
    try {
      final cible = chemin.join(
        source.parent.path,
        'hba_preuve_${DateTime.now().millisecondsSinceEpoch}.jpg',
      );

      final resultat = await FlutterImageCompress.compressAndGetFile(
        source.absolute.path,
        cible,
        quality: _qualite,
        minWidth: _coteMaximal,
        minHeight: _coteMaximal,
        format: CompressFormat.jpeg,
      );

      return resultat == null ? source : File(resultat.path);
    } on Object {
      return source;
    }
  }
}

final preuveRepositoryProvider = Provider<PreuveRepository>(
  (ref) => PreuveRepository(ref.watch(apiClientProvider)),
);
