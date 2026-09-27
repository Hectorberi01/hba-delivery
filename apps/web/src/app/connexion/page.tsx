import { redirect } from 'next/navigation';
import { Marque } from '@/composants/entete';
import { lirePrincipal } from '@/lib/serveur/session';
import { FormulaireDeConnexion } from './formulaire';

export const metadata = { title: 'Connexion — Console HBA Delivery' };

export default async function PageDeConnexion() {
  if (await lirePrincipal()) redirect('/tableau-de-bord');

  return (
    <main className="grid min-h-screen place-items-center px-6 py-12">
      <div className="w-full max-w-[420px]">
        <div className="relief rounded-[var(--radius-carte)] p-8">
          <div className="flex justify-center">
            <Marque grand />
          </div>

          <h1 className="mt-7 text-center text-xl font-semibold text-encre">Connexion</h1>
          <p className="mt-1 text-center text-sm text-encre-3">
            Back-office : admin, ops, support, finance.
          </p>

          <FormulaireDeConnexion />
        </div>

        <p className="mt-6 text-center text-xs text-encre-3">
          Un identifiant inconnu et un mot de passe faux donnent la même réponse.
        </p>
      </div>
    </main>
  );
}
