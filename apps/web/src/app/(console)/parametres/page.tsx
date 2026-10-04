import { redirect } from 'next/navigation';

/**
 * PARAMETRES EST DEVENU « MON PROFIL » LE 2 OCTOBRE 2026.
 *
 * La page melait trois choses sans rapport : le compte de celui qui regarde,
 * la creation de comptes de back-office, et les clients OAuth des partenaires.
 * Les deux dernieres ont leurs pages — Equipe et Partenaires —, et ce qui
 * reste est le profil.
 *
 * CETTE REDIRECTION N'EST PAS DU ZELE : l'adresse a pu etre mise en favori, et
 * un 404 sur « /parametres » se lirait comme une panne de la console.
 */
export default function PageParametres() {
  redirect('/profil');
}
