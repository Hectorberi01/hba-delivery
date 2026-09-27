import { NextResponse, type NextRequest } from 'next/server';
import { APPAREIL, appelerPasserelle, type PaireDeJetons } from '@/lib/serveur/passerelle';
import { poserSession } from '@/lib/serveur/session';

const ROLES_BACK_OFFICE = ['admin', 'ops', 'support', 'finance'];

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

  // CETTE VERIFICATION NE PROTEGE RIEN, ELLE EXPLIQUE. Un commercant a un
  // compte valide : sans ce message, il se connecterait a une console dont
  // chaque ecran lui renverrait un refus sans dire pourquoi. Les services,
  // eux, refusent de toute facon (ADR 0007).
  if (!roles.some((role) => ROLES_BACK_OFFICE.includes(role))) {
    return NextResponse.json(
      {
        code: 'HORS_BACK_OFFICE',
        message: "Ce compte n'a pas acces au back-office.",
      },
      { status: 403 },
    );
  }

  const succes = NextResponse.json({ principal: paire.principal });
  poserSession(succes, paire);
  return succes;
}
