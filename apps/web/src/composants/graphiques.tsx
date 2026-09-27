'use client';

import { useId, useState } from 'react';

/* Specifications communes aux marques :
   - extremite arrondie de 4 px du cote de la valeur, jamais du cote de la
     ligne de base : un pied arrondi decolle la barre de son zero et fausse la
     lecture ;
   - 2 px de fond entre deux barres voisines ;
   - grille et axes en retrait, la donnee devant ;
   - survol partout : un graphique HTML est interactif, s'en priver le reduit
     a une image. */

const RAYON = 4;

function cheminBarre(x: number, y: number, largeur: number, hauteur: number): string {
  const r = Math.min(RAYON, largeur / 2, Math.max(hauteur, 0));
  const bas = y + hauteur;
  if (hauteur <= 0) return '';
  return [
    `M ${x} ${bas}`,
    `L ${x} ${y + r}`,
    `Q ${x} ${y} ${x + r} ${y}`,
    `L ${x + largeur - r} ${y}`,
    `Q ${x + largeur} ${y} ${x + largeur} ${y + r}`,
    `L ${x + largeur} ${bas}`,
    'Z',
  ].join(' ');
}

function graduations(max: number): number[] {
  if (max <= 0) return [0];
  const pas = Math.pow(10, Math.floor(Math.log10(max)));
  const arrondi = Math.ceil(max / pas) * pas;
  return [0, arrondi / 4, arrondi / 2, (arrondi * 3) / 4, arrondi];
}

export type Point = { libelle: string; valeur: number };

export function BarresVerticales({
  donnees,
  couleur = 'var(--color-serie-1)',
  unite = '',
  hauteur = 240,
}: {
  donnees: Point[];
  couleur?: string;
  unite?: string;
  hauteur?: number;
}) {
  const [survol, setSurvol] = useState<number | null>(null);
  const identifiant = useId();

  const largeur = 720;
  const margeGauche = 44;
  const margeBasse = 26;
  const margeHaute = 12;
  const traceHauteur = hauteur - margeBasse - margeHaute;
  const traceLargeur = largeur - margeGauche - 8;

  const maximum = Math.max(...donnees.map((d) => d.valeur), 0);
  const ticks = graduations(maximum);
  const plafond = ticks[ticks.length - 1] || 1;

  const pas = traceLargeur / Math.max(donnees.length, 1);
  const largeurBarre = Math.max(pas - 10, 4);

  return (
    <div className="relative">
      <svg
        viewBox={`0 0 ${largeur} ${hauteur}`}
        className="w-full"
        role="img"
        aria-labelledby={`${identifiant}-titre`}
      >
        <title id={`${identifiant}-titre`}>
          Histogramme de {donnees.length} valeurs, maximum {maximum} {unite}
        </title>

        {ticks.map((tick) => {
          const y = margeHaute + traceHauteur - (tick / plafond) * traceHauteur;
          return (
            <g key={tick}>
              <line
                x1={margeGauche}
                x2={largeur - 8}
                y1={y}
                y2={y}
                stroke="var(--color-grille)"
                strokeWidth={1}
              />
              <text
                x={margeGauche - 10}
                y={y + 4}
                textAnchor="end"
                className="chiffres-tabulaires"
                fontSize={11}
                fill="var(--color-encre-3)"
              >
                {Math.round(tick)}
              </text>
            </g>
          );
        })}

        {donnees.map((point, index) => {
          const h = plafond ? (point.valeur / plafond) * traceHauteur : 0;
          const x = margeGauche + index * pas + (pas - largeurBarre) / 2;
          const y = margeHaute + traceHauteur - h;

          return (
            <g key={point.libelle}>
              <path
                d={cheminBarre(x, y, largeurBarre, h)}
                fill={couleur}
                opacity={survol === null || survol === index ? 1 : 0.45}
              />
              {/* Cible de survol plus large que la barre : viser une colonne de
                  8 px a la souris est penible, et impossible au doigt. */}
              <rect
                x={margeGauche + index * pas}
                y={margeHaute}
                width={pas}
                height={traceHauteur}
                fill="transparent"
                onMouseEnter={() => setSurvol(index)}
                onMouseLeave={() => setSurvol(null)}
              />
              <text
                x={margeGauche + index * pas + pas / 2}
                y={hauteur - 8}
                textAnchor="middle"
                fontSize={11}
                fill="var(--color-encre-3)"
              >
                {point.libelle}
              </text>
            </g>
          );
        })}
      </svg>

      {survol !== null ? (
        <div
          className="relief pointer-events-none absolute top-2 rounded-xl px-3 py-2 text-xs"
          style={{
            left: `${((margeGauche + survol * pas + pas / 2) / largeur) * 100}%`,
            transform: 'translateX(-50%)',
          }}
        >
          <span className="block font-medium text-encre">{donnees[survol].libelle}</span>
          <span className="chiffres-tabulaires text-encre-2">
            {donnees[survol].valeur.toLocaleString('fr-FR')} {unite}
          </span>
        </div>
      ) : null}
    </div>
  );
}

