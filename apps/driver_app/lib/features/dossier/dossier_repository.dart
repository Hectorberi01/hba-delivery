import 'dart:io';

import 'package:flutter_image_compress/flutter_image_compress.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';
import 'package:path/path.dart' as chemin;

import '../../core/providers.dart';

/// Nature d'une piece.
///
/// LE CODE EST CELUI QUE LA PASSERELLE REND, pas celui du fichier .proto.
/// Protobuf nomme la valeur « DOCUMENT_TYPE_NATIONAL_ID » ; la passerelle,
/// elle, serialise l'enum C# genere et ecrit « NationalId ». J'avais pris le
/// nom du contrat : l'envoi etait refuse en « nature inconnue », et la
/// relecture ne reconnaissait aucune piece deposee — deux pannes pour une
/// seule confusion.
enum Piece {
  nationalId('NationalId', 'Carte d\'identité', 'Recto, bien à plat et lisible'),
  drivingLicence('DrivingLicence', 'Permis de conduire', 'La page avec votre photo'),
  vehicleRegistration('VehicleRegistration', 'Carte grise', 'Du véhicule que vous conduisez'),
  identityPhoto('IdentityPhoto', 'Votre photo', 'Visage dégagé, sans lunettes de soleil'),
  vehiclePhoto('VehiclePhoto', 'Photo du véhicule', 'De cote, avec la plaque visible');

  const Piece(this.code, this.libelle, this.aide);

  final String code;
  final String libelle;
  final String aide;

  static Piece? parCode(String code) {
    for (final p in Piece.values) {
      if (p.code == code) return p;
    }
    return null;
  }
}

/// Une piece deja deposee.
class PieceDeposee {
  const PieceDeposee({
    required this.type,
    required this.deposeeLe,
    required this.url,
  });

  final Piece? type;
  final DateTime? deposeeLe;
  final String url;
}

/// Le dossier, vu de l'application.
class Dossier {
  const Dossier({
    required this.statut,
    required this.motif,
    required this.pieces,
    required this.manquantes,
    required this.vehiculeType,
    required this.plaque,
    required this.photoProfil,
    required this.peutSoumettre,
    required this.soumisLe,
  });

  final String statut;
  final String motif;
  final List<PieceDeposee> pieces;
  final List<Piece> manquantes;
  final String vehiculeType;
  final String plaque;
  final String photoProfil;
  final bool peutSoumettre;
  final DateTime? soumisLe;

  bool get enAttente => statut == 'PendingVerification';
  bool get valide => statut == 'Verified';
  bool get rejete => statut == 'Rejected';
  bool get suspendu => statut == 'Suspended';

  /// Le vehicule est-il declare ? La plaque fait foi : le type nait a
  /// « Motorcycle » sans que personne ne l'ait dit.
  bool get vehiculeDeclare => plaque.isNotEmpty;

  static DateTime? _date(Object? brut) =>
      brut is String && brut.isNotEmpty ? DateTime.tryParse(brut)?.toLocal() : null;

  static Dossier fromJson(Map<String, dynamic> json) {
    final vehicule = json['vehicle'];
    final pieces = json['documents'];
    final manquantes = json['missingDocuments'];

    return Dossier(
      statut: json['verificationStatus'] as String? ?? '',
      motif: json['statusReason'] as String? ?? '',
      pieces: [
        if (pieces is List)
          for (final p in pieces)
            if (p is Map)
              PieceDeposee(
                type: Piece.parCode(p['type'] as String? ?? ''),
                deposeeLe: _date(p['uploadedAt']),
                url: p['readUrl'] as String? ?? '',
              ),
      ],
      manquantes: [
        if (manquantes is List)
          for (final m in manquantes)
            if (Piece.parCode(m as String? ?? '') case final piece?) piece,
      ],
      vehiculeType: vehicule is Map ? vehicule['type'] as String? ?? '' : '',
      plaque: vehicule is Map ? vehicule['plate'] as String? ?? '' : '',
      photoProfil: json['profilePhotoUrl'] as String? ?? '',
      peutSoumettre: json['canSubmit'] as bool? ?? false,
      soumisLe: _date(json['submittedAt']),
    );
  }
}

