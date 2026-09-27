'use client';

export const PERIODES = [
  { cle: '7d', libelle: '7 jours' },
  { cle: '30d', libelle: '30 jours' },
  { cle: '90d', libelle: '90 jours' },
  { cle: 'month', libelle: 'Mois en cours' },
] as const;

export type ClePeriode = (typeof PERIODES)[number]['cle'];

/**
 * Choix de la période.
 *
 * ON ENVOIE UN NOM, PAS DEUX DATES. Un poste à Paris et un poste à Cotonou ne
 * commencent pas la journée au même instant : « les sept derniers jours »
 * calculés ici couperait la première barre en deux, et la coupure suivrait le
 * voyageur. C'est la passerelle qui traduit « 7d » en instants, dans le
 * fuseau métier.
 */
export function ChoixPeriode({
  valeur,
  onChange,
}: {
  valeur: ClePeriode;
  onChange: (cle: ClePeriode) => void;
}) {
  return (
    <div
      role="group"
      aria-label="Période"
      className="creux inline-flex rounded-2xl p-1.5"
    >
      {PERIODES.map((periode) => (
        <button
          key={periode.cle}
          type="button"
          onClick={() => onChange(periode.cle)}
          aria-pressed={valeur === periode.cle}
          // CREUX AUTOUR, RELIEF DEDANS. Le groupe est enfonce dans la
          // page et le segment choisi ressort : c'est la seule combinaison
          // ou l'oeil lit « un parmi plusieurs » sans avoir besoin d'une
          // couleur de marque en plus.
          className={`rounded-xl px-3.5 py-1.5 text-sm font-medium transition ${
            valeur === periode.cle
              ? 'relief-doux text-marque'
              : 'text-encre-2 hover:text-encre'
          }`}
        >
          {periode.libelle}
        </button>
      ))}
    </div>
  );
}
