import { redirect } from 'next/navigation';
import { BarreLaterale, Entete } from '@/composants/entete';
import { lirePrincipal } from '@/lib/serveur/session';

export default async function ConsoleLayout({ children }: { children: React.ReactNode }) {
  const principal = await lirePrincipal();

  // CE RENVOI N'EST PAS UNE MESURE DE SECURITE, C'EST UNE COMMODITE. Le cookie
  // lu ici ne porte aucun jeton : il dit qu'une session a ete ouverte, rien de
  // plus. Ce qui protege reellement les donnees, c'est que chaque service
  // verifie le JWT qu'on lui presente (ADR 0007).
  if (!principal) redirect('/connexion');

  return (
    // LA COLONNE DISPARAIT SOUS 1024px, ET LA NAVIGATION PASSE EN HAUT. Une
    // barre laterale fixe sur un ecran etroit mange la moitie de la largeur ;
    // la meme liste, posee au-dessus du contenu, defile avec la page.
    <div className="min-h-screen lg:grid lg:grid-cols-[260px_1fr]">
      <aside className="lg:sticky lg:top-0 lg:h-screen lg:overflow-y-auto">
        <BarreLaterale principal={principal} />
      </aside>

      <div className="flex min-h-screen min-w-0 flex-col">
        <Entete principal={principal} />

        {/* LA LARGEUR EST BORNEE, PAS LA PAGE. Un tableau de bord etire sur
            2560px eloigne ses cartes au point qu'on ne les compare plus ; la
            borne vit ici et non sur le shell, pour que la barre laterale touche
            bien le bord de l'ecran.

            1600 ET NON 1400 DEPUIS LE 2 OCTOBRE 2026 : la carte des livreurs a
            sa propre page, et deux cents pixels de rue valent plus pour elle que
            pour une grille de quatre tuiles, que la borne ne genait pas.

            « flex flex-col » PLUTOT QU'UN EMPILEMENT, ET C'EST CE QUI PERMET A UNE
            PAGE DE PRENDRE LA HAUTEUR. Avec « space-y », un enfant en « flex-1 »
            ne recoit rien : il n'y a pas de conteneur flexible au-dessus de lui.
            Le « gap-5 » rend exactement le meme espacement entre des enfants
            empiles, donc aucune page n'a bouge. */}
        <main className="mx-auto flex w-full max-w-[1600px] flex-1 flex-col gap-5 px-4 py-6 sm:px-6">
          {children}
        </main>
      </div>
    </div>
  );
}
