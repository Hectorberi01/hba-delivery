'use client';

import { useEffect, useState } from 'react';

/**
 * Laisse la frappe se terminer avant de rendre la valeur.
 *
 * SANS CELA, UNE RECHERCHE EST UN APPEL PAR CARACTERE. Le terme saisi entre
 * dans l'URL d'une ressource ; taper « +2290195 » declenchait huit
 * allers-retours jusqu'a Postgres, dont sept dont personne ne lira jamais le
 * resultat. Sur l'annuaire client, chacun de ces appels est en plus une
 * lecture de donnees personnelles — donc, le jour ou elles se journalisent,
 * huit lignes de journal pour une seule recherche.
 *
 * TROIS CENTS MILLISECONDES : assez pour qu'une frappe continue ne parte pas,
 * assez peu pour que la liste suive la main. En dessous, la frappe rapide
 * repart ; au-dessus, l'ecran parait en retard sur le clavier.
 */
export function useAttente<T>(valeur: T, millisecondes = 300): T {
  const [differee, setDifferee] = useState(valeur);

  useEffect(() => {
    const minuteur = window.setTimeout(() => setDifferee(valeur), millisecondes);
    return () => window.clearTimeout(minuteur);
  }, [valeur, millisecondes]);

  return differee;
}
