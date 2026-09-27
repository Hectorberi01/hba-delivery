import type { InstantApi } from '@/lib/types';

/**
 * Un instant de l'API, en millisecondes depuis l'epoque. Null s'il manque ou
 * s'il est illisible.
 *
 * LIRE LES DEUX FORMES N'EST PAS DE LA COMPLAISANCE. Un Timestamp protobuf
 * serialise par System.Text.Json est un objet { seconds, nanos } ; en JSON
 * protobuf, c'est une chaine ISO. La passerelle rend aujourd'hui la premiere
 * forme sur tout ce qu'elle relaie tel quel. « new Date({seconds: 1790…}) »
 * ne leve rien : il rend une date invalide, qui s'affiche « — » — et qui fait
 * silencieusement tomber a zero les compteurs bases dessus.
 *
 * LES NANOSECONDES SONT TRONQUEES A LA MILLISECONDE, ce que JavaScript sait
 * representer. Personne n'affiche mieux que la minute de toute facon.
 */
export function instant(valeur: InstantApi | number | undefined): number | null {
  if (valeur === null || valeur === undefined) return null;

  // UN NOMBRE NU VIENT DE NOUS, PAS DE L'API : c'est le resultat d'un
  // instant() precedent, donc deja en millisecondes. InstantApi ne comprend
  // volontairement pas « number », pour qu'aucune date du contrat ne puisse
  // arriver ici sous une forme dont on ignorerait l'unite.
  if (typeof valeur === 'number') return Number.isFinite(valeur) ? valeur : null;

  if (typeof valeur === 'string') {
    if (!valeur) return null;
    const ms = new Date(valeur).getTime();
    return Number.isNaN(ms) ? null : ms;
  }

  if (typeof valeur !== 'object') return null;

  const secondes = Number(valeur.seconds ?? Number.NaN);
  if (!Number.isFinite(secondes)) return null;

  const nanos = Number(valeur.nanos ?? 0);
  return secondes * 1000 + (Number.isFinite(nanos) ? Math.floor(nanos / 1_000_000) : 0);
}

/** Le meme instant, en Date. Pratique pour les comparaisons de calendrier. */
export function dateDe(valeur: InstantApi | number | undefined): Date | null {
  const ms = instant(valeur);
  return ms === null ? null : new Date(ms);
}

/** Montants en ENTIERS de francs CFA (ADR 0006) : ni centimes, ni flottants. */
export function xof(montant: number | string | null | undefined): string {
  const valeur = Number(montant ?? 0);
  if (!Number.isFinite(valeur)) return '—';
  return `${valeur.toLocaleString('fr-FR')} F`;
}

export function nombre(valeur: number | string | null | undefined): string {
  const n = Number(valeur ?? 0);
  return Number.isFinite(n) ? n.toLocaleString('fr-FR') : '—';
}

export function pourcentage(part: number, total: number): string {
  if (!total) return '0 %';
  return `${Math.round((part / total) * 100)} %`;
}

export function dateHeure(valeur: InstantApi | number | undefined): string {
  const d = dateDe(valeur);
  if (d === null) return '—';
  return d.toLocaleString('fr-FR', {
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  });
}

export function heure(valeur: InstantApi | number | undefined): string {
  const d = dateDe(valeur);
  return d === null ? '—' : d.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
}

/** « il y a 3 h ». Rend une chaine vide plutot qu'une approximation si la date manque. */
export function depuis(valeur: InstantApi | number | undefined): string {
  const d = instant(valeur);
  if (d === null) return '';

  const minutes = Math.round((Date.now() - d) / 60000);
  if (minutes < 1) return "a l'instant";
  if (minutes < 60) return `il y a ${minutes} min`;
  const heures = Math.round(minutes / 60);
  if (heures < 24) return `il y a ${heures} h`;
  return `il y a ${Math.round(heures / 24)} j`;
}

/**
 * Nombre venu de l'API.
 *
 * LES ENTIERS 64 BITS ARRIVENT PARFOIS EN CHAINE. La passerelle les rend
 * aujourd'hui en nombres, mais un serialiseur protobuf conforme les ecrit
 * entre guillemets : accepter les deux coute une ligne et evite un NaN
 * silencieux le jour ou la passerelle change d'avis.
 */
export function val(valeur: number | string | null | undefined): number {
  const n = Number(valeur ?? 0);
  return Number.isFinite(n) ? n : 0;
}

/** Duree lisible : « 4 min 30 s », « 2 h 15 min ». */
export function duree(secondes: number | string | null | undefined): string {
  const total = Math.round(val(secondes));
  if (total <= 0) return '—';
  if (total < 60) return `${total} s`;

  const minutes = Math.floor(total / 60);
  if (minutes < 60) {
    const reste = total % 60;
    return reste ? `${minutes} min ${reste} s` : `${minutes} min`;
  }

  const heures = Math.floor(minutes / 60);
  const reste = minutes % 60;
  return reste ? `${heures} h ${reste} min` : `${heures} h`;
}

/** « du 20 au 26 septembre ». La fenetre est demi-ouverte : la borne haute exclut le jour suivant. */
export function fenetre(debut: InstantApi | undefined, fin: InstantApi | undefined): string {
  const d = dateDe(debut);
  const f = dateDe(fin);
  if (d === null || f === null) return '';

  // La borne haute est exclue : on affiche la veille, qui est le dernier jour
  // reellement compte.
  const dernier = new Date(f.getTime() - 1000);
  const format: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'long' };

  return `du ${d.toLocaleDateString('fr-FR', format)} au ${dernier.toLocaleDateString('fr-FR', format)}`;
}
