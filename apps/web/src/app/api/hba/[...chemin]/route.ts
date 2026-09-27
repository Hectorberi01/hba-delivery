import { NextResponse, type NextRequest } from 'next/server';
import { appelerPasserelle, rafraichir } from '@/lib/serveur/passerelle';
import {
  effacerSession,
  lireAcces,
  lireRafraichissement,
  poserSession,
} from '@/lib/serveur/session';

/**
 * PROCURATION VERS LA PASSERELLE.
 *
 * Le navigateur appelle /api/hba/admin/v1/deliveries ; ce gestionnaire pose le
 * jeton et appelle /api/admin/v1/deliveries. Le jeton ne quitte donc jamais le
 * serveur. Sur un 401, il rafraichit UNE fois et rejoue : un jeton d'acces vit
 * quinze minutes, une console ouverte toute la journee le franchit sans que
 * personne ne se reconnecte.
 *
 * ELLE NE RELAIE QUE TROIS PREFIXES. Une procuration qui colle un jeton
 * d'administrateur sur n'importe quel chemin est un depute confus : il
 * suffirait d'une adresse inattendue pour lui faire signer un appel que
 * l'interface ne propose nulle part.
 */
const PREFIXES_AUTORISES = ['admin/v1', 'web/v1', 'merchant/v1'];

const METHODES_AVEC_CORPS = new Set(['POST', 'PUT', 'PATCH']);

async function relayer(
  requete: NextRequest,
  contexte: { params: Promise<{ chemin: string[] }> },
): Promise<NextResponse> {
  const { chemin } = await contexte.params;

  if (chemin.some((segment) => segment === '..' || segment.includes('/'))) {
    return NextResponse.json({ code: 'CHEMIN_INVALIDE' }, { status: 400 });
  }

  const relatif = chemin.join('/');

  if (!PREFIXES_AUTORISES.some((prefixe) => relatif.startsWith(prefixe))) {
    return NextResponse.json({ code: 'CHEMIN_NON_RELAYE' }, { status: 403 });
  }

  const acces = await lireAcces();
  const rafraichissement = await lireRafraichissement();

  if (!acces && !rafraichissement) {
    return NextResponse.json({ code: 'SESSION_ABSENTE' }, { status: 401 });
  }

  const cible = `/api/${relatif}${requete.nextUrl.search}`;
  const corps = METHODES_AVEC_CORPS.has(requete.method) ? await requete.text() : undefined;

  const entetes = new Headers();
  const typeDeContenu = requete.headers.get('content-type');
  if (typeDeContenu) entetes.set('content-type', typeDeContenu);

  // L'en-tete d'idempotence vient de l'appelant et doit traverser tel quel :
  // c'est lui qui evite qu'un double clic cree deux fois la meme chose.
  const idempotence = requete.headers.get('hba-idempotency-key');
  if (idempotence) entetes.set('hba-idempotency-key', idempotence);

  let reponse = await appelerPasserelle(cible, {
    method: requete.method,
    headers: entetes,
    body: corps,
    jeton: acces,
  });

  if (reponse.status !== 401 || !rafraichissement) {
    return await transmettre(reponse);
  }

  const paire = await rafraichir(rafraichissement);

  if (!paire) {
    // Le rafraichissement a echoue : la session est finie, on nettoie plutot
    // que de laisser l'interface boucler sur des 401.
    const fin = NextResponse.json({ code: 'SESSION_EXPIREE' }, { status: 401 });
    effacerSession(fin);
    return fin;
  }

  reponse = await appelerPasserelle(cible, {
    method: requete.method,
    headers: entetes,
    body: corps,
    jeton: paire.accessToken,
  });

  const rejouee = await transmettre(reponse);
  poserSession(rejouee, paire);
  return rejouee;
}

async function transmettre(reponse: Response): Promise<NextResponse> {
  const texte = await reponse.text();

  return new NextResponse(texte || null, {
    status: reponse.status,
    headers: {
      'content-type': reponse.headers.get('content-type') ?? 'application/json',
    },
  });
}

export const GET = relayer;
export const POST = relayer;
export const PUT = relayer;
export const PATCH = relayer;
export const DELETE = relayer;
