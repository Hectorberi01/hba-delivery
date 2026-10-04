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
    required this.exigees,
    required this.vehiculeType,
    required this.plaque,
    required this.capaciteGrammes,
    required this.placesPassager,
    required this.marque,
    required this.modele,
    required this.couleur,
    required this.capacites,
    required this.capacitesPossibles,
    required this.photoProfil,
    required this.peutSoumettre,
    required this.vehiculeDeclare,
    required this.soumisLe,
  });

  final String statut;
  final String motif;
  final List<PieceDeposee> pieces;
  final List<Piece> manquantes;

  /// Les pieces exigees POUR CE VEHICULE, deposees ou non.
  ///
  /// ELLE VIENT DU SERVICE, ET CE N'EST PAS UN CAPRICE. L'ecran affichait les
  /// cinq natures en dur ; depuis que le velo existe (30 septembre 2026), un
  /// cycliste y voyait « permis » et « carte grise », deux lignes qu'il n'aurait
  /// jamais pu satisfaire. La regle est metier : la recopier ici en aurait fait
  /// une seconde version, qui aurait diverge.
  final List<Piece> exigees;

  final String vehiculeType;
  final String plaque;

  /// Charge utile declaree, en grammes. ZERO VEUT DIRE « NON DECLAREE ».
  final int capaciteGrammes;

  /// Places passagers, conducteur non compris. ZERO VAUT « AUCUNE », pas
  /// « inconnu » : un vehicule sans place declaree n'en prend pas, et le
  /// dispatch ne lui envoie aucune course de personnes.
  final int placesPassager;

  /// Marque, modele et couleur. DECLARATIFS : ils servent au client a
  /// reconnaitre le vehicule qui arrive, pas a autoriser quoi que ce soit.
  /// Aucun ecran ne les saisit encore ; l'application les relit et les renvoie
  /// tels quels pour ne pas les effacer en enregistrant le vehicule.
  final String marque;
  final String modele;
  final String couleur;

  /// Ce que le livreur a declare exercer. VIDE VEUT DIRE « LE COLIS » : c'est
  /// le socle, il ne se declare pas.
  final List<String> capacites;

  /// Ce que son vehicule permet, habilite ou non. ELLE VIENT DU SERVICE, pour
  /// la meme raison que [exigees] : la table vehicule vers habilitations est
  /// une regle metier, et la recopier ici en aurait fait une seconde version.
  /// Elle contient le colis, que l'ecran n'affiche pas comme une case.
  final List<String> capacitesPossibles;

  final String photoProfil;
  final bool peutSoumettre;

  /// Le vehicule est-il declare ? RENDU PAR LE SERVICE depuis le 30 septembre
  /// 2026. L'application le devinait par « la plaque n'est pas vide » — un velo
  /// n'en a pas, et serait reste « non declare » pour toujours.
  final bool vehiculeDeclare;

  final DateTime? soumisLe;

  bool get enAttente => statut == 'PendingVerification';
  bool get valide => statut == 'Verified';
  bool get rejete => statut == 'Rejected';
  bool get suspendu => statut == 'Suspended';

  static int _entier(Object? brut) => switch (brut) {
        final int n => n,
        final num n => n.toInt(),
        final String s => int.tryParse(s) ?? 0,
        _ => 0,
      };

  /// Une liste de noms, rendue vide plutot que nulle.
  ///
  /// UN SERVICE PLUS ANCIEN QUI NE RENVERRAIT PAS LE CHAMP DONNE UNE LISTE
  /// VIDE, et l'ecran n'affiche alors aucune habilitation a cocher — ce qui est
  /// exact : sans la liste du service, l'application ne sait pas ce que le
  /// vehicule permet, et deviner serait pire que se taire.
  static List<String> _mots(Object? brut) => [
        if (brut is List)
          for (final e in brut)
            if (e is String && e.isNotEmpty) e,
      ];

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
      exigees: [
        if (json['requiredDocuments'] case final List brut)
          for (final e in brut)
            if (Piece.parCode(e as String? ?? '') case final piece?) piece,
      ],
      vehiculeType: vehicule is Map ? vehicule['type'] as String? ?? '' : '',
      plaque: vehicule is Map ? vehicule['plate'] as String? ?? '' : '',
      capaciteGrammes: vehicule is Map ? _entier(vehicule['capacityGrams']) : 0,
      placesPassager: vehicule is Map ? _entier(vehicule['passengerCapacity']) : 0,
      marque: vehicule is Map ? vehicule['brand'] as String? ?? '' : '',
      modele: vehicule is Map ? vehicule['model'] as String? ?? '' : '',
      couleur: vehicule is Map ? vehicule['color'] as String? ?? '' : '',
      capacites: _mots(json['capabilities']),
      capacitesPossibles: _mots(json['allowedCapabilities']),
      photoProfil: json['profilePhotoUrl'] as String? ?? '',
      peutSoumettre: json['canSubmit'] as bool? ?? false,
      vehiculeDeclare: json['vehicleDeclared'] as bool? ?? false,
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
    int? placesPassager,
    String? marque,
    String? modele,
    String? couleur,
  }) =>
      _api.put('/vehicle', body: {
        'type': type,
        'plate': plaque,
        if (capaciteGrammes != null) 'capacityGrams': capaciteGrammes,

        // LA DECLARATION REMPLACE LE VEHICULE ENTIER cote domaine : un champ
        // omis n'est pas conserve, il est remis a zero. On renvoie donc ce
        // qu'on a lu pour les champs qu'aucun ecran ne saisit encore.
        if (placesPassager != null) 'passengerCapacity': placesPassager,
        if (marque != null) 'brand': marque,
        if (modele != null) 'model': modele,
        if (couleur != null) 'color': couleur,
      });

  /// Declare les activites du livreur.
  ///
  /// L'ENSEMBLE COMPLET, PAS UN AJOUT : ce qui n'est pas dans la liste est
  /// retire. Une liste vide ramene le livreur au seul colis, qui est le socle.
  ///
  /// LE SERVICE REFUSE SUR UN DOSSIER VALIDE (APPLICATION_NOT_EDITABLE), comme
  /// pour les pieces et le vehicule : un dossier valide se fige, et sa
  /// correction passe par ops.
  Future<void> declarerHabilitations(List<String> capacites) =>
      _api.put('/capabilities', body: {'capabilities': capacites});

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
      // UNE FORMDATA NEUVE A CHAQUE TENTATIVE. Elle etait construite ici, une
      // fois : le rejeu qui suit un rafraichissement de jeton la renvoyait
      // finalisee et echouait en « erreur inattendue ». C'est le cas le plus
      // courant du dossier — on prend ses photos tranquillement, et le jeton
      // expire avant qu'on appuie.
      formulaire: () async => FormData.fromMap({
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
      formulaire: () async => FormData.fromMap({
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
