/**
 * Distances, cote console.
 *
 * POURQUOI LA CONSOLE CALCULE UNE DISTANCE QU'ELLE POURRAIT DEMANDER. La
 * liste des positions rend N livreurs ; poser la question « a quelle distance
 * du retrait ? » au service pour chacun ferait N allers-retours pour trier
 * une liste. Le tri se fait donc ici, et la mesure qui COMPTE — celle qui
 * part avec l'offre et que le livreur verra — est refaite par le service au
 * moment ou l'offre est posee.
 */

/**
 * LE RAYON TERRESTRE DE REDIS, PAS CELUI DES MANUELS.
 *
 * Le service mesure sur une sphere de 6 372 797,560856 m, parce que c'est
 * celle de GEORADIUS. Prendre les 6 371 000 m habituels ici afficherait une
 * distance dans la liste et une autre dans la confirmation, pour le meme
 * livreur et le meme retrait. L'ecart est de trois pour mille : invisible sur
 * le terrain, et largement suffisant pour faire douter de la mesure.
 */
const RAYON_TERRESTRE_M = 6372797.560856;

export type Point = { latitude?: number; longitude?: number };

/** Distance orthodromique en metres, ou null si l'un des deux points manque. */
export function distanceMetres(a: Point | null | undefined, b: Point | null | undefined): number | null {
  if (
    typeof a?.latitude !== 'number' ||
    typeof a?.longitude !== 'number' ||
    typeof b?.latitude !== 'number' ||
    typeof b?.longitude !== 'number'
  ) {
    return null;
  }

  const enRadians = Math.PI / 180;

  const phi1 = a.latitude * enRadians;
  const phi2 = b.latitude * enRadians;
  const dPhi = (b.latitude - a.latitude) * enRadians;
  const dLambda = (b.longitude - a.longitude) * enRadians;

  const sinPhi = Math.sin(dPhi / 2);
  const sinLambda = Math.sin(dLambda / 2);

  const h = sinPhi * sinPhi + Math.cos(phi1) * Math.cos(phi2) * sinLambda * sinLambda;

  return 2 * RAYON_TERRESTRE_M * Math.asin(Math.min(1, Math.sqrt(h)));
}

/**
 * Distance lisible. EN DESSOUS DU KILOMETRE ON GARDE LES METRES, arrondis a
 * la dizaine : « 430 m » se decide d'un coup d'oeil, « 0,4 km » demande une
 * conversion mentale, et « 437 m » promet une precision que le GPS d'un
 * telephone en ville n'a pas.
 */
export function metres(valeur: number | null | undefined): string {
  if (typeof valeur !== 'number' || Number.isNaN(valeur)) return '—';
  if (valeur < 1000) return `${Math.round(valeur / 10) * 10} m`;

  return `${(valeur / 1000).toLocaleString('fr-FR', { maximumFractionDigits: 1 })} km`;
}
