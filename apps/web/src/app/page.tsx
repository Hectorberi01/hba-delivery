import { redirect } from 'next/navigation';
import { accueilDe } from '@/lib/accueil';
import { lirePrincipal } from '@/lib/serveur/session';

export default async function Accueil() {
  // La racine ne montre rien : elle oriente. Le principal ne prouve rien par
  // lui-meme — c'est la passerelle qui autorise — il dit seulement s'il y a
  // une session a tenter.
  const principal = await lirePrincipal();

  redirect(principal ? accueilDe(principal.roles) : '/connexion');
}
