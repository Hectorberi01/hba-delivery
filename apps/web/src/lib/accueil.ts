/**
 * Ou atterrit une session, selon ses roles.
 *
 * UNE SEULE FONCTION POUR QUATRE ENDROITS. La racine, la page de connexion, le
 * formulaire et la procuration de session renvoyaient tous « /tableau-de-bord »
 * en dur ; depuis que le portail commercant existe, un commercant y serait
 * tombe sur une console dont chaque ecran lui aurait repondu 403.
 *
 * CE N'EST PAS UNE AUTORISATION. Ce que cette fonction decide, c'est la
 * premiere page montree. Ce qui protege, c'est le refus des services (ADR
 * 0007) et les politiques de la passerelle.
 */
const BACK_OFFICE = ['admin', 'ops', 'support', 'finance'];
const COMMERCANT = ['merchant_owner', 'merchant_staff'];

export function accueilDe(roles: readonly string[]): string {
  if (roles.some((role) => BACK_OFFICE.includes(role))) return '/tableau-de-bord';
  if (roles.some((role) => COMMERCANT.includes(role))) return '/portail';

  // NI L'UN NI L'AUTRE : on renvoie a la connexion plutot que d'ouvrir une
  // porte au hasard. Un client ou un livreur n'a rien a faire ici — ses
  // applications sont ailleurs.
  return '/connexion';
}

export function estCommercant(roles: readonly string[]): boolean {
  return roles.some((role) => COMMERCANT.includes(role));
}
