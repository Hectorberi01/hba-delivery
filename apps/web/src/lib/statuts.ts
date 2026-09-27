/**
 * Les enumerations protobuf arrivent en ENTIERS.
 *
 * La passerelle serialise les messages protobuf avec System.Text.Json, qui
 * rend un enum C# sous forme de nombre. Les valeurs ci-dessous sont donc
 * celles du .proto, pas des chaines : delivery_service.proto fait foi.
 */
export type CleStatut =
  | 'inconnu'
  | 'attente_paiement'
  | 'paiement_echoue'
  | 'payee'
  | 'recherche_livreur'
  | 'sans_livreur'
  | 'livreur_affecte'
  | 'au_point_de_collecte'
  | 'collectee'
  | 'livree'
  | 'annulee'
  | 'echouee';

const PAR_NUMERO: Record<number, CleStatut> = {
  0: 'inconnu',
  1: 'attente_paiement',
  2: 'paiement_echoue',
  3: 'payee',
  4: 'recherche_livreur',
  5: 'sans_livreur',
  6: 'livreur_affecte',
  7: 'au_point_de_collecte',
  8: 'collectee',
  9: 'livree',
  10: 'annulee',
  11: 'echouee',
};

const PAR_NOM: Record<string, CleStatut> = {
  DELIVERY_STATUS_UNSPECIFIED: 'inconnu',
  DELIVERY_STATUS_PENDING_PAYMENT: 'attente_paiement',
  DELIVERY_STATUS_PAYMENT_FAILED: 'paiement_echoue',
  DELIVERY_STATUS_PAID: 'payee',
  DELIVERY_STATUS_SEARCHING_DRIVER: 'recherche_livreur',
  DELIVERY_STATUS_NO_DRIVER_FOUND: 'sans_livreur',
  DELIVERY_STATUS_DRIVER_ASSIGNED: 'livreur_affecte',
  DELIVERY_STATUS_DRIVER_AT_PICKUP: 'au_point_de_collecte',
  DELIVERY_STATUS_PICKED_UP: 'collectee',
  DELIVERY_STATUS_DELIVERED: 'livree',
  DELIVERY_STATUS_CANCELLED: 'annulee',
  DELIVERY_STATUS_FAILED: 'echouee',
};

/** Accepte les deux formes : l'entier d'aujourd'hui, le nom si la passerelle change d'avis. */
export function lireStatut(brut: unknown): CleStatut {
  if (typeof brut === 'number') return PAR_NUMERO[brut] ?? 'inconnu';
  if (typeof brut === 'string') {
    if (PAR_NOM[brut]) return PAR_NOM[brut];
    const numerique = Number(brut);
    if (Number.isInteger(numerique)) return PAR_NUMERO[numerique] ?? 'inconnu';
  }
  return 'inconnu';
}

export type TonStatut = 'bon' | 'attention' | 'serieux' | 'critique' | 'encours' | 'neutre';

type Definition = { libelle: string; ton: TonStatut; ouverte: boolean };

export const STATUTS: Record<CleStatut, Definition> = {
  inconnu: { libelle: 'Inconnu', ton: 'neutre', ouverte: false },
  attente_paiement: { libelle: 'Attente de paiement', ton: 'attention', ouverte: true },
  paiement_echoue: { libelle: 'Paiement échoué', ton: 'critique', ouverte: false },
  payee: { libelle: 'Payée', ton: 'encours', ouverte: true },
  recherche_livreur: { libelle: 'Recherche livreur', ton: 'encours', ouverte: true },
  sans_livreur: { libelle: 'Aucun livreur trouvé', ton: 'serieux', ouverte: false },
  livreur_affecte: { libelle: 'Livreur affecté', ton: 'encours', ouverte: true },
  au_point_de_collecte: { libelle: 'Au point de collecte', ton: 'encours', ouverte: true },
  collectee: { libelle: 'Colis collecté', ton: 'encours', ouverte: true },
  livree: { libelle: 'Livrée', ton: 'bon', ouverte: false },
  annulee: { libelle: 'Annulée', ton: 'critique', ouverte: false },
  echouee: { libelle: 'Échouée', ton: 'critique', ouverte: false },
};

