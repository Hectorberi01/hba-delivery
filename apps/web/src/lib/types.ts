/** Formes rendues par la passerelle. Elles suivent les .proto, en camelCase. */

/**
 * Un instant rendu par la passerelle.
 *
 * DEUX FORMES CIRCULENT, ET LA SECONDE EST CELLE QU'ON RECOIT LE PLUS SOUVENT.
 * Un google.protobuf.Timestamp serialise en JSON PROTOBUF est une chaine ISO ;
 * serialise par System.Text.Json — ce que fait la passerelle sur les messages
 * qu'elle relaie tels quels — c'est un objet { seconds, nanos }. Seule la route
 * des positions met en forme a la main, et rend une chaine.
 *
 * DECLARER « string » ETAIT UN MENSONGE SILENCIEUX : new Date({seconds}) rend
 * une date invalide, qui ne leve rien. Elle s'affiche « — », et emporte avec
 * elle tous les compteurs qui en dependent.
 */
export type InstantApi =
  | string
  | { seconds?: number | string; nanos?: number | string }
  | null;

export type PointGeo = { latitude?: number; longitude?: number };

/**
 * hba.common.v1.Location. LES COORDONNEES SONT DANS « point », pas a la
 * racine : le .proto imbrique un GeoPoint, et une version anterieure de ce
 * type les declarait a plat — elles seraient toujours arrivees indefinies,
 * sans que rien ne le signale, parce qu'un champ optionnel manquant ne fait
 * pas d'erreur.
 */
export type Lieu = {
  point?: PointGeo;
  landmark?: string;
  phone?: string;
  contactName?: string;
  notes?: string;
};

export type Montant = { amount?: number | string; currency?: string };

export type Tarification = {
  total?: Montant;
  base?: Montant;
  distanceMeters?: number;
  durationSeconds?: number;
};

export type LivreurAffecte = {
  driverId?: string;
  displayName?: string;
  phone?: string;
  vehicleType?: number | string;
  vehiclePlate?: string;
};

export type Course = {
  id: string;
  reference?: string;
  status?: number | string;
  source?: number | string;
  customerId?: string;
  merchantId?: string;
  partnerId?: string;
  pickup?: Lieu;
  dropoff?: Lieu;
  recipientName?: string;
  recipientPhone?: string;
  pricing?: Tarification;
  driver?: LivreurAffecte;
  packageDescription?: string;
  packageWeightGrams?: number;
  createdAt?: InstantApi;
  paidAt?: InstantApi;
  assignedAt?: InstantApi;
  pickedUpAt?: InstantApi;
  completedAt?: InstantApi;
  closureReason?: string;
};

export type PageDeCourses = {
  deliveries?: Course[];
  nextPageToken?: string;
};

export type Livreur = {
  id: string;
  displayName?: string;
  phone?: string;
  vehicle?: { type?: number | string; plate?: string; capacityGrams?: number };
  verificationStatus?: number | string;
  operationalStatus?: number | string;
  registeredAt?: InstantApi;
  verifiedAt?: InstantApi;
  statusReason?: string;
};

/**
 * Position courante d'un livreur.
 *
 * Cette route est la seule du back-office a rendre des NOMS d'enumeration et
 * une date ISO : la passerelle y met en forme au lieu de relayer le message
 * protobuf brut. Les autres rendent encore des entiers et des
 * { seconds, nanos }.
 */
export type PositionLivreur = {
  driverId: string;
  displayName?: string;
  latitude: number;
  longitude: number;
  // ISO, et non { seconds, nanos } : c'est la seule route ou la passerelle
  // met en forme a la main plutot que de relayer le message protobuf.
  seenAt?: string;
  operationalStatus?: number | string;
  vehicleType?: number | string;
};

export type PagePositions = {
  positions?: PositionLivreur[];
  /** Fenetre qui definit la liste : sans elle, le compte ne veut rien dire. */
  freshnessSeconds?: number;
};

/** Une ligne de l'annuaire client. Le telephone y est toujours masque. */
export type LigneAnnuaireClient = {
  customerId: string;
  displayName?: string;
  phoneMasked?: string;
  createdAt?: InstantApi;
};

export type PageAnnuaireClients = {
  customers?: LigneAnnuaireClient[];
  total?: number;
};

export type AdresseFavorite = {
  id: string;
  label?: string;
  isDefault?: boolean;
  landmark?: string;
  contactName?: string;
  phone?: string;
  notes?: string | null;
};

/**
 * La fiche, telle que le role de l'appelant permet de la voir.
 *
 * emailHidden et addressesHidden ne sont pas du confort : sans eux, une fiche
 * sans adresse se lirait comme un client qui n'en a enregistre aucune.
 */
export type FicheClient = {
  customerId: string;
  displayName?: string;
  phone?: string;
  email?: string | null;
  emailHidden?: boolean;
  addressesHidden?: boolean;
  createdAt?: InstantApi;
  favoriteAddresses?: AdresseFavorite[];
};

/** Une trace d'acces au dossier d'un client. */
export type AccesClient = {
  readerId: string;
  readerRoles?: string;
  readAt?: InstantApi;
};

export type JournalAcces = { entries?: AccesClient[] };

export type PageDeLivreurs = {
  drivers?: Livreur[];
  total?: number;
};

/* --- Indicateurs. Les enums arrivent en entiers, les int64 en nombres. --- */

export type Nombre = number | string;

export type PointSerie = { key: string; value?: Nombre };

