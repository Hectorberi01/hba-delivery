import { dateDe, instant } from '@/lib/format';
import { lireStatut, STATUTS, type CleStatut } from '@/lib/statuts';
import type { Course } from '@/lib/types';

/**
 * TOUT CE FICHIER CALCULE DANS LE NAVIGATEUR, SUR UNE PAGE DE RESULTATS.
 *
 * Aucune route ne rend d'agregat : « GET /api/admin/v1/deliveries » rend des
 * courses, au plus quelques centaines. Les chiffres ci-dessous portent donc
 * sur cet echantillon, et les ecrans le disent. Le jour ou un service exposera
 * de vrais indicateurs, ces fonctions disparaissent — elles ne doivent surtout
 * pas devenir la source de verite des chiffres de l'entreprise.
 */

export function montant(course: Course): number {
  const brut = course.pricing?.total?.amount;
  const valeur = typeof brut === 'string' ? Number(brut) : brut;
  return Number.isFinite(valeur) ? Number(valeur) : 0;
}

export function repartitionParStatut(courses: Course[]): Map<CleStatut, number> {
  const compte = new Map<CleStatut, number>();
  for (const course of courses) {
    const cle = lireStatut(course.status);
    compte.set(cle, (compte.get(cle) ?? 0) + 1);
  }
  return compte;
}

export function ouvertes(courses: Course[]): Course[] {
  return courses.filter((course) => STATUTS[lireStatut(course.status)].ouverte);
}

export function livrees(courses: Course[]): Course[] {
  return courses.filter((course) => lireStatut(course.status) === 'livree');
}

/** Chiffre encaisse : seules les courses reellement payees comptent. */
export function encaisse(courses: Course[]): number {
  return courses
    .filter((course) => Boolean(course.paidAt))
    .reduce((total, course) => total + montant(course), 0);
}

/** Courses dont la remise a ete confirmee aujourd'hui, heure du poste. */
export function livreesAujourdhui(courses: Course[]): Course[] {
  const debut = new Date();
  debut.setHours(0, 0, 0, 0);

  return courses.filter((course) => {
    if (lireStatut(course.status) !== 'livree') return false;
    const date = dateDe(course.completedAt);
    return date !== null && date >= debut;
  });
}

/**
 * Part de courses livrees parmi les courses CLOSES.
 *
 * LE DENOMINATEUR EXCLUT LES COURSES EN COURS, et ce n'est pas un detail :
 * les compter ferait chuter le taux chaque matin, puis remonter le soir,
 * sans que rien n'ait change dans le service. Rend null tant qu'aucune
 * course n'est close — un taux sur zero course ne veut rien dire.
 */
export function tauxDeSucces(courses: Course[]): number | null {
  // Une course au statut inconnu ne compte ni pour ni contre : elle dirait
  // seulement que la passerelle a renvoye une valeur que nous ne savons pas
  // lire.
  const denombrables = courses.filter((course) => {
    const cle = lireStatut(course.status);
    return cle !== 'inconnu' && !STATUTS[cle].ouverte;
  });

  if (!denombrables.length) return null;

  const reussies = denombrables.filter((course) => lireStatut(course.status) === 'livree').length;
  return reussies / denombrables.length;
}

const SOURCES: Record<number, string> = {
  0: 'Non précisée',
  1: 'HBA Express',
  2: 'HBA Food',
  3: 'API partenaire',
  4: 'App client',
};

export function libelleSource(source: number | string | undefined): string {
  const numero = typeof source === 'string' ? Number(source) : source;
  return SOURCES[Number(numero) || 0] ?? 'Non précisée';
}

const VEHICULES: Record<number, string> = {
  0: 'Véhicule non précisé',
  1: 'Moto',
  2: 'Voiture',
  3: 'Camionnette',
};

/** Les noms du contrat, rendus par les routes qui mettent en forme. */
const VEHICULES_PAR_NOM: Record<string, number> = {
  VEHICLE_TYPE_UNSPECIFIED: 0,
  VEHICLE_TYPE_MOTORCYCLE: 1,
  VEHICLE_TYPE_CAR: 2,
  VEHICLE_TYPE_VAN: 3,
};

