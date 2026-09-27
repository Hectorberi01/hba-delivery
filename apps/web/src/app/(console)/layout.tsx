import { redirect } from 'next/navigation';
import { Entete } from '@/composants/entete';
import { lirePrincipal } from '@/lib/serveur/session';

export default async function ConsoleLayout({ children }: { children: React.ReactNode }) {
  const principal = await lirePrincipal();

  // CE RENVOI N'EST PAS UNE MESURE DE SECURITE, C'EST UNE COMMODITE. Le cookie
  // lu ici ne porte aucun jeton : il dit qu'une session a ete ouverte, rien de
  // plus. Ce qui protege reellement les donnees, c'est que chaque service
  // verifie le JWT qu'on lui presente (ADR 0007).
  if (!principal) redirect('/connexion');

  return (
    <div className="mx-auto w-full max-w-[1440px] px-4 py-5 sm:px-6">
      <Entete principal={principal} />
      <main className="mt-5 space-y-5 pb-12">{children}</main>
    </div>
  );
}
