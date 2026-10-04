import type { ReactNode } from 'react';

export function Carte({
  children,
  className = '',
}: {
  children: ReactNode;
  className?: string;
}) {
  return (
    <section
      className={`relief rounded-[var(--radius-carte)] ${className}`}
    >
      {children}
    </section>
  );
}

export function TitreDeCarte({ titre, sous, action }: { titre: string; sous?: string; action?: ReactNode }) {
  return (
    <header className="flex items-start justify-between gap-4 px-5 pt-5">
      <div>
        <h2 className="text-base font-semibold text-encre">{titre}</h2>
        {sous ? <p className="mt-0.5 text-sm text-encre-3">{sous}</p> : null}
      </div>
      {action}
    </header>
  );
}

/** Pastille de statut : couleur ET libelle, toujours les deux. */
export function Pastille({ couleur, libelle }: { couleur: string; libelle: string }) {
  return (
    <span className="inline-flex items-center gap-2 text-sm text-encre-2">
      <span
        aria-hidden
        className="h-2.5 w-2.5 shrink-0 rounded-full"
        style={{ backgroundColor: couleur }}
      />
      {libelle}
    </span>
  );
}

/**
 * Etiquette de statut.
 *
 * LA COULEUR EST DANS LA PASTILLE, LE LIBELLE EST EN ENCRE. L'ancienne
 * version ecrivait le texte dans la couleur du statut sur un aplat de cette
 * couleur a 12 % melee de blanc. Deux problemes que le fond argile rend
 * visibles : l'aplat blanchi apparait comme une tache claire posee sur la
 * matiere, et plusieurs statuts — jaune, orange clair — ne tenaient pas
 * 4.5:1 en texte. La couleur code toujours la donnee, par le point ; le
 * libelle, lui, se lit.
 */
export function Badge({ couleur, children }: { couleur: string; children: ReactNode }) {
  return (
    <span className="creux-doux inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium text-encre-2">
      <span aria-hidden className="h-1.5 w-1.5 rounded-full" style={{ backgroundColor: couleur }} />
      {children}
    </span>
  );
}