/** Couleur du ton. Elle ne porte jamais le sens seule : chaque pastille est accompagnee de son libelle. */
export const COULEUR_TON: Record<TonStatut, string> = {
  bon: 'var(--color-bon)',
  attention: 'var(--color-attention)',
  serieux: 'var(--color-serieux)',
  critique: 'var(--color-critique)',
  encours: 'var(--color-serie-1)',
  neutre: 'var(--color-encre-3)',
};

/* --- Livreurs. Memes valeurs que driver_service.proto, memes entiers. --- */

export type TonLivreur = 'bon' | 'attention' | 'critique' | 'encours' | 'neutre';

type DefinitionLivreur = { libelle: string; ton: TonLivreur };

const VERIFICATION: Record<number, DefinitionLivreur> = {
  0: { libelle: 'Statut inconnu', ton: 'neutre' },
  1: { libelle: 'En attente de KYC', ton: 'attention' },
  2: { libelle: 'Dossier validé', ton: 'bon' },
  3: { libelle: 'Dossier rejeté', ton: 'critique' },
  4: { libelle: 'Suspendu', ton: 'critique' },
};

const OPERATIONNEL: Record<number, DefinitionLivreur> = {
  0: { libelle: 'Statut inconnu', ton: 'neutre' },
  1: { libelle: 'Hors ligne', ton: 'neutre' },
  2: { libelle: 'Disponible', ton: 'bon' },
  3: { libelle: 'Offre en cours', ton: 'encours' },
  4: { libelle: 'En mission', ton: 'encours' },
};

/**
 * Les noms du contrat, pour les routes qui les rendent.
 *
 * LA PASSERELLE NE PARLE PAS D'UNE SEULE VOIX, et c'est un etat de fait, pas
 * un choix : les routes anciennes relaient le message protobuf tel quel — donc
 * des entiers — et les nouvelles mettent en forme. Lire les deux ici coute
 * deux tables et evite de faire dependre chaque ecran de l'age de sa route.
 */
const VERIFICATION_PAR_NOM: Record<string, number> = {
  VERIFICATION_STATUS_UNSPECIFIED: 0,
  VERIFICATION_STATUS_PENDING_VERIFICATION: 1,
  VERIFICATION_STATUS_VERIFIED: 2,
  VERIFICATION_STATUS_REJECTED: 3,
  VERIFICATION_STATUS_SUSPENDED: 4,
};

const OPERATIONNEL_PAR_NOM: Record<string, number> = {
  OPERATIONAL_STATUS_UNSPECIFIED: 0,
  OPERATIONAL_STATUS_OFFLINE: 1,
  OPERATIONAL_STATUS_AVAILABLE: 2,
  OPERATIONAL_STATUS_RESERVED: 3,
  OPERATIONAL_STATUS_ON_MISSION: 4,
};

function lireNumero(brut: unknown, parNom: Record<string, number> = {}): number {
  if (typeof brut === 'number') return brut;
  if (typeof brut === 'string') {
    const parle = parNom[brut];
    if (parle !== undefined) return parle;

    const n = Number(brut);
    if (Number.isInteger(n)) return n;
  }
  return 0;
}

export function lireVerification(brut: unknown): DefinitionLivreur {
  return VERIFICATION[lireNumero(brut, VERIFICATION_PAR_NOM)] ?? VERIFICATION[0];
}

export function lireOperationnel(brut: unknown): DefinitionLivreur {
  return OPERATIONNEL[lireNumero(brut, OPERATIONNEL_PAR_NOM)] ?? OPERATIONNEL[0];
}

export function estEnService(brut: unknown): boolean {
  const n = lireNumero(brut, OPERATIONNEL_PAR_NOM);
  return n === 2 || n === 3 || n === 4;
}

export function estDisponible(brut: unknown): boolean {
  return lireNumero(brut, OPERATIONNEL_PAR_NOM) === 2;
}

export function estEnAttenteDeKyc(brut: unknown): boolean {
  return lireNumero(brut, VERIFICATION_PAR_NOM) === 1;
}

export const COULEUR_TON_LIVREUR: Record<TonLivreur, string> = {
  bon: 'var(--color-bon)',
  attention: 'var(--color-attention)',
  critique: 'var(--color-critique)',
  encours: 'var(--color-serie-1)',
  neutre: 'var(--color-encre-3)',
};
