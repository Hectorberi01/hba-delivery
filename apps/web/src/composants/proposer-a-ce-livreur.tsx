'use client';

import { useMemo, useState } from 'react';
import { Bouton } from '@/composants/base';
import { proposerCourse } from '@/lib/client/offres';
import { useRessource } from '@/lib/client/ressource';
import { depuis, xof } from '@/lib/format';
import { montant } from '@/lib/agregats';
import { distanceMetres, metres } from '@/lib/geo';
import { estDisponible, lireStatut } from '@/lib/statuts';
import type { Course, Livreur, PagePositions } from '@/lib/types';

const MAX_AFFICHEES = 6;

/**
 * Le geste inverse de celui de la fiche de course : partir d'un livreur et
 * lui proposer une des courses qui cherchent preneur.
 *
 * POURQUOI LES DEUX SENS. Ce sont deux moments d'exploitation differents, et
 * aucun des deux ne remplace l'autre. Depuis une course : « celle-ci traîne,
 * qui est à côté ? ». Depuis la carte : « il vient de se libérer au marché
 * Dantokpa, qu'est-ce qu'il y a pour lui ? ». Le second est celui qu'on fait
 * en regardant l'écran des positions, et c'est pour cela qu'il vit ici.
 *
 * LA LISTE VIENT DE LA PAGE DEJA CHARGEE, comme les compteurs de cet ecran.
 * ListDeliveries n'a pas de filtre de statut : demander « les courses en
 * recherche » n'est pas possible aujourd'hui, et les deux cents dernieres
 * courses contiennent en pratique toutes celles qui cherchent encore. Le
 * jour ou ce ne sera plus vrai, c'est un parametre a ajouter au contrat, pas
 * un ecran a reecrire.
 */
export function CoursesAProposer({
  livreur,
  courses,
  apresAction,
}: {
  livreur: Livreur;
  courses: Course[];
  apresAction: () => void;
}) {
  const disponible = estDisponible(livreur.operationalStatus);

  // LES POSITIONS NE SE LISENT QUE S'IL EST DISPONIBLE. Inutile de demander
  // ou se trouve quelqu'un a qui on ne peut rien proposer.
  const positions = useRessource<PagePositions>(
    disponible ? 'admin/v1/drivers/positions' : null,
  );

  const [motif, setMotif] = useState('');
  const [envoi, setEnvoi] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [probleme, setProbleme] = useState<string | null>(null);

  const ici = useMemo(
    () => (positions.donnees?.positions ?? []).find((p) => p.driverId === livreur.id) ?? null,
    [positions.donnees, livreur.id],
  );

  const candidates = useMemo(() => {
    return courses
      .filter((course) => lireStatut(course.status) === 'recherche_livreur')
      .map((course) => ({ course, distance: distanceMetres(ici, course.pickup?.point) }))
      .sort((a, b) => {
        // SANS POSITION CONNUE, ON NE TRIE PAS PAR DISTANCE, on garde
        // l'ordre rendu par le service — les plus recentes d'abord. Un tri
        // sur une distance inventee serait pire qu'aucun tri.
        if (a.distance === null) return b.distance === null ? 0 : 1;
        if (b.distance === null) return -1;
        return a.distance - b.distance;
      })
      .slice(0, MAX_AFFICHEES);
  }, [courses, ici]);

  async function proposer(course: Course) {
    setEnvoi(course.id);
    setMessage(null);
    setProbleme(null);

    const resultat = await proposerCourse(course.id, livreur.id, motif);

    if (resultat.ok) {
      const reference = course.reference ?? course.id.slice(0, 8);
      setMessage(
        `Course ${reference} proposée à ${livreur.displayName || 'ce livreur'}. Il reste libre de refuser.`,
      );
      setMotif('');
      apresAction();
      positions.recharger();
    } else {
      setProbleme(resultat.message);
    }

    setEnvoi(null);
  }

  return (
    <div className="creux-doux rounded-xl px-5 py-4">
      <h3 className="text-sm font-semibold text-encre">Lui proposer une course</h3>

      {!disponible ? (
        <p className="mt-1 text-sm text-encre-3">
          Il n&apos;est pas disponible en ce moment — hors ligne, déjà sollicité, ou en course.
          Une offre serait refusée par le service.
        </p>
      ) : (
        <>
          <p className="mt-1 text-sm text-encre-3">
            Les courses qui cherchent encore un livreur, de la plus proche de lui. Il reçoit une
            offre ordinaire, qu&apos;il peut refuser.
          </p>

          {!ici && !positions.chargement ? (
            <p className="mt-3 text-sm text-encre-3">
              Sa position n&apos;est pas connue : la liste reste dans l&apos;ordre du service, et
              l&apos;offre sera refusée tant que son téléphone n&apos;aura pas donné signe de vie.
            </p>
          ) : null}

          <label className="mt-3 block">
            <span className="mb-1.5 block text-sm font-medium text-encre-2">
              Motif <span className="font-normal text-encre-3">— facultatif, il est journalisé</span>
            </span>
            <input
              type="text"
              value={motif}
              onChange={(e) => setMotif(e.target.value)}
              className="creux w-full rounded-xl border-0 px-4 py-2.5 text-sm text-encre outline-none placeholder:text-encre-3"
              placeholder="Ex. : il vient de se libérer juste à côté."
            />
          </label>

          {positions.chargement ? (
            <p className="mt-3 text-sm text-encre-3">Lecture de sa position…</p>
          ) : null}

          {!candidates.length ? (
            <p className="mt-3 text-sm text-encre-3">
              Aucune course en recherche de livreur parmi celles chargées.
            </p>
          ) : (
            <ul className="mt-3 space-y-2">
              {candidates.map(({ course, distance }) => (
                <li
                  key={course.id}
                  className="relief-doux flex items-center justify-between gap-3 rounded-xl px-3.5 py-2.5"
                >
                  <div className="min-w-0">
                    <p className="truncate text-sm font-medium text-encre">
                      {course.reference ?? course.id.slice(0, 8)}
                      <span className="ml-2 font-normal text-encre-3">{xof(montant(course))}</span>
                    </p>
                    <p className="truncate text-xs text-encre-3">
                      {ici ? `${metres(distance)} du retrait · ` : ''}
                      {course.pickup?.landmark || 'retrait sans repère'} · créée{' '}
                      {depuis(course.createdAt)}
                    </p>
                  </div>

                  <Bouton
                    disabled={envoi !== null}
                    onClick={() => proposer(course)}
                    className="shrink-0"
                  >
                    {envoi === course.id ? 'Envoi…' : 'Proposer'}
                  </Bouton>
                </li>
              ))}
            </ul>
          )}
        </>
      )}

      {message ? <p className="mt-3 text-sm text-bon">{message}</p> : null}
      {probleme ? (
        <p role="alert" className="mt-3 text-sm text-critique">
          {probleme}
        </p>
      ) : null}
    </div>
  );
}
