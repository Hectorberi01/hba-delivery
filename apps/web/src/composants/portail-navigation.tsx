'use client';

import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { Initiales } from '@/composants/base';
import { Marque } from '@/composants/entete';
import type { Principal } from '@/lib/serveur/session';

/**
 * LA NAVIGATION DU PORTAIL EST HORIZONTALE, celle de la console verticale.
 *
 * Ce n'est pas une coquetterie : la console a onze sections et en gagnera
 * d'autres, le portail en a quatre et n'en aura pas vingt. Une colonne de
 * 260 pixels pour quatre entrees prendrait de la place a un ecran qui sert
 * surtout a remplir un formulaire d'adresse.
 */
const ONGLETS = [
  { href: '/portail', libelle: 'Mes courses', exact: true },
  { href: '/portail/nouvelle', libelle: 'Commander' },
  { href: '/portail/points-de-retrait', libelle: 'Points de retrait' },
  { href: '/portail/profil', libelle: 'Mon commerce' },
];

export function NavigationDuPortail({ principal }: { principal: Principal }) {
  const chemin = usePathname();
  const routeur = useRouter();

  async function deconnecter() {
    await fetch('/api/session/deconnexion', { method: 'POST' });
    routeur.replace('/connexion');
    routeur.refresh();
  }

  return (
    <header className="border-b border-bordure bg-surface">
      <div className="mx-auto flex w-full max-w-[1100px] flex-wrap items-center gap-4 px-4 py-3 sm:px-6">
        <Marque />

        <nav aria-label="Sections du portail" className="flex flex-1 flex-wrap gap-1">
          {ONGLETS.map((onglet) => {
            const actif = onglet.exact ? chemin === onglet.href : chemin.startsWith(onglet.href);

            return (
              <Link
                key={onglet.href}
                href={onglet.href}
                aria-current={actif ? 'page' : undefined}
                className={`rounded-xl px-3.5 py-2 text-sm font-medium transition ${
                  actif ? 'bg-marque-doux text-marque' : 'text-encre-2 hover:text-encre'
                }`}
              >
                {onglet.libelle}
              </Link>
            );
          })}
        </nav>

        <div className="flex items-center gap-3">
          <Initiales nom={principal.displayName || '?'} />
          <button
            type="button"
            onClick={deconnecter}
            className="relief-doux pressable rounded-xl px-3.5 py-2 text-sm font-medium text-encre-2 hover:text-encre"
          >
            Déconnexion
          </button>
        </div>
      </div>
    </header>
  );
}
