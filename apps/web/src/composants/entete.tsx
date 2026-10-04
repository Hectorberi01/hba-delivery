'use client';

import Link from 'next/link';
import { usePathname, useRouter } from 'next/navigation';
import { useEffect, useState } from 'react';
import type { Principal } from '@/lib/serveur/session';

/**
 * Une section de la console. Sans « roles », elle est visible par tout le
 * back-office.
 *
 * L'ICONE EST UN TRACE, PAS UNE DEPENDANCE. Une bibliotheque d'icones pour
 * neuf symboles ajouterait un paquet a installer, a versionner et a charger,
 * pour des formes qui ne changeront pas.
 */
const ONGLETS: {
  href: string;
  libelle: string;
  trace: string;
  roles?: string[];
  /** Reservee aux proprietaires de la plateforme : la passerelle dit qui l'est. */
  proprietaire?: boolean;
}[] = [
  {
    href: '/tableau-de-bord',
    libelle: 'Tableau de bord',
    trace: 'M3 13h8V3H3v10Zm0 8h8v-6H3v6Zm10 0h8V11h-8v10Zm0-18v6h8V3h-8Z',
  },
  {
    href: '/commandes',
    libelle: 'Commandes',
    trace: 'M3 4h18v4H3V4Zm1 6h16v10H4V10Zm5 3h6v2H9v-2Z',
  },
  {
    href: '/livreurs',
    libelle: 'Livreurs',
    trace:
      'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm0 2c-4 0-8 2-8 5v1h16v-1c0-3-4-5-8-5Z',
  },
  // LA CARTE EST UNE SECTION A ELLE, PAS UN BLOC DE L'ANNUAIRE. C'est le seul
  // ecran de la console qu'on laisse ouvert : il lui faut une entree, pas un
  // defilement.
  {
    href: '/carte',
    libelle: 'Carte',
    // TROIS VOLETS PLEINS, PAS UN CONTOUR. Ces icones sont rendues en
    // « fill », et un trace pense pour un trait s'y remplit en tache.
    trace: 'M3 5.5 9 3v16l-6 2.5v-16Zm7-2.5 5 2v16l-5-2V3Zm6 2 5-2.5v16L16 21V5Z',
  },
  {
    href: '/clients',
    libelle: 'Clients',
    trace:
      'M9 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7Zm8 0a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM2 19c0-3 3.5-5 7-5s7 2 7 5v1H2v-1Zm15.5-4.5c2.6.4 4.5 2 4.5 4.5v1h-4v-1c0-1.4-.2-2.5-.5-3.5Z',
  },
  // LES COMMERCANTS SE REFERENCENT DEPUIS LA CONSOLE, et nulle part ailleurs :
  // personne ne s'inscrit soi-meme comme commerce. L'entree manquait alors que
  // le service et la passerelle savaient tout faire depuis l'origine.
  {
    href: '/commercants',
    libelle: 'Commerçants',
    // UNE DEVANTURE AVEC SON AUVENT. Ni une personne (Clients), ni une puce
    // (Partenaires) : une entreprise a pignon sur rue.
    trace: 'M4 4h16l1.5 5H2.5L4 4Zm-1 7h18v9H3v-9Zm4 2v5h4v-5H7Zm7 0v3h4v-3h-4Z',
  },
  {
    href: '/versements',
    libelle: 'Versements',
    trace: 'M3 6h18v12H3V6Zm2 2v8h14V8H5Zm10 2h3v4h-3a2 2 0 1 1 0-4Z',
  },
  {
    href: '/facturation',
    libelle: 'Facturation',
    trace: 'M5 2h14v20l-3-2-2 2-2-2-2 2-2-2-3 2V2Zm3 5h8v2H8V7Zm0 4h8v2H8v-2Z',
  },

  // LES TARIFS NE SONT PAS POUR TOUT LE BACK-OFFICE. Ops et support traitent des
  // courses, pas des baremes : la passerelle exige « admin ou finance » et
  // Pricing le reverifie (ADR 0007). L'entree suit la meme regle, pour ne pas
  // proposer une porte qui se refermera au clic.
  {
    href: '/tarifs',
    libelle: 'Tarifs',
    trace: 'M3 3h8l10 10-8 8L3 11V3Zm4 3a2 2 0 1 0 0 4 2 2 0 0 0 0-4Z',
    roles: ['admin', 'finance'],
  },

  // LES ZONES SE LISENT PLUS LARGEMENT QU'ELLES NE S'ECRIVENT. La carte explique
  // un prix et un dispatch : ops en a besoin. Dessiner, en revanche, deplace des
  // prix — la passerelle reserve l'ecriture a admin, et l'ecran affiche son refus
  // tel quel plutot que de cacher le bouton a moitie.
  {
    href: '/zones',
    libelle: 'Zones',
    trace:
      'M12 2a7 7 0 0 0-7 7c0 5 7 13 7 13s7-8 7-13a7 7 0 0 0-7-7Zm0 9.5A2.5 2.5 0 1 1 12 6.5a2.5 2.5 0 0 1 0 5Z',
  },
  // LE JOURNAL DES ENVOIS : admin, ops et support — PAS finance, meme decoupe
  // que l'annuaire des clients, et pour la meme raison : il porte des numeros,
  // fussent-ils masques.
  {
    href: '/notifications',
    libelle: 'Notifications',
    // UNE CLOCHE, ET ELLE NE SONNE PAS. Celle de l'en-tete avait ete retiree
    // faute de service derriere ; celle-ci mene a un journal qui existe.
    trace:
      'M12 2a6 6 0 0 0-6 6v3.6l-1.7 3.1A1 1 0 0 0 5.2 16h13.6a1 1 0 0 0 .9-1.3L18 11.6V8a6 6 0 0 0-6-6Zm0 20a3 3 0 0 0 3-3H9a3 3 0 0 0 3 3Z',
    roles: ['admin', 'ops', 'support'],
  },

  // L'EQUIPE EST RESERVEE A admin, ET L'ENTREE SUIT LA ROUTE. La passerelle
  // exige admin sur la liste des comptes de back-office et Identity le
  // reverifie : proposer l'entree a ops ou support serait proposer une porte
  // qui se referme au clic.
  {
    href: '/equipe',
    libelle: 'Équipe',
    // UN BOUCLIER AVEC UNE PERSONNE : ni l'annuaire des clients ni celui des
    // livreurs, mais ceux qui ont des droits.
    trace:
      'M12 3 4 6v5c0 4.4 3.2 8.4 8 10 4.8-1.6 8-5.6 8-10V6l-8-3Zm0 5a2.5 2.5 0 1 1 0 5 2.5 2.5 0 0 1 0-5Zm0 6c2 0 4 1 4 2.5V17H8v-.5C8 15 10 14 12 14Z',
    roles: ['admin'],
  },
  // LES CLIENTS OAUTH DES PARTENAIRES, SORTIS DE PARAMETRES le 2 octobre 2026 :
  // ouvrir le B2B n'est pas un reglage personnel.
  {
    href: '/partenaires',
    libelle: 'Partenaires',
    trace:
      'M7 3h10a2 2 0 0 1 2 2v3h2v4h-2v7a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2v-7H3V8h2V5a2 2 0 0 1 2-2Zm2 5h6V5H9v3Z',
    roles: ['admin'],
  },
  // LA PLATEFORME N'EST PAS FILTREE PAR UN ROLE, PARCE QU'IL N'Y EN A PAS.
  // Le referentiel acteurs ne connait pas de « super admin » : les
  // proprietaires sont nommes dans la configuration de la passerelle (ADR 0018
  // pour le principe). L'entree se montre donc sur la reponse de la passerelle,
  // pas sur un role du jeton — d'ou « proprietaire » plutot que « roles ».
  {
    href: '/plateforme',
    libelle: 'Plateforme',
    // DES ROUAGES RELIES : l'etat de ce qui tourne, pas un reglage personnel.
    trace:
      'M4 5h7v6H4V5Zm9 0h7v4h-7V5Zm0 6h7v8h-7v-8ZM4 13h7v6H4v-6Z',
    proprietaire: true,
  },
  {
    href: '/profil',
    libelle: 'Mon profil',
    trace:
      'M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8Zm9 4-2-.6a7 7 0 0 0-.7-1.7l1-1.8-2.2-2.2-1.8 1a7 7 0 0 0-1.7-.7L13 2h-2l-.6 2a7 7 0 0 0-1.7.7l-1.8-1-2.2 2.2 1 1.8a7 7 0 0 0-.7 1.7L3 12v2l2 .6c.2.6.4 1.2.7 1.7l-1 1.8 2.2 2.2 1.8-1c.5.3 1.1.5 1.7.7l.6 2h2l.6-2c.6-.2 1.2-.4 1.7-.7l1.8 1 2.2-2.2-1-1.8c.3-.5.5-1.1.7-1.7l2-.6v-2Z',
  },
];

