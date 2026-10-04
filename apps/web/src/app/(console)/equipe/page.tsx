import { EcranEquipe } from '@/composants/equipe';
import { lirePrincipal } from '@/lib/serveur/session';

/**
 * La page Équipe : qui administre la plateforme.
 *
 * COMPOSANT SERVEUR, POUR UNE SEULE RAISON : savoir QUI regarde. L'écran doit
 * reconnaître votre propre compte dans la liste — on ne se suspend pas
 * soi-même — et le principal se lit dans un cookie, donc côté serveur. Tout le
 * reste de l'écran est client, comme les autres pages.
 *
 * CE N'EST PAS UNE PROTECTION. Le cookie « principal » ne porte aucun jeton et
 * n'accorde rien : la passerelle exige le rôle admin et Identity le revérifie
 * (ADR 0007). Ce que l'on fait ici, c'est dessiner, pas autoriser.
 */
export default async function PageEquipe() {
  const principal = await lirePrincipal();

  return <EcranEquipe moi={principal?.subjectId ?? ''} />;
}
