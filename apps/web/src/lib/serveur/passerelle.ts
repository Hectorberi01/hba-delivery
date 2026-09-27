export const PASSERELLE = process.env.HBA_GATEWAY_URL ?? 'http://localhost:5100';
export const APPAREIL = process.env.HBA_DEVICE_ID ?? 'console-web';

export type PaireDeJetons = {
  accessToken: string;
  refreshToken: string;
  expiresInSeconds?: number;
  principal?: {
    subjectId: string;
    displayName: string;
    email?: string;
    phone?: string;
    roles: string[];
  };
};

/** Appel brut a la passerelle. Le jeton est facultatif : les routes d'authentification n'en ont pas. */
export async function appelerPasserelle(
  chemin: string,
  init: RequestInit & { jeton?: string } = {},
): Promise<Response> {
  const { jeton, headers, ...reste } = init;

  const entetes = new Headers(headers);
  if (jeton) entetes.set('authorization', `Bearer ${jeton}`);
  if (reste.body && !entetes.has('content-type')) {
    entetes.set('content-type', 'application/json');
  }

  return fetch(`${PASSERELLE}${chemin}`, {
    ...reste,
    headers: entetes,
    // Une console n'a aucune raison de servir une page de cache : les etats de
    // course changent a la minute.
    cache: 'no-store',
  });
}

/**
 * Echange le jeton de rafraichissement contre une nouvelle paire.
 *
 * Rend null sur tout echec, sans distinguer les causes : jeton expire, jeton
 * deja consomme, Identity indisponible. L'appelant n'a qu'une chose a en
 * faire — renvoyer vers la connexion.
 */
export async function rafraichir(jetonDeRafraichissement: string): Promise<PaireDeJetons | null> {
  try {
    const reponse = await appelerPasserelle('/api/web/v1/auth/refresh', {
      method: 'POST',
      body: JSON.stringify({ refreshToken: jetonDeRafraichissement, deviceId: APPAREIL }),
    });

    if (!reponse.ok) return null;
    return (await reponse.json()) as PaireDeJetons;
  } catch {
    return null;
  }
}
