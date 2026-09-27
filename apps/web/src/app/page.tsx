import { redirect } from 'next/navigation';
import { lirePrincipal } from '@/lib/serveur/session';

export default async function Accueil() {
  // La racine ne montre rien : elle oriente. Le principal ne prouve rien par
  // lui-meme — c'est la passerelle qui autorise — il dit seulement s'il y a
  // une session a tenter.
  redirect((await lirePrincipal()) ? '/tableau-de-bord' : '/connexion');
}