class DossierRepository {
  DossierRepository(this._api);

  final ApiClient _api;

  /// Cote le plus long apres reduction.
  ///
  /// MILLE SIX CENTS PIXELS SUFFISENT A LIRE UNE CNI, et ramenent une photo
  /// de huit megaoctets a quelques centaines de kilooctets. Le livreur paie
  /// ses donnees a la recharge : ce n'est pas une optimisation, c'est de
  /// l'argent qu'on ne lui prend pas.
  static const _coteMaximal = 1600;

  /// Qualite JPEG. Au-dela de 85, le poids monte sans que l'oeil y gagne.
  static const _qualite = 82;

  Future<Dossier> lire() async => Dossier.fromJson(await _api.get('/application'));

  Future<void> declarerVehicule({
    required String type,
    required String plaque,
    int? capaciteGrammes,
  }) =>
      _api.put('/vehicle', body: {
        'type': type,
        'plate': plaque,
        if (capaciteGrammes != null) 'capacityGrams': capaciteGrammes,
      });

  Future<void> soumettre() => _api.post(
        '/application/submit',
        body: const <String, Object?>{},
        idempotencyKey: ApiClient.newIdempotencyKey(),
      );

  Future<void> deposer({
    required Piece piece,
    required File fichier,
    void Function(int envoye, int total)? progression,
  }) async {
    final reduit = await _reduire(fichier);

    await _api.upload(
      '/documents?type=${piece.code}',
      formulaire: FormData.fromMap({
        'fichier': await MultipartFile.fromFile(
          reduit.path,
          filename: 'piece.jpg',
        ),
      }),
      // UNE CLE NEUVE A CHAQUE DEPOT. Elle etait liee a la nature de la
      // piece, pour qu'un renvoi apres coupure ne fasse pas deux depots.
      // Mais un redepot n'est pas un renvoi : le livreur qui remplace sa
      // carte d'identite rejetee refait exprement la meme requete. Avec une
      // cle fixe, le jour ou cette route honorera l'en-tete, ce second
      // depot serait pris pour un doublon et la piece corrigee ne
      // remplacerait jamais l'ancienne — sans qu'aucune erreur ne s'affiche.
      idempotencyKey: ApiClient.newIdempotencyKey(),
      progression: progression,
    );
  }

  Future<void> deposerPhotoProfil(File fichier) async {
    final reduit = await _reduire(fichier);

    await _api.upload(
      '/profile-photo',
      formulaire: FormData.fromMap({
        'fichier': await MultipartFile.fromFile(reduit.path, filename: 'profil.jpg'),
      }),
      idempotencyKey: ApiClient.newIdempotencyKey(),
    );
  }

  /// Reduit et recompresse.
  ///
  /// RENVOIE L'ORIGINAL SI LA REDUCTION ECHOUE plutot que de faire echouer
  /// l'envoi : le serveur plafonne a cinq megaoctets et le dira lui-meme,
  /// avec un message que le livreur peut suivre. Une piece refusee par
  /// l'application pour une raison technique, il ne peut rien en faire.
  Future<File> _reduire(File source) async {
    try {
      final dossier = source.parent.path;
      final cible = chemin.join(
        dossier,
        'hba_${DateTime.now().millisecondsSinceEpoch}.jpg',
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

final dossierRepositoryProvider = Provider<DossierRepository>(
  (ref) => DossierRepository(ref.watch(apiClientProvider)),
);

/// Relu a chaque affichage : ops peut avoir tranche pendant que l'ecran
/// etait ouvert.
final dossierProvider = FutureProvider.autoDispose<Dossier>(
  (ref) => ref.watch(dossierRepositoryProvider).lire(),
);
