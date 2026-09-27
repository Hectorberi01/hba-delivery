'use client';

import { useCallback, useEffect, useState } from 'react';

export type Ressource<T> = {
  donnees: T | null;
  chargement: boolean;
  erreur: string | null;
  recharger: () => void;
};

/**
 * Lecture d'une route de la passerelle, a travers la procuration.
 *
 * Le chemin est relatif : « admin/v1/deliveries ». Le jeton est pose cote
 * serveur, le navigateur n'en detient aucun.
 */
export function useRessource<T>(chemin: string | null): Ressource<T> {
  const [donnees, setDonnees] = useState<T | null>(null);
  const [chargement, setChargement] = useState(chemin !== null);
  const [erreur, setErreur] = useState<string | null>(null);
  const [compteur, setCompteur] = useState(0);

  const recharger = useCallback(() => setCompteur((n) => n + 1), []);

  useEffect(() => {
    if (!chemin) return;

    let vivant = true;
    setChargement(true);
    setErreur(null);

    fetch(`/api/hba/${chemin}`, { headers: { accept: 'application/json' } })
      .then(async (reponse) => {
        if (reponse.status === 401) {
          // La session est finie cote serveur : on renvoie a la connexion
          // plutot que d'afficher une erreur que l'utilisateur ne peut pas
          // corriger depuis cet ecran.
          window.location.href = '/connexion';
          return null;
        }

        if (!reponse.ok) {
          throw new Error(expliquer(reponse.status, await reponse.text(), chemin));
        }

        return (await reponse.json()) as T;
      })
      .then((valeur) => {
        if (!vivant || valeur === null) return;
        setDonnees(valeur);
      })
      .catch((cause: unknown) => {
        if (!vivant) return;
        setErreur(cause instanceof Error ? cause.message : 'Erreur inattendue.');
      })
      .finally(() => {
        if (vivant) setChargement(false);
      });

    return () => {
      vivant = false;
    };
  }, [chemin, compteur]);

  return { donnees, chargement, erreur, recharger };
}

/**
 * Ce qu'un echec veut dire, en clair.
 *
 * UN 404 SUR UNE ROUTE DE LA CONSOLE N'EST PAS UNE DONNEE ABSENTE. La
 * procuration ne relaie que trois prefixes et rend 403 hors de ceux-la ; un
 * 404 vient donc de la passerelle elle-meme, et veut dire qu'elle ne connait
 * pas cette adresse. En developpement, c'est presque toujours la meme cause :
 * l'image du conteneur est anterieure a l'ecran qui l'appelle. « Erreur 404 »
 * affiche seul laisse chercher ailleurs pendant dix minutes.
 *
 * LE CORPS DU SERVICE PASSE AVANT, quand il y en a un : il porte le code
 * metier, et lui seul sait pourquoi il a refuse.
 */
function expliquer(statut: number, corps: string, chemin: string): string {
  const texte = corps.trim();

  if (texte) {
    try {
      const objet = JSON.parse(texte) as { message?: string; code?: string };
      return objet.message ?? objet.code ?? texte;
    } catch {
      return texte;
    }
  }

  if (statut === 404) {
    return `La passerelle ne connaît pas la route « ${chemin} ». `
      + "Son image est probablement antérieure à cet écran : reconstruisez-la.";
  }

  return `Erreur ${statut}`;
}

/** Ecriture. Rend le corps de reponse, ou leve avec le message du service. */
export async function envoyer<T>(
  chemin: string,
  corps: unknown,
  options: { methode?: string; idempotence?: string } = {},
): Promise<T> {
  const entetes: Record<string, string> = { 'content-type': 'application/json' };
  if (options.idempotence) entetes['hba-idempotency-key'] = options.idempotence;

  const reponse = await fetch(`/api/hba/${chemin}`, {
    method: options.methode ?? 'POST',
    headers: entetes,
    body: JSON.stringify(corps),
  });

  const texte = await reponse.text();

  if (!reponse.ok) {
    throw new Error(expliquer(reponse.status, texte, chemin));
  }

  return texte ? (JSON.parse(texte) as T) : ({} as T);
}
