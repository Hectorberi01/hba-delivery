import { NextResponse, type NextRequest } from 'next/server';
import { APPAREIL, appelerPasserelle, type PaireDeJetons } from '@/lib/serveur/passerelle';
import { poserSession } from '@/lib/serveur/session';
import { accueilDe } from '@/lib/accueil';

const ROLES_ACCEPTES = [
  'admin',
  'ops',
  'support',
  'finance',
  // LE PORTAIL COMMERCANT VIT DANS LA MEME APPLICATION depuis le 2 octobre
  // 2026. Cette route refusait tout compte hors back-office : un commercant
  // muni d'un compte valide se voyait repondre « pas d'acces », ce qui etait
  // vrai de la console et faux de son portail.
  'merchant_owner',
  'merchant_staff',
];

export async function POST(requete: NextRequest): Promise<NextResponse> {
  const { login, motDePasse } = (await requete.json()) as {
    login?: string;
    motDePasse?: string;
  };

  if (!login || !motDePasse) {
    return NextResponse.json({ code: 'CHAMPS_MANQUANTS' }, { status: 400 });
  }

  const reponse = await appelerPasserelle('/api/web/v1/auth/login', {
    method: 'POST',
    body: JSON.stringify({ login, password: motDePasse, deviceId: APPAREIL }),
  });

  if (!reponse.ok) {
    // Identifiant inconnu et mot de passe faux donnent deja la meme reponse
    // cote Identity. On la transmet telle quelle, sans rien y ajouter.
    return NextResponse.json(
      { code: 'IDENTIFIANTS_REFUSES', message: 'Identifiants incorrects.' },
      { status: 401 },
    );
  }

  const paire = (await reponse.json()) as PaireDeJetons;
  const roles = paire.principal?.roles ?? [];

  // CETTE VERIFICATION NE PROTEGE RIEN, ELLE EXPLIQUE. Un client ou un livreur
  // a un compte valide : sans ce message, il se connecterait a une application
  // dont chaque ecran lui renverrait un refus sans dire pourquoi. Les services,
  // eux, refusent de toute facon (ADR 0007).
  if (!roles.some((role) => ROLES_ACCEPTES.includes(role))) {
    return NextResponse.json(
      {
        code: 'HORS_PERIMETRE',
        message: "Ce compte n'a acces ni au back-office ni au portail commercant.",
      },
      { status: 403 },
    );
  }

  // L'ACCUEIL EST DECIDE ICI, OU LES ROLES SONT CONNUS. Le formulaire ne les
  // lit pas : le cookie de principal n'est pose qu'avec la reponse.
  const succes = NextResponse.json({ principal: paire.principal, accueil: accueilDe(roles) });
  poserSession(succes, paire);
  return succes;
}
