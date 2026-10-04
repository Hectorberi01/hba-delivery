'use client';

import Link from 'next/link';
import { Bouton, Carte, Chargement, Erreur, Pastille, TitreDeCarte, Vide } from '@/composants/base';
import { montant } from '@/lib/agregats';
import { depuis, nombre, xof } from '@/lib/format';
import { COULEUR_TON, lireStatut, STATUTS } from '@/lib/statuts';
import { useRessource } from '@/lib/client/ressource';
import type { PageDeCourses } from '@/lib/types';

/**
 * Les courses du commercant.
 *
 * LE PERIMETRE N'EST PAS POSE ICI. La route ne porte aucun identifiant : c'est
 * Delivery qui borne la liste au merchant_id du jeton. Le navigateur ne peut
 * donc pas demander les courses d'un autre commercant, meme en forgeant
 * l'appel.
 */
export default function PageDesCourses() {
  const courses = useRessource<PageDeCourses>('merchant/v1/deliveries?pageSize=25');
  const lignes = courses.donnees?.deliveries ?? [];

  const enCours = lignes.filter((course) => {
    const cle = lireStatut(course.status);
    return cle !== 'livree' && cle !== 'annulee' && cle !== 'echouee';
  }).length;

  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-lg font-semibold text-encre">Mes courses</h1>
          <p className="text-sm text-encre-3">
            {courses.donnees
              ? `${nombre(enCours)} en cours sur ${nombre(lignes.length)} affichées`
              : 'Les vingt-cinq dernières'}
          </p>
        </div>

        <Link href="/portail/nouvelle">
          <Bouton>Commander une course</Bouton>
        </Link>
      </div>

      <Carte className="overflow-hidden pb-2">
        <TitreDeCarte titre="Dernières courses" sous="La plus récente en premier" />

        {courses.chargement ? <Chargement quoi="de vos courses" /> : null}
        {courses.erreur ? <Erreur message={courses.erreur} /> : null}
        {!courses.chargement && !courses.erreur && lignes.length === 0 ? (
          <Vide message="Aucune course pour le moment. « Commander une course » pour la première." />
        ) : null}

        {lignes.length ? (
          <ul className="mt-4 divide-y divide-bordure">
            {lignes.map((course) => {
              const statut = STATUTS[lireStatut(course.status)];

              return (
                <li key={course.id} className="px-5 py-3.5">
                  <div className="flex flex-wrap items-baseline justify-between gap-3">
                    <span className="font-mono text-[13px] text-encre-2">
                      {course.reference || course.id}
                    </span>
                    <span className="chiffres-tabulaires text-sm font-medium text-encre">
                      {xof(montant(course))}
                    </span>
                  </div>

                  <div className="mt-1 flex flex-wrap items-baseline justify-between gap-3">
                    <Pastille couleur={COULEUR_TON[statut.ton]} libelle={statut.libelle} />
                    <span className="text-xs text-encre-3">{depuis(course.createdAt)}</span>
                  </div>

                  <p className="mt-1 truncate text-sm text-encre-2">
                    {course.dropoff?.landmark || 'Destination sans repère'}
                    {course.recipientName ? ` · ${course.recipientName}` : ''}
                  </p>
                </li>
              );
            })}
          </ul>
        ) : null}
      </Carte>
    </>
  );
}
