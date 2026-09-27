import { cookies } from 'next/headers';
import type { NextResponse } from 'next/server';

/**
 * TROIS COOKIES, DONT DEUX QUE LE NAVIGATEUR NE LIT PAS.
 *
 * Les deux jetons sont httpOnly : un script injecte dans la page ne peut pas
 * les recopier. Seul le troisieme, « principal », est lisible — il ne contient
 * que le nom et les roles, sert a dessiner l'interface, et n'accorde rien.
 * L'autorisation reste verifiee par chaque service (ADR 0007) : masquer un
 * bouton n'a jamais empeche personne d'appeler la route.
 */
export const COOKIE_ACCES = 'hba_acces';
export const COOKIE_RAFRAICHISSEMENT = 'hba_rafraichissement';
export const COOKIE_PRINCIPAL = 'hba_principal';

export type Principal = {
  subjectId: string;
  displayName: string;
  email?: string;
  phone?: string;
  roles: string[];
};

const commun = {
  httpOnly: true,
  sameSite: 'lax' as const,
  secure: process.env.NODE_ENV === 'production',
  path: '/',
};

export async function lireAcces(): Promise<string | undefined> {
  return (await cookies()).get(COOKIE_ACCES)?.value;
}

export async function lireRafraichissement(): Promise<string | undefined> {
  return (await cookies()).get(COOKIE_RAFRAICHISSEMENT)?.value;
}

export async function lirePrincipal(): Promise<Principal | null> {
  const brut = (await cookies()).get(COOKIE_PRINCIPAL)?.value;
  if (!brut) return null;

  try {
    return JSON.parse(brut) as Principal;
  } catch {
    // Cookie illisible : on traite comme une absence de session plutot que de
    // faire tomber le rendu sur une valeur corrompue.
    return null;
  }
}

/** Duree du cookie d'acces : celle du jeton, bornee pour eviter une valeur absurde. */
function secondesAcces(expiresInSeconds: number | undefined): number {
  const valeur = Number(expiresInSeconds);
  return Number.isFinite(valeur) && valeur > 0 ? Math.min(valeur, 3600) : 900;
}

export function poserSession(
  reponse: NextResponse,
  paire: {
    accessToken: string;
    refreshToken: string;
    expiresInSeconds?: number;
    principal?: Principal;
  },
): void {
  reponse.cookies.set(COOKIE_ACCES, paire.accessToken, {
    ...commun,
    maxAge: secondesAcces(paire.expiresInSeconds),
  });

  // Trente jours, comme le jeton lui-meme. Il est a usage unique : chaque
  // rafraichissement le remplace, et un jeton deja consomme qui reviendrait
  // ferait revoquer toute la chaine cote Identity.
  reponse.cookies.set(COOKIE_RAFRAICHISSEMENT, paire.refreshToken, {
    ...commun,
    maxAge: 60 * 60 * 24 * 30,
  });

  if (paire.principal) {
    reponse.cookies.set(COOKIE_PRINCIPAL, JSON.stringify(paire.principal), {
      httpOnly: false,
      sameSite: 'lax',
      secure: process.env.NODE_ENV === 'production',
      path: '/',
      maxAge: 60 * 60 * 24 * 30,
    });
  }
}

export function effacerSession(reponse: NextResponse): void {
  for (const nom of [COOKIE_ACCES, COOKIE_RAFRAICHISSEMENT, COOKIE_PRINCIPAL]) {
    reponse.cookies.set(nom, '', { ...commun, httpOnly: nom !== COOKIE_PRINCIPAL, maxAge: 0 });
  }
}