/**
 * La navigation de la console, en colonne a gauche.
 *
 * ELLE ETAIT EN ONGLETS EN HAUT JUSQU'AU 2 OCTOBRE 2026. Neuf entrees sur une
 * ligne passaient a la ligne des que la fenetre retrecissait, et la section
 * courante pouvait finir seule sur un second rang. En colonne, l'ordre ne bouge
 * plus et une dixieme section s'ajoute sans rien reorganiser.
 *
 * CACHER N'EST PAS PROTEGER, et ce n'est pas le but : ce qui protege, c'est le
 * refus du service. Une entree qui mene a un 403 est juste un mauvais ecran.
 */
export function BarreLaterale({ principal }: { principal: Principal }) {
  const chemin = usePathname();

  // LA SEULE ENTREE QUI DEMANDE SON EXISTENCE A LA PASSERELLE. Un role se lit
  // dans le jeton, la propriete de la plateforme non : elle vit dans la
  // configuration de l'hote. Un appel, au chargement, et l'entree apparait ou
  // reste absente. Un echec vaut « non » — et ne cache rien : la route refuse
  // de toute facon.
  const [proprietaire, setProprietaire] = useState(false);

  useEffect(() => {
    let vivant = true;

    fetch('/api/hba/admin/v1/plateforme/acces', { headers: { accept: 'application/json' } })
      .then(async (reponse) => {
        if (!vivant || !reponse.ok) return;
        const corps = (await reponse.json()) as { proprietaire?: boolean };
        setProprietaire(corps.proprietaire === true);
      })
      .catch(() => {
        /* Sans reponse, l'entree n'apparait pas. */
      });

    return () => {
      vivant = false;
    };
  }, []);

  const onglets = ONGLETS.filter((onglet) => {
    if (onglet.proprietaire) return proprietaire;
    return !onglet.roles || onglet.roles.some((role) => principal.roles.includes(role));
  });

  return (
    <nav
      aria-label="Sections de la console"
      className="flex h-full flex-col gap-1 border-r border-bordure bg-surface px-3 py-4"
    >
      <div className="px-2 pb-4">
        <Marque />
      </div>

      <ul className="flex flex-col gap-0.5">
        {onglets.map((onglet) => {
          const actif = chemin.startsWith(onglet.href);
          return (
            <li key={onglet.href}>
              <Link
                href={onglet.href}
                aria-current={actif ? 'page' : undefined}
                // LA SECTION COURANTE EST TEINTEE, PAS CREUSEE. L'ancien style
                // l'enfoncait — c'etait son seul moyen de dire « deja choisi ».
                // Un style plat n'a rien a enfoncer : le signal devient un aplat
                // doux et un filet vertical, qui se lisent sans relief.
                className={`flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition ${
                  actif
                    ? 'border-l-2 border-marque bg-marque-doux text-marque'
                    : 'border-l-2 border-transparent text-encre-2 hover:bg-creux hover:text-encre'
                }`}
              >
                <svg
                  aria-hidden
                  viewBox="0 0 24 24"
                  className="h-5 w-5 shrink-0"
                  fill="currentColor"
                >
                  <path d={onglet.trace} />
                </svg>
                {onglet.libelle}
              </Link>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}

/**
 * La barre du haut : qui est connecte, et par ou sortir.
 *
 * PAS DE RECHERCHE NI DE CLOCHE, ET C'EST DELIBERE. La maquette en montre deux ;
 * aucune des deux n'a de service derriere elle — il n'existe ni route de
 * recherche transverse, ni file de notifications pour le back-office. Les
 * dessiner donnerait deux boutons qui ne font rien, ce qui est pire qu'une barre
 * plus sobre.
 */
export function Entete({ principal }: { principal: Principal }) {
  const routeur = useRouter();

  async function seDeconnecter() {
    await fetch('/api/session/deconnexion', { method: 'POST' });
    routeur.replace('/connexion');
    routeur.refresh();
  }

  const initiales = principal.displayName
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((mot) => mot[0]?.toUpperCase() ?? '')
    .join('');

  return (
    <header className="flex items-center justify-end gap-3 border-b border-bordure bg-surface px-5 py-3">
      <span
        aria-hidden
        className="grid h-9 w-9 place-items-center rounded-full bg-marque-doux text-sm font-semibold text-marque"
      >
        {initiales}
      </span>

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
    </header>
  );
}

export function Marque({ grand = false }: { grand?: boolean }) {
  return (
    <span className="flex items-center gap-2.5">
      <span
        aria-hidden
        className={`grid place-items-center rounded-xl bg-marque text-white ${grand ? 'h-11 w-11' : 'h-9 w-9'}`}
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
