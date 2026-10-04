import { redirect } from 'next/navigation';
import { NavigationDuPortail } from '@/composants/portail-navigation';
import { estCommercant } from '@/lib/accueil';
import { lirePrincipal } from '@/lib/serveur/session';

/**
 * Le portail commerçant, dans la même application que la console.
 *
 * POURQUOI ICI ET NON DANS UNE SECONDE APPLICATION. Le relais /api/hba relaie
 * déjà le préfixe merchant/v1, le jeton ne quitte jamais le serveur, la session
 * et son rafraîchissement existent, et les composants de base sont les mêmes.
 * Une seconde application Next aurait dupliqué tout ce socle, puis l'aurait
 * fait diverger.
 *
 * CE RENVOI N'EST PAS UNE MESURE DE SECURITE, C'EST UNE COMMODITE — même
 * raison que pour la console : le cookie lu ici ne porte aucun jeton. Ce qui
 * protège, c'est que la passerelle exige le rôle merchant et que les services
 * revérifient (ADR 0007).
 */
export default async function PortailLayout({ children }: { children: React.ReactNode }) {
  const principal = await lirePrincipal();

  if (!principal) redirect('/connexion');

  // UN MEMBRE DU BACK-OFFICE QUI ARRIVE ICI N'A PAS DE merchant_id : toutes les
  // routes lui répondraient « jeton sans merchant_id ». On le renvoie chez lui
  // plutôt que de lui montrer quatre écrans en erreur.
  if (!estCommercant(principal.roles)) redirect('/tableau-de-bord');

  return (
    <div className="flex min-h-screen flex-col">
      <NavigationDuPortail principal={principal} />

      <main className="mx-auto flex w-full max-w-[1100px] flex-1 flex-col gap-5 px-4 py-6 sm:px-6">
        {children}
      </main>
    </div>
  );
}
