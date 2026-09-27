/// Modeles du compte du livreur.
///
/// CES ROUTES-LA SONT MISES EN FORME A LA MAIN PAR LA PASSERELLE, contrairement
/// aux courses et aux offres qui arrivent en protobuf serialise. Les dates sont
/// donc des chaines ISO et les etats des NOMS de valeur du contrat, pas des
/// entiers. Les lectures ci-dessous acceptent quand meme les deux formes : le
/// contrat de serialisation n'est pas tranche a l'echelle de la passerelle, et
/// une lecture qui ne sait lire qu'une forme casse en silence le jour ou elle
/// change d'avis.
library;

int _entier(Object? valeur) => switch (valeur) {
      int v => v,
      num v => v.toInt(),
      String v => int.tryParse(v) ?? 0,
      _ => 0,
    };

DateTime? _instant(Object? valeur) {
  if (valeur is String) {
    return valeur.isEmpty ? null : DateTime.tryParse(valeur)?.toLocal();
  }

  if (valeur is Map) {
    final secondes = (valeur['seconds'] as num?)?.toInt() ??
        int.tryParse('${valeur['seconds']}');
    if (secondes == null) return null;

    return DateTime.fromMillisecondsSinceEpoch(secondes * 1000, isUtc: true)
        .toLocal();
  }

  return null;
}

String _texte(Object? valeur) => valeur is String ? valeur : '';

/// Sens d'un mouvement.
///
/// LE SIGNE N'EST PAS DANS LE MONTANT, cote serveur comme ici : un montant
/// negatif se glisse dans une somme, dans un affichage, dans une comparaison,
/// et l'erreur se decouvre au moment de payer quelqu'un.
///
/// LA LECTURE SUIT L'IDIOME DES COURSES : un nom de valeur du contrat, ou un
/// entier, ou un entier rendu en chaine. Un etat inconnu ne leve rien, il
/// retombe sur « inconnu » — et l'ecran doit donc rester lisible dans ce cas.
enum SensMouvement {
  inconnu,
  entree,
  sortie;

  static const _parNom = {
    'LEDGER_DIRECTION_CREDIT': entree,
    'LEDGER_DIRECTION_DEBIT': sortie,
  };

  static const _parIndice = {1: entree, 2: sortie};

  static SensMouvement parse(Object? brut) => switch (brut) {
        int v => _parIndice[v] ?? inconnu,
        String v => _parNom[v] ?? _parIndice[int.tryParse(v) ?? -1] ?? inconnu,
        _ => inconnu,
      };
}

enum NatureMouvement {
  inconnue,
  course,
  versement;

  static const _parNom = {
    'LEDGER_ENTRY_KIND_DELIVERY_EARNING': course,
    'LEDGER_ENTRY_KIND_PAYOUT': versement,
  };

  static const _parIndice = {1: course, 2: versement};

  static NatureMouvement parse(Object? brut) => switch (brut) {
        int v => _parIndice[v] ?? inconnue,
        String v => _parNom[v] ?? _parIndice[int.tryParse(v) ?? -1] ?? inconnue,
        _ => inconnue,
      };
}

class Mouvement {
  const Mouvement({
    required this.id,
    required this.nature,
    required this.sens,
    required this.montantXof,
    required this.referenceCourse,
    required this.versementId,
    required this.survenuLe,
  });

  factory Mouvement.fromJson(Map<String, dynamic> json) => Mouvement(
        id: _texte(json['id']),
        nature: NatureMouvement.parse(json['kind']),
        sens: SensMouvement.parse(json['direction']),
        montantXof: _entier(json['amountXof']),
        referenceCourse: _texte(json['deliveryReference']),
        versementId: _texte(json['payoutId']),

        // L'INSTANT DU FAIT, PAS DE L'ECRITURE. Un rattrapage d'evenements
        // ecrit aujourd'hui des lignes datees d'hier, et c'est hier qui doit
        // s'afficher.
        survenuLe: _instant(json['occurredAt']),
      );

  final String id;
  final NatureMouvement nature;
  final SensMouvement sens;

  /// Toujours positif. Le sens est porte par [sens].
  final int montantXof;

  final String referenceCourse;

  /// L'origine d'une sortie d'argent. Vide pour une entree.
  final String versementId;

  final DateTime? survenuLe;