export type Part = { libelle: string; valeur: number; couleur: string };

export function Anneau({
  parts,
  total,
  legende,
  taille = 180,
}: {
  parts: Part[];
  total: number;
  legende: string;
  taille?: number;
}) {
  const [survol, setSurvol] = useState<number | null>(null);

  const rayon = taille / 2 - 14;
  const circonference = 2 * Math.PI * rayon;
  const somme = total || parts.reduce((acc, p) => acc + p.valeur, 0) || 1;

  let cumul = 0;

  return (
    <svg viewBox={`0 0 ${taille} ${taille}`} width={taille} height={taille} role="img">
      <title>{legende}</title>
      <g transform={`translate(${taille / 2} ${taille / 2}) rotate(-90)`}>
        {parts.map((part, index) => {
          const fraction = part.valeur / somme;
          // 2 px de fond entre deux segments : sans ce trait, deux couleurs
          // voisines se touchent et la frontiere devient une illusion.
          const longueur = Math.max(fraction * circonference - 2, 0);
          const decalage = -cumul * circonference;
          cumul += fraction;

          return (
            <circle
              key={part.libelle}
              r={rayon}
              fill="none"
              stroke={part.couleur}
              strokeWidth={survol === index ? 20 : 16}
              strokeDasharray={`${longueur} ${circonference}`}
              strokeDashoffset={decalage}
              onMouseEnter={() => setSurvol(index)}
              onMouseLeave={() => setSurvol(null)}
            >
              <title>
                {part.libelle} : {part.valeur.toLocaleString('fr-FR')}
              </title>
            </circle>
          );
        })}
      </g>
      <text
        x={taille / 2}
        y={taille / 2 - 2}
        textAnchor="middle"
        fontSize={22}
        fontWeight={600}
        fill="var(--color-encre)"
      >
        {somme.toLocaleString('fr-FR')}
      </text>
      <text
        x={taille / 2}
        y={taille / 2 + 16}
        textAnchor="middle"
        fontSize={10}
        letterSpacing={1}
        fill="var(--color-encre-3)"
      >
        {legende.toUpperCase()}
      </text>
    </svg>
  );
}

export function BarresHorizontales({ parts, total }: { parts: Part[]; total: number }) {
  const somme = total || parts.reduce((acc, p) => acc + p.valeur, 0) || 1;

  return (
    <ul className="space-y-3">
      {parts.map((part) => {
        const fraction = part.valeur / somme;
        return (
          <li key={part.libelle}>
            <div className="mb-1.5 flex items-baseline justify-between gap-3 text-sm">
              <span className="flex items-center gap-2 text-encre-2">
                <span
                  aria-hidden
                  className="h-2.5 w-2.5 rounded-full"
                  style={{ backgroundColor: part.couleur }}
                />
                {part.libelle}
              </span>
              {/* Valeur ecrite, pas seulement coloree : deux des quatre teintes
                  de serie passent sous 3:1 face au blanc. */}
              <span className="chiffres-tabulaires font-medium text-encre">
                {part.valeur.toLocaleString('fr-FR')}
                <span className="ml-2 text-encre-3">{Math.round(fraction * 100)} %</span>
              </span>
            </div>
            <div className="creux-doux h-2 overflow-hidden rounded-full">
              <div
                className="h-full rounded-full"
                style={{ width: `${Math.max(fraction * 100, 1)}%`, backgroundColor: part.couleur }}
              />
            </div>
          </li>
        );
      })}
    </ul>
  );
}

export function Courbe({
  valeurs,
  couleur = 'var(--color-serie-1)',
  hauteur = 64,
}: {
  valeurs: number[];
  couleur?: string;
  hauteur?: number;
}) {
  if (valeurs.length < 2) return null;

  const largeur = 260;
  const max = Math.max(...valeurs);
  const min = Math.min(...valeurs);
  const amplitude = max - min || 1;

  const points = valeurs.map((valeur, index) => {
    const x = (index / (valeurs.length - 1)) * largeur;
    const y = hauteur - ((valeur - min) / amplitude) * (hauteur - 8) - 4;
    return `${x},${y}`;
  });

  return (
    <svg viewBox={`0 0 ${largeur} ${hauteur}`} className="w-full" role="img">
      <title>Tendance sur {valeurs.length} points</title>
      <polyline
        points={points.join(' ')}
        fill="none"
        stroke={couleur}
        strokeWidth={2}
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}
