import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:hba_core/hba_core.dart';

import '../../core/providers.dart';

/// Etat de verification du dossier, tel que le service Driver le nomme.
enum EtatDossier {
  inconnu,
  enAttente,
  valide,
  rejete,
  suspendu;

  /// La passerelle renvoie le nom C# de l'enum protobuf. Un entier reste
  /// accepte : le contrat de serialisation n'est pas tranche.
  static EtatDossier lire(Object? brut) => switch (brut) {
        'PendingVerification' || 1 || '1' => enAttente,
        'Verified' || 2 || '2' => valide,
        'Rejected' || 3 || '3' => rejete,
        'Suspended' || 4 || '4' => suspendu,
        _ => inconnu,
      };

  String get libelle => switch (this) {
        enAttente => 'Dossier en cours',
        valide => 'Dossier valide',
        rejete => 'Dossier rejeté',
        suspendu => 'Compte suspendu',
        inconnu => 'Statut inconnu',
      };
}

/// Etat operationnel, tel que le service Driver le nomme.
enum EtatOperationnel {
  inconnu,
  horsLigne,
  disponible,
  offreEnCours,
  enMission;

  static EtatOperationnel lire(Object? brut) => switch (brut) {
        'Offline' || 1 || '1' => horsLigne,
        'Available' || 2 || '2' => disponible,
        'Reserved' || 3 || '3' => offreEnCours,
        'OnMission' || 4 || '4' => enMission,
        _ => inconnu,
      };

  String get libelle => switch (this) {
        horsLigne => 'Hors ligne',
        disponible => 'Disponible',
        offreEnCours => 'Offre en cours',
        enMission => 'En mission',
        inconnu => 'Inconnu',
      };
}

/// Vehicule declare.
///
/// IL NAIT VIDE ET IL SE DECLARE. Le profil est cree a
/// VEHICLE_TYPE_UNSPECIFIED — le compte precede le dossier —, puis
/// « PUT /vehicle » le renseigne depuis l'ecran Dossier. Il se fige ensuite :
/// le domaine refuse toute modification une fois le dossier valide, pour que
/// la plaque enregistree reste celle qu'ops a comparee a la carte grise.
///
/// CE COMMENTAIRE A ETE FAUX PENDANT UN TEMPS, et il affirmait exactement le
/// contraire : « aucune route ne permet de le renseigner ». La route est
/// arrivee, la phrase est restee. Un commentaire faux coute plus cher qu'un
/// commentaire absent — celui-ci a survecu a l'ecran qui l'a dementi.
class Vehicule {
  const Vehicule({required this.type, required this.plaque});

  final String type;
  final String plaque;

  bool get estRenseigne => type != 'Unspecified' && type.isNotEmpty;

  String get libelle => switch (type) {
        'Motorcycle' || '1' => 'Moto',
        'Car' || '2' => 'Voiture',
        'Van' || '3' => 'Camionnette',
        _ => 'Non déclaré',
      };

  static Vehicule? fromJson(Object? json) {
    if (json is! Map) return null;

    return Vehicule(
      type: json['type']?.toString() ?? '',
      plaque: json['plate'] as String? ?? '',
    );
  }
}

/// Ce que le livreur peut voir de lui-meme.
///
/// CET OBJET EST EN LECTURE SEULE, MAIS LE PROFIL NE L'EST PLUS, et la nuance
/// compte. Cette classe traduit « GET /me » et rien d'autre : c'est une vue.
/// Les modifications existent, elles passent simplement ailleurs —
/// « PUT /vehicle » et « POST /documents » par l'ecran Dossier,
/// « POST /profile-photo » par l'ecran Profil. Aucune ne repasse par ici.
///
/// La version precedente de ce commentaire disait que rien ne modifiait le
/// profil, ce qui a cesse d'etre vrai sans que personne ne le corrige.
class ProfilLivreur {
  const ProfilLivreur({
    required this.id,
    required this.displayName,
    required this.phone,
    required this.dossier,
    required this.motif,
    required this.operationnel,
    required this.vehicule,
    required this.inscritLe,
    required this.valideLe,
  });

  final String id;
  final String displayName;
  final String phone;
  final EtatDossier dossier;

  /// Motif d'un rejet ou d'une suspension. C'est la seule chose qui dise au
  /// livreur ce qu'il doit corriger.
  final String motif;

  final EtatOperationnel operationnel;
  final Vehicule? vehicule;
  final DateTime? inscritLe;
  final DateTime? valideLe;

  static DateTime? _date(Object? brut) =>
      brut is String && brut.isNotEmpty ? DateTime.tryParse(brut)?.toLocal() : null;

  static ProfilLivreur fromJson(Map<String, dynamic> json) => ProfilLivreur(
        id: json['id'] as String? ?? '',
        displayName: json['displayName'] as String? ?? '',
        phone: json['phone'] as String? ?? '',
        dossier: EtatDossier.lire(json['verificationStatus']),
        motif: json['statusReason'] as String? ?? '',
        operationnel: EtatOperationnel.lire(json['operationalStatus']),
        vehicule: Vehicule.fromJson(json['vehicle']),
        inscritLe: _date(json['registeredAt']),
        valideLe: _date(json['verifiedAt']),
      );
}

class ProfilRepository {
  ProfilRepository(this._api);

  final ApiClient _api;

  Future<ProfilLivreur> lire() async =>
      ProfilLivreur.fromJson(await _api.get('/me'));
}

final profilRepositoryProvider = Provider<ProfilRepository>(
  (ref) => ProfilRepository(ref.watch(apiClientProvider)),
);

/// Relu a chaque affichage de l'onglet : le dossier peut avoir ete valide
/// pendant que l'application etait ouverte.
final profilProvider = FutureProvider.autoDispose<ProfilLivreur>(
  (ref) => ref.watch(profilRepositoryProvider).lire(),
);