  bool get estUneEntree => sens == SensMouvement.entree;

  /// Vrai quand le serveur a rendu un sens que cette version ne connait pas.
  /// L'ECRAN DOIT LE DIRE PLUTOT QUE DE CHOISIR : afficher « − 5 000 F » sur
  /// une ligne dont on ignore le sens ferait croire a une sortie d'argent.
  bool get sensIndetermine => sens == SensMouvement.inconnu;
}

/// Le compte du livreur.
///
/// LES TROIS CUMULS PORTENT SUR TOUT LE COMPTE, pas sur les lignes rendues, et
/// c'est ce qui fait de [restantDuXof] une dette et non plus une addition de
/// page. C'est le serveur qui les calcule : refaire la soustraction ici
/// donnerait un second chiffre qui finirait par ne pas tomber d'accord avec le
/// premier, un jour, devant un livreur.
class Releve {
  const Releve({
    required this.gagneXof,
    required this.verseXof,
    required this.restantDuXof,
    required this.mouvements,
    required this.totalMouvements,
  });

  factory Releve.fromJson(Map<String, dynamic> json) {
    final lignes = json['entries'];

    return Releve(
      gagneXof: _entier(json['earnedXof']),
      verseXof: _entier(json['paidOutXof']),
      restantDuXof: _entier(json['dueXof']),
      mouvements: lignes is List
          ? [
              for (final ligne in lignes)
                if (ligne is Map<String, dynamic>) Mouvement.fromJson(ligne),
            ]
          : const <Mouvement>[],
      totalMouvements: _entier(json['totalEntries']),
    );
  }

  final int gagneXof;
  final int verseXof;

  /// gagne - verse. PEUT ETRE NEGATIF, et l'ecran ne le cache pas : cela
  /// voudrait dire qu'on a verse plus que du, et le ramener a zero ferait
  /// disparaitre le seul signal qui permet de le voir.
  final int restantDuXof;

  final List<Mouvement> mouvements;

  /// Nombre de lignes au compte, pour dire ce que le plafond a coupe.
  final int totalMouvements;
}

enum EtatVersement {
  inconnu,
  demandee,
  approuvee,
  versee,
  refusee;

  static const _parNom = {
    'PAYOUT_STATUS_REQUESTED': demandee,
    'PAYOUT_STATUS_APPROVED': approuvee,
    'PAYOUT_STATUS_PAID': versee,
    'PAYOUT_STATUS_REJECTED': refusee,
  };

  static const _parIndice = {1: demandee, 2: approuvee, 3: versee, 4: refusee};

  static EtatVersement parse(Object? brut) => switch (brut) {
        int v => _parIndice[v] ?? inconnu,
        String v => _parNom[v] ?? _parIndice[int.tryParse(v) ?? -1] ?? inconnu,
        _ => inconnu,
      };
}

/// Une demande de versement.
class DemandeVersement {
  const DemandeVersement({
    required this.id,
    required this.montantXof,
    required this.etat,
    required this.demandeeLe,
    required this.decideeLe,
    required this.motifDeRefus,
    required this.verseeLe,
    required this.referenceDuVirement,
  });

  factory DemandeVersement.fromJson(Map<String, dynamic> json) => DemandeVersement(
        id: _texte(json['id']),
        montantXof: _entier(json['amountXof']),
        etat: EtatVersement.parse(json['status']),
        demandeeLe: _instant(json['requestedAt']),
        decideeLe: _instant(json['decidedAt']),
        motifDeRefus: _texte(json['rejectionReason']),
        verseeLe: _instant(json['paidAt']),
        referenceDuVirement: _texte(json['paymentReference']),
      );

  final String id;
  final int montantXof;
  final EtatVersement etat;
  final DateTime? demandeeLe;
  final DateTime? decideeLe;
  final String motifDeRefus;
  final DateTime? verseeLe;
  final String referenceDuVirement;

  /// La demande attend encore une decision ou un virement.
  ///
  /// APPROUVEE COMPTE COMME EN COURS, et c'est la distinction qui compte pour
  /// le livreur : l'accord ne veut pas dire que l'argent est parti. Tant que
  /// c'est vrai, il ne peut pas en ouvrir une autre.
  bool get enCours =>
      etat == EtatVersement.demandee || etat == EtatVersement.approuvee;
}