export type FenetreApi = { from?: InstantApi; to?: InstantApi };

export type StatsCourses = {
  window?: FenetreApi;
  total?: Nombre;
  open?: Nombre;
  delivered?: Nombre;
  closed?: Nombre;
  billedXof?: Nombre;
  byStatus?: { status?: number | string; count?: Nombre }[];
  bySource?: { source?: number | string; count?: Nombre }[];
  createdSeries?: PointSerie[];
  avgSecondsToAssignment?: Nombre;
  avgSecondsToPickup?: Nombre;
  avgSecondsToDelivery?: Nombre;
  assignmentSamples?: Nombre;
  pickupSamples?: Nombre;
  deliverySamples?: Nombre;
};

export type StatsPaiements = {
  window?: FenetreApi;
  createdCount?: Nombre;
  createdAmountXof?: Nombre;
  createdByStatus?: { status?: number | string; count?: Nombre; amountXof?: Nombre }[];
  createdByMethod?: { method?: number | string; count?: Nombre; amountXof?: Nombre }[];
  collectedCount?: Nombre;
  collectedXof?: Nombre;
  collectedSeries?: PointSerie[];
  avgSecondsToPayment?: Nombre;
  paymentSamples?: Nombre;
};

export type StatsDispatch = {
  window?: FenetreApi;
  dispatches?: Nombre;
  assigned?: Nombre;
  exhausted?: Nombre;
  cancelled?: Nombre;
  stillSearching?: Nombre;
  offersSent?: Nombre;
  offersByStatus?: { status?: number | string; count?: Nombre }[];
  acceptedByWave?: { waveNumber?: number; count?: Nombre }[];
  avgSecondsToAssignment?: Nombre;
  assignmentSamples?: Nombre;
  avgSecondsToOfferResponse?: Nombre;
  offerResponseSamples?: Nombre;
};

export type StatsLivreurs = {
  window?: FenetreApi;
  total?: Nombre;
  byVerification?: { status?: number | string; count?: Nombre }[];
  byOperational?: { status?: number | string; count?: Nombre }[];
  registeredInWindow?: Nombre;
  verifiedInWindow?: Nombre;
};

export type Indicateurs = {
  window?: FenetreApi;
  granularity?: string;
  deliveries?: StatsCourses | null;
  payments?: StatsPaiements | null;
  dispatch?: StatsDispatch | null;
  drivers?: StatsLivreurs | null;
  unavailable?: string[];
};

/**
 * Dossier d'un livreur, tel que GET /api/admin/v1/drivers/{id}/application le
 * rend.
 *
 * LES ENUMS Y SONT DES NOMS, PAS DES ENTIERS, contrairement au reste de la
 * passerelle : ces routes sont neuves et n'heritent pas du piege.
 */
export type PieceDuDossier = {
  type?: string;
  uploadedAt?: InstantApi;
  sizeBytes?: Nombre;
  contentType?: string;
  /** URL signee, valable quelques minutes seulement. */
  readUrl?: string;
  readUrlExpiresAt?: string;
};

export type DossierLivreur = {
  driverId?: string;
  verificationStatus?: string;
  statusReason?: string;
  submittedAt?: InstantApi;
  canSubmit?: boolean;
  profilePhotoUrl?: string;
  vehicle?: { type?: string; plate?: string; capacityGrams?: number } | null;
  documents?: PieceDuDossier[];
  missingDocuments?: string[];
};

/**
 * Une demande de versement, telle que le back-office la lit.
 *
 * LES DATES SONT DES CHAINES ISO ICI, comme pour les positions : la passerelle
 * met en forme cette route a la main au lieu de relayer le message protobuf.
 * Le type passe quand meme par InstantApi, parce que c'est ce que `dateHeure`
 * attend et qu'une exception de plus dans les formats finirait par se payer.
 */
export type DemandeVersement = {
  id: string;
  driverId: string;
  amountXof?: Nombre;
  /** PAYOUT_STATUS_REQUESTED | _APPROVED | _PAID | _REJECTED */
  status?: string;
  requestedAt?: InstantApi;
  decidedAt?: InstantApi;
  /** Le sujet du jeton de qui a decide. Absent tant que rien n'est decide. */
  decidedBy?: string | null;
  rejectionReason?: string | null;
  paidAt?: InstantApi;
  paymentReference?: string | null;
};

export type FileDesVersements = { payouts?: DemandeVersement[] };

/** Une ligne du grand livre d'un livreur. */
export type MouvementLivreur = {
  id: string;
  /** LEDGER_ENTRY_KIND_DELIVERY_EARNING | _PAYOUT */
  kind?: string;
  /** LEDGER_DIRECTION_CREDIT | _DEBIT */
  direction?: string;
  amountXof?: Nombre;
  deliveryId?: string | null;
  deliveryReference?: string | null;
  payoutId?: string | null;
  occurredAt?: InstantApi;
};

/**
 * Le compte d'un livreur.
 *
 * LES TROIS CUMULS PORTENT SUR TOUT LE COMPTE, pas sur les lignes rendues :
 * c'est le service qui les calcule, et refaire la soustraction ici ferait un
 * second chiffre qui finirait par ne pas tomber d'accord avec le premier.
 */
export type ReleveLivreur = {
  driverId?: string;
  earnedXof?: Nombre;
  paidOutXof?: Nombre;
  dueXof?: Nombre;
  totalEntries?: Nombre;
  entries?: MouvementLivreur[];
};