export function Bouton({
  children,
  variante = 'principal',
  ...reste
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { variante?: 'principal' | 'discret' | 'danger' }) {
  // L'ACTION PRINCIPALE EST UN APLAT DE COULEUR, LE SECONDAIRE UNE SURFACE
  // BORDEE. La raison a change avec le style sans que le resultat bouge : le
  // neomorphisme noyait l'action evidente sous un relief general, un style plat
  // la noierait sous des bordures toutes semblables.
  //
  // PLUS D'OMBRE PORTEE SUR LES APLATS. Elle existait pour poser le bouton sur
  // l'argile ; sur un fond blanc elle ne pose rien, elle salit.
  const styles: Record<string, string> = {
    principal: 'pressable-plein bg-marque text-white hover:bg-marque-fonce',
    discret: 'relief-doux pressable text-encre-2 hover:text-encre',
    danger: 'pressable-plein bg-critique text-white hover:brightness-95',
  };

  return (
    <button
      {...reste}
      className={`inline-flex items-center justify-center gap-2 rounded-xl px-4 py-2.5 text-sm font-medium disabled:cursor-not-allowed disabled:opacity-50 disabled:shadow-none ${styles[variante]} ${reste.className ?? ''}`}
    >
      {children}
    </button>
  );
}

export function Champ({
  libelle,
  aide,
  ...reste
}: React.InputHTMLAttributes<HTMLInputElement> & { libelle: string; aide?: string }) {
  return (
    <label className="block">
      <span className="mb-1.5 block text-sm font-medium text-encre-2">{libelle}</span>
      <input
        {...reste}
        className="creux w-full rounded-xl border-0 px-4 py-3 text-sm text-encre outline-none placeholder:text-encre-3"
      />
      {aide ? <span className="mt-1 block text-xs text-encre-3">{aide}</span> : null}
    </label>
  );
}

/**
 * Tuile de chiffre. Pas de graphique : une valeur unique se lit mieux en
 * grand qu'en barre d'un pixel.
 *
 * « detail » n'est pas decoratif — c'est lui qui dit sur quoi porte le
 * chiffre. Un nombre sans perimetre finit toujours par etre lu comme un
 * total d'entreprise.
 */
export function CarteStat({
  titre,
  valeur,
  detail,
  ecart,
}: {
  titre: string;
  valeur: string;
  detail: string;

  /**
   * Variation par rapport a la periode precedente, en pourcentage. ABSENTE
   * QUAND LA COMPARAISON N'A PAS DE SENS, et c'est le cas le plus frequent au
   * pilote : periode precedente vide, ou trop maigre pour qu'un rapport veuille
   * dire quelque chose. Voir le calcul dans l'ecran, qui decide.
   */
  ecart?: number;
}) {
  return (
    <Carte className="px-5 py-4">
      <p className="text-sm text-encre-2">{titre}</p>
      <p className="mt-2 text-[28px] font-semibold leading-none tracking-tight text-encre">{valeur}</p>

      {ecart === undefined ? (
        <p className="mt-2 text-xs text-encre-3">{detail}</p>
      ) : (
        <div className="mt-2 flex items-baseline gap-1.5">
          {/* LA FLECHE DOUBLE LA COULEUR, ET CE N'EST PAS DECORATIF : un ecart
              code uniquement par du vert ou du rouge disparait pour qui ne
              distingue pas les deux. */}
          <span
            className={`text-xs font-semibold ${ecart >= 0 ? 'text-bon' : 'text-critique'}`}
          >
            {ecart >= 0 ? '↑' : '↓'} {ecart >= 0 ? '+' : '−'}
            {Math.abs(ecart)} %
          </span>
          <span className="text-xs text-encre-3">vs période précédente</span>
        </div>
      )}
    </Carte>
  );
}

/** Initiales, faute de photo : le service Driver n'en porte aucune. */
export function Initiales({ nom }: { nom: string }) {
  const lettres = nom
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((mot) => mot[0]?.toUpperCase() ?? '')
    .join('');

  return (
    <span
      aria-hidden
      className="creux grid h-10 w-10 shrink-0 place-items-center rounded-full text-sm font-semibold text-marque"
    >
      {lettres || '?'}
    </span>
  );
}

export function Chargement({ quoi }: { quoi: string }) {
  return (
    <p role="status" className="px-5 py-10 text-center text-sm text-encre-3">
      Chargement {quoi}…
    </p>
  );
}

export function Erreur({ message }: { message: string }) {
  return (
    <p role="alert" className="px-5 py-10 text-center text-sm text-critique">
      {message}
    </p>
  );
}

export function Vide({ message }: { message: string }) {
  return <p className="px-5 py-10 text-center text-sm text-encre-3">{message}</p>;
}

/**
 * CE QUE L'API NE SAIT PAS ENCORE DIRE.
 *
 * Plutot qu'un chiffre invente ou un graphique de demonstration, l'ecran
 * nomme la route qui manque. Une maquette remplie de fausses valeurs finit
 * toujours par etre lue comme une vraie mesure.
 */
export function LacuneApi({
  quoi,
  route,
  besoin,
}: {
  quoi: string;
  /** La route qui manque, telle qu'elle s'appellerait. */
  route?: string;
  /** Ce qui manque quand ce n'est PAS une route — une décision, un arbitrage. */
  besoin?: string;
}) {
  return (
    <div className="creux-doux rounded-xl px-5 py-6">
      <p className="text-sm font-medium text-encre-2">{quoi}</p>
      <p className="mt-1 text-sm text-encre-3">
        {route ? (
          <>
            Aucune route ne fournit cette donnée aujourd&apos;hui. Il faudrait{' '}
            <code className="creux-doux rounded px-1.5 py-0.5 text-[13px] text-encre-2">{route}</code>.
          </>
        ) : (
          // DEUX FORMES, PARCE QU'IL Y A DEUX MANQUES DIFFERENTS. « Il
          // faudrait GET /x » se lit bien ; « Il faudrait "une decision : ou
          // vivent les objectifs ?" » entre balises code se lit comme un nom
          // de route absurde. Ce composant avait une seule forme et j'y ai
          // fait passer de la prose : c'est ce que corrige "besoin".
          besoin
        )}
      </p>
    </div>
  );
}

