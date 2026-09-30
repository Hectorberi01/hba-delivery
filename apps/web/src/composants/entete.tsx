'use client';

import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import type { Principal } from '@/lib/serveur/session';

const ONGLETS = [
  { href: '/tableau-de-bord', libelle: 'Tableau de bord' },
  { href: '/commandes', libelle: 'Commandes' },
  { href: '/livreurs', libelle: 'Livreurs' },
  { href: '/clients', libelle: 'Clients' },
  { href: '/versements', libelle: 'Versements' },
  { href: '/facturation', libelle: 'Facturation' },
  { href: '/parametres', libelle: 'Paramètres' },
];

export function Entete({ principal }: { principal: Principal }) {
  const chemin = usePathname();
  const routeur = useRouter();

  async function seDeconnecter() {
    await fetch('/api/session/deconnexion', { method: 'POST' });
    routeur.replace('/connexion');
    routeur.refresh();
  }

  return (
    <header className="relief rounded-[var(--radius-carte)] px-4 py-3">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <Marque />

        <nav aria-label="Sections de la console" className="order-3 w-full lg:order-2 lg:w-auto">
          <ul className="flex flex-wrap items-center gap-1">
            {ONGLETS.map((onglet) => {
              const actif = chemin.startsWith(onglet.href);
              return (
                <li key={onglet.href}>
                  <Link
                    href={onglet.href}
                    aria-current={actif ? 'page' : undefined}
                    // L'ONGLET COURANT EST CREUSE, PAS COLORE. Dans ce
                    // style, « enfonce » veut dire « deja choisi » ; c'est le
                    // meme signe que pour la periode selectionnee, et il vaut
                    // mieux qu'un aplat de plus.
                    className={`block rounded-xl px-3.5 py-2 text-sm font-medium transition ${
                      actif
                        ? 'creux text-marque'
                        : 'text-encre-2 hover:text-encre'
                    }`}
                  >
                    {onglet.libelle}
                  </Link>
                </li>
              );
            })}
          </ul>
        </nav>

        <div className="order-2 flex items-center gap-3 lg:order-3">
          <div className="text-right">
            <p className="text-sm font-medium text-encre">{principal.displayName}</p>
            <p className="text-xs text-encre-3">{principal.roles.join(', ')}</p>
          </div>
          <button
            type="button"
            onClick={seDeconnecter}
            className="relief-doux pressable rounded-xl px-3.5 py-2 text-sm text-encre-2 hover:text-encre"
          >
            Déconnexion
          </button>
        </div>
      </div>
    </header>
  );
}

export function Marque({ grand = false }: { grand?: boolean }) {
  return (
    <span className="flex items-center gap-2.5">
      <span
        aria-hidden
        className={`grid place-items-center rounded-xl bg-marque text-white shadow-[3px_3px_7px_var(--ombre-sombre)] ${grand ? 'h-11 w-11' : 'h-9 w-9'}`}
      >
        <svg viewBox="0 0 24 24" className={grand ? 'h-6 w-6' : 'h-5 w-5'} fill="currentColor">
          <path d="M13 2 4.5 13.5H11L10 22l8.5-11.5H12z" />
        </svg>
      </span>
      <span className={`font-semibold tracking-tight ${grand ? 'text-2xl' : 'text-lg'}`}>
        HBA<span className="text-marque">Delivery</span>
      </span>
    </span>
  );
}
