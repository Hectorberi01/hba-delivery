import { NextResponse } from 'next/server';
import { appelerPasserelle } from '@/lib/serveur/passerelle';
import { effacerSession, lireRafraichissement } from '@/lib/serveur/session';

export async function POST(): Promise<NextResponse> {
  const rafraichissement = await lireRafraichissement();

  if (rafraichissement) {
    // On revoque cote Identity avant d'effacer : sans cet appel, le jeton
    // resterait valide trente jours pour qui l'aurait intercepte.
    try {
      await appelerPasserelle('/api/web/v1/auth/logout', {
        method: 'POST',
        body: JSON.stringify({ refreshToken: rafraichissement }),
      });
    } catch {
      // La revocation a echoue : on efface quand meme cote navigateur, sinon
      // l'utilisateur reste connecte a un poste qu'il croit avoir quitte.
    }
  }

  const reponse = NextResponse.json({ ok: true });
  effacerSession(reponse);
  return reponse;
}