export function libelleVehicule(type: number | string | undefined): string {
  if (typeof type === 'string' && VEHICULES_PAR_NOM[type] !== undefined) {
    return VEHICULES[VEHICULES_PAR_NOM[type]] ?? 'Véhicule non précisé';
  }

  const numero = typeof type === 'string' ? Number(type) : type;
  return VEHICULES[Number(numero) || 0] ?? 'Véhicule non précisé';
}

/**
 * Livreurs vus dans une page de courses.
 *
 * CE N'EST PAS UN ANNUAIRE, ET LA NUANCE COMPTE : un livreur inscrit qui n'a
 * jamais ete affecte n'apparait nulle part ici, et le statut operationnel —
 * en ligne, reserve, en mission — vit dans le service Driver, qui n'expose
 * aucune liste. Ce que Delivery porte, en revanche, est reel : l'instantane
 * du livreur est fige dans chaque course au moment de l'affectation.
 */
export type LivreurVu = {
  identifiant: string;
  nom: string;
  telephone: string;
  vehicule: string;
  plaque: string;
  courses: Course[];
  enCours: number;
  livrees: number;
  closes: number;
  /** En millisecondes depuis l'epoque : c'est ce qui se compare et s'affiche. */
  derniereActivite?: number;
};

export function livreursVus(courses: Course[]): LivreurVu[] {
  const parLivreur = new Map<string, LivreurVu>();

  for (const course of courses) {
    const identifiant = course.driver?.driverId;
    if (!identifiant) continue;

    const vu = parLivreur.get(identifiant) ?? {
      identifiant,
      nom: course.driver?.displayName || 'Sans nom',
      telephone: course.driver?.phone || '',
      vehicule: libelleVehicule(course.driver?.vehicleType),
      plaque: course.driver?.vehiclePlate || '',
      courses: [],
      enCours: 0,
      livrees: 0,
      closes: 0,
    };

    vu.courses.push(course);

    const cle = lireStatut(course.status);
    if (STATUTS[cle].ouverte) vu.enCours += 1;
    else if (cle !== 'inconnu') {
      vu.closes += 1;
      if (cle === 'livree') vu.livrees += 1;
    }

    // COMPARER LES INSTANTS, PAS LES VALEURS BRUTES. Tant que les dates
    // etaient declarees « string », ce test comparait deux chaines ISO et
    // marchait par accident ; sur un objet { seconds, nanos } il comparait
    // deux objets, donc jamais rien.
    const date = instant(course.assignedAt ?? course.createdAt);
    if (date !== null && (vu.derniereActivite === undefined || date > vu.derniereActivite)) {
      vu.derniereActivite = date;
    }

    parLivreur.set(identifiant, vu);
  }

  return [...parLivreur.values()].sort((a, b) => b.courses.length - a.courses.length);
}

export function repartitionParSource(courses: Course[]): Map<string, number> {
  const compte = new Map<string, number>();
  for (const course of courses) {
    const cle = libelleSource(course.source);
    compte.set(cle, (compte.get(cle) ?? 0) + 1);
  }
  return compte;
}

/** Nombre de courses creees par jour, sur les N derniers jours, aujourd'hui inclus. */
export function parJour(courses: Course[], jours: number): { libelle: string; valeur: number }[] {
  const compte = new Map<string, number>();

  for (const course of courses) {
    const date = dateDe(course.createdAt);
    if (date === null) continue;
    const cle = date.toISOString().slice(0, 10);
    compte.set(cle, (compte.get(cle) ?? 0) + 1);
  }

  const resultat: { libelle: string; valeur: number }[] = [];

  for (let recul = jours - 1; recul >= 0; recul -= 1) {
    const jour = new Date();
    jour.setDate(jour.getDate() - recul);
    const cle = jour.toISOString().slice(0, 10);
    resultat.push({
      libelle: jour.toLocaleDateString('fr-FR', { day: '2-digit', month: '2-digit' }),
      valeur: compte.get(cle) ?? 0,
    });
  }

  return resultat;
}
