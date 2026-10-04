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
      className="creux inline-flex rounded-xl p-1"
    >
      {PERIODES.map((periode) => (
        <button
          key={periode.cle}
          type="button"
          onClick={() => onChange(periode.cle)}
          aria-pressed={valeur === periode.cle}
          // LE SEGMENT CHOISI EST UN APLAT DE MARQUE, et c'est un changement
          // de signal assume (2 octobre 2026). L'ancien style posait un groupe
          // creuse et faisait ressortir le segment en relief ; un style plat n'a
          // ni l'un ni l'autre. Reste la couleur — la seule chose qui, sans
          // relief, dise « un parmi plusieurs » d'un coup d'oeil.
          className={`rounded-lg px-3.5 py-1.5 text-sm font-medium transition ${
            valeur === periode.cle
              ? 'bg-marque text-white'
              : 'text-encre-2 hover:text-encre'
          }`}
        >
          {periode.libelle}
        </button>
      ))}
    </div>
  );
}
