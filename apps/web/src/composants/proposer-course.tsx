'use client';

import { useMemo, useState } from 'react';
import { Bouton } from '@/composants/base';
import { proposerCourse } from '@/lib/client/offres';
import { useRessource } from '@/lib/client/ressource';
import { depuis } from '@/lib/format';
import { distanceMetres, metres } from '@/lib/geo';
import { estDisponible, lireOperationnel } from '@/lib/statuts';
import type { Course, PagePositions, PositionLivreur } from '@/lib/types';

type Candidat = {
  position: PositionLivreur;
  distance: number | null;
};

const MAX_AFFICHES = 8;

/**
 * Proposer la course a un livreur choisi a la main.
 *
 * CE QUE CET ENCART FAIT, ET CE QU'IL NE FAIT PAS. Il envoie une OFFRE, avec
 * le meme delai et le meme droit de refus que celles du moteur. Il n'affecte
 * personne : imposer une course demanderait d'abord de decider ce qui se
 * passe quand le livreur n'est pas d'accord, et cette question n'est pas
 * tranchee.
 *
 * LA LISTE EST TRIEE PAR DISTANCE AU RETRAIT, pas par nom ni par anciennete.
 * C'est la seule information qui aide a choisir : tout le reste — qui
 * travaille bien, qui connaît le quartier — n'existe nulle part dans le
 * domaine, et le classer sur une note inventee serait pire que de ne pas
 * classer.
 */
export function ProposerLaCourse({
  course,
  apresAction,
}: {
  course: Course;
  apresAction: () => void;
}) {
  const retrait = course.pickup?.point;

  const { donnees, chargement, erreur, recharger } = useRessource<PagePositions>(
    'admin/v1/drivers/positions',
  );

  const [motif, setMotif] = useState('');
  const [envoi, setEnvoi] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [probleme, setProbleme] = useState<string | null>(null);

  const candidats = useMemo<Candidat[]>(() => {
    const positions = donnees?.positions ?? [];

    return positions
      .filter((position) => estDisponible(position.operationalStatus))
      .map((position) => ({ position, distance: distanceMetres(retrait, position) }))
      .sort((a, b) => {
        // UNE DISTANCE INCONNUE VA A LA FIN, jamais a zero : un livreur dont
        // on ignore l'ecart au retrait ne doit pas se retrouver en tete de
        // liste comme s'il etait sur place.
        if (a.distance === null) return 1;
        if (b.distance === null) return -1;
        return a.distance - b.distance;
      })
      .slice(0, MAX_AFFICHES);
  }, [donnees, retrait]);

  async function proposer(position: PositionLivreur) {
    setEnvoi(position.driverId);
    setMessage(null);
    setProbleme(null);

    const nom = position.displayName || position.driverId.slice(0, 8);
    const resultat = await proposerCourse(course.id, position.driverId, motif);

    if (resultat.ok) {
      setMessage(
        `Offre envoyée à ${nom}. Il a le même délai que les offres du moteur pour répondre, et reste libre de refuser.`,
      );
      setMotif('');
      apresAction();
    } else {
      // LE REFUS S'AFFICHE ICI, PAS DANS LE BANDEAU D'ERREUR. « Il vient de
      // partir en course » n'est pas une panne : c'est la reponse, et elle
      // demande de choisir quelqu'un d'autre, pas de recommencer.
      setProbleme(resultat.message);
    }

    // DANS LES DEUX CAS ON RELIT LES POSITIONS : celui qui vient d'accepter
    // n'est plus disponible, et le laisser dans la liste inviterait a
    // recommencer sur lui.
    recharger();
    setEnvoi(null);
  }

  return (
    <div className="border-t border-bordure px-5 py-5">
      <h3 className="text-sm font-semibold text-encre">Proposer à un livreur</h3>
      <p className="mt-1 text-sm text-encre-3">
        Le moteur cherche par cercles de 2, 4 puis 6 km. Si vous savez que quelqu&apos;un est
        juste à côté, désignez-le : il reçoit une offre ordinaire, qu&apos;il peut refuser.
      </p>

      {!retrait ? (
        <p className="mt-4 rounded-lg bg-plan px-3 py-2 text-sm text-encre-3">
          Cette course n&apos;a pas de point de retrait exploitable : impossible de trier les
          livreurs par distance.
        </p>
      ) : null}

      <label className="mt-4 block">
        <span className="mb-1.5 block text-sm font-medium text-encre-2">
          Motif <span className="font-normal text-encre-3">— facultatif, il est journalisé</span>
        </span>
        <input
          type="text"
          value={motif}
          onChange={(e) => setMotif(e.target.value)}
          className="creux w-full rounded-xl border-0 px-4 py-3 text-sm text-encre outline-none placeholder:text-encre-3"
          placeholder="Ex. : le client a rappelé, il est pressé."
        />
      </label>

      <div className="mt-4">
        <div className="flex items-baseline justify-between">
          <p className="text-sm font-medium text-encre-2">Livreurs disponibles, du plus proche</p>
          <button
            type="button"
            onClick={recharger}
            className="text-sm text-marque underline-offset-2 hover:underline"
          >
            Rafraîchir
          </button>
        </div>

        {chargement ? <p className="mt-3 text-sm text-encre-3">Lecture des positions…</p> : null}
        {erreur ? (
          <p role="alert" className="mt-3 text-sm text-critique">
            {erreur}
          </p>
        ) : null}

        {!chargement && !erreur && !candidats.length ? (
          <p className="mt-3 rounded-lg bg-plan px-3 py-2 text-sm text-encre-3">
            Aucun livreur disponible n&apos;a donné sa position récemment. Rien à proposer pour
            l&apos;instant — le moteur continue de chercher.
          </p>
        ) : null}

        <ul className="mt-3 space-y-2">
          {candidats.map(({ position, distance }) => {
            const etat = lireOperationnel(position.operationalStatus);
            const nom = position.displayName || position.driverId.slice(0, 8);

            return (
              <li
                key={position.driverId}
                className="creux-doux flex items-center justify-between gap-3 rounded-xl px-3.5 py-2.5"
              >
                <div className="min-w-0">
                  <p className="truncate text-sm font-medium text-encre">{nom}</p>
                  <p className="text-xs text-encre-3">
                    {retrait ? metres(distance) : etat.libelle} · vu {depuis(position.seenAt)}
                  </p>
                </div>

                <Bouton
                  disabled={envoi !== null}
                  onClick={() => proposer(position)}
                  className="shrink-0"
                >
                  {envoi === position.driverId ? 'Envoi…' : 'Proposer'}
                </Bouton>
              </li>
            );
          })}
        </ul>
      </div>

      {message ? <p className="mt-3 text-sm text-bon">{message}</p> : null}
      {probleme ? (
        <p role="alert" className="mt-3 text-sm text-critique">
          {probleme}
        </p>
      ) : null}
    </div>
  );
}
