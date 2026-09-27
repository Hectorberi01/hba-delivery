'use client';

import { useMemo, useState } from 'react';
import { Anneau, BarresHorizontales, BarresVerticales, type Part } from '@/composants/graphiques';
import {
  Carte,
  CarteStat,
  Chargement,
  Erreur,
  LacuneApi,
  TitreDeCarte,
} from '@/composants/base';
import { ChoixPeriode, type ClePeriode } from '@/composants/periode';
import { duree, fenetre as libelleFenetre, nombre, val, xof } from '@/lib/format';
import { COULEUR_TON, lireStatut, STATUTS } from '@/lib/statuts';
import { libelleSource } from '@/lib/agregats';
import { lireOperationnel, lireVerification, COULEUR_TON_LIVREUR } from '@/lib/statuts';
import { useRessource } from '@/lib/client/ressource';
import type { Indicateurs, StatsDispatch, StatsLivreurs } from '@/lib/types';

const SERIES = [
  'var(--color-serie-1)',
  'var(--color-serie-2)',
  'var(--color-serie-3)',
  'var(--color-serie-4)',
];

const RAMPE = ['var(--color-rampe-1)', 'var(--color-rampe-2)', 'var(--color-rampe-3)'];

export default function TableauDeBord() {
  const [periode, setPeriode] = useState<ClePeriode>('30d');

  // LE NAVIGATEUR NE CALCULE PLUS RIEN. Il nomme une période ; la passerelle
  // la traduit en instants dans le fuseau métier, la même pour les quatre
  // services (ADR 0019 et 0020).
  // Toutes les periodes proposees se lisent au jour ; la granularite reste
  // un parametre du contrat pour le jour ou l'on offrira la semaine ou le mois.
  const pas = 'day';

  const { donnees, chargement, erreur } = useRessource<Indicateurs>(
    `admin/v1/kpi?range=${periode}&granularity=${pas}`,
  );

  const courses = donnees?.deliveries ?? null;
  const paiements = donnees?.payments ?? null;
  const dispatch = donnees?.dispatch ?? null;
  const livreurs = donnees?.drivers ?? null;
  const absents = donnees?.unavailable ?? [];

  const partsStatut = useMemo<Part[]>(() => {
    if (!courses?.byStatus) return [];
    return courses.byStatus
      .map((t) => {
        const definition = STATUTS[lireStatut(t.status)];
        return {
          libelle: definition.libelle,
          valeur: val(t.count),
          couleur: COULEUR_TON[definition.ton],
        };
      })
      .sort((a, b) => b.valeur - a.valeur);
  }, [courses]);

  const partsSource = useMemo<Part[]>(() => {
    if (!courses?.bySource) return [];
    return courses.bySource
      .map((t, index) => ({
        libelle: libelleSource(t.source),
        valeur: val(t.count),
        couleur: SERIES[index % SERIES.length],
      }))
      .sort((a, b) => b.valeur - a.valeur);
  }, [courses]);

  const closes = val(courses?.closed);
  const livrees = val(courses?.delivered);

  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-lg font-semibold text-encre">Tableau de bord</h1>
          <p className="text-sm text-encre-3">
            {donnees?.window?.from && donnees.window.to
              ? libelleFenetre(donnees.window.from, donnees.window.to)
              : 'Période en cours de chargement'}
          </p>
        </div>
        <ChoixPeriode valeur={periode} onChange={setPeriode} />
      </div>

      {absents.length ? (
        // UN BLOC ABSENT SE DIT, IL NE S'AFFICHE PAS A ZERO. Un zéro se lirait
        // comme une mesure.
        <p
          role="status"
          className="rounded-xl border border-attention bg-[color-mix(in_srgb,var(--color-attention)_10%,white)] px-4 py-3 text-sm text-encre-2"
        >
          Indicateurs indisponibles : <strong>{absents.join(', ')}</strong>. Les blocs concernés
          sont vides — ce ne sont pas des zéros.
        </p>
      ) : null}

      {chargement ? (
        <Carte>
          <Chargement quoi="des indicateurs" />
        </Carte>
      ) : null}

      {erreur ? (
        <Carte>
          <Erreur message={erreur} />
        </Carte>
      ) : null}

      {!chargement && !erreur ? (
        <>
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <CarteStat
              titre="Courses"
              valeur={courses ? nombre(val(courses.total)) : '—'}
              detail="Créées sur la période"
            />
            <CarteStat
              titre="Livrées"
              valeur={courses ? nombre(livrees) : '—'}
              detail="Remise confirmée par OTP"
            />
            <CarteStat
              titre="Encaissé"
              valeur={paiements ? xof(val(paiements.collectedXof)) : '—'}
              detail="Reçu sur la période"
            />
            <CarteStat
              titre="Taux de réussite"
              valeur={closes ? `${Math.round((livrees / closes) * 100)} %` : '—'}
              detail={closes ? `Sur ${nombre(closes)} courses closes` : 'Aucune course close'}
            />
          </div>

          <div className="grid gap-4 xl:grid-cols-[1fr_360px]">
            <Carte>
              <TitreDeCarte titre="Courses créées" sous="Par jour, calendrier de Cotonou" />
              <div className="px-3 pb-4 pt-3">
                {courses?.createdSeries?.length ? (
                  <BarresVerticales
                    donnees={courses.createdSeries.map((p) => ({
                      libelle: jourCourt(p.key),
                      valeur: val(p.value),
                    }))}
                    unite="courses"
                  />
                ) : (
                  <Vide />
                )}
              </div>
            </Carte>

            <Carte>
              <TitreDeCarte titre="État des courses" sous="Répartition de la période" />
              <div className="flex flex-col items-center gap-5 px-5 pb-5 pt-4">
                {partsStatut.length ? (
                  <>
                    <Anneau
                      parts={partsStatut}
                      total={val(courses?.total)}
                      legende="courses"
                    />
                    <ul className="w-full space-y-2">
                      {partsStatut.map((part) => (
                        <li
                          key={part.libelle}
                          className="flex items-center justify-between gap-3 text-sm"
                        >
                          <span className="flex items-center gap-2 text-encre-2">
                            <span
                              aria-hidden
                              className="h-2.5 w-2.5 rounded-full"
                              style={{ backgroundColor: part.couleur }}
                            />
                            {part.libelle}
                          </span>
                          <span className="chiffres-tabulaires font-medium text-encre">
                            {nombre(part.valeur)}
                          </span>
                        </li>
                      ))}
                    </ul>
                  </>
                ) : (
                  <Vide />
                )}
              </div>
            </Carte>
          </div>

          <div className="grid gap-4 xl:grid-cols-[1fr_360px]">
            <Carte>
              <TitreDeCarte
                titre="Recette encaissée"
                sous="Par jour d'encaissement, pas par jour de commande"
              />
              <div className="px-3 pb-4 pt-3">
                {paiements?.collectedSeries?.length ? (
                  <BarresVerticales
                    donnees={paiements.collectedSeries.map((p) => ({
                      libelle: jourCourt(p.key),
                      valeur: val(p.value),
                    }))}
                    unite="F"
                  />
                ) : (
                  <Vide />
                )}
              </div>
            </Carte>

            <Carte>
              <TitreDeCarte titre="Origine des courses" sous="Qui donne l'ordre" />
              <div className="px-5 pb-5 pt-4">
                {partsSource.length ? (
                  <BarresHorizontales parts={partsSource} total={val(courses?.total)} />
                ) : (
                  <Vide />
                )}
              </div>
            </Carte>
          </div>

          <div className="grid gap-4 xl:grid-cols-2">
            <BlocAffectation dispatch={dispatch} />
            <BlocDelais courses={courses} dispatch={dispatch} />
          </div>

          <div className="grid gap-4 xl:grid-cols-2">
            <BlocLivreurs livreurs={livreurs} />

            <Carte>
              <TitreDeCarte titre="Ce qui manque encore" sous="Décisions, pas routes" />
              <div className="space-y-3 px-5 pb-5 pt-4">
                <LacuneApi
                  quoi="Objectifs mensuels — la barre « 78 % » n'a aucune source"
                  besoin="Ce n'est pas une route qui manque mais une décision : où vivent les objectifs, qui les fixe, et sur quelle période."
                />
                <div className="creux-doux rounded-xl px-5 py-4">
                  <p className="text-sm font-medium text-encre-2">
                    Paliers de service et zones
                  </p>
                  <p className="mt-1 text-sm text-encre-3">
                    Express, Standard, Premium n&apos;existent pas dans le domaine, et Driver
                    connaît des positions, pas des secteurs. Les afficher demanderait
                    d&apos;abord de les définir — c&apos;est une décision produit, pas un manque
                    technique.
                  </p>
                </div>
              </div>
            </Carte>
          </div>
        </>
      ) : null}
    </>
  );
}

function Vide() {
  return <p className="py-10 text-center text-sm text-encre-3">Aucune donnée sur la période.</p>;
}

/** « 2026-09-26 » → « 26/09 ». La clé vient du service, on ne la recalcule pas. */
function jourCourt(cle: string): string {
  const morceaux = cle.split('-');
  return morceaux.length === 3 ? `${morceaux[2]}/${morceaux[1]}` : cle;
}

function BlocAffectation({ dispatch }: { dispatch: StatsDispatch | null }) {
  const vagues = (dispatch?.acceptedByWave ?? []).map((v, index) => ({
    libelle: `Vague ${v.waveNumber ?? index + 1}`,
    valeur: val(v.count),
    couleur: RAMPE[Math.min((v.waveNumber ?? index + 1) - 1, RAMPE.length - 1)],
  }));

  const acceptees = vagues.reduce((total, v) => total + v.valeur, 0);

  return (
    <Carte>
      <TitreDeCarte titre="Affectation" sous="Le moteur, pas les livreurs" />
      <div className="space-y-4 px-5 pb-5 pt-4">
        {dispatch ? (
          <>
            <dl className="space-y-3 text-sm">
              <Mesure terme="Recherches ouvertes" valeur={nombre(val(dispatch.dispatches))} />
              <Mesure terme="Affectées" valeur={nombre(val(dispatch.assigned))} />
              <Mesure
                terme="Sans livreur"
                valeur={nombre(val(dispatch.exhausted))}
                contexte="toutes les vagues ont échoué"
              />
              <Mesure terme="Offres envoyées" valeur={nombre(val(dispatch.offersSent))} />
            </dl>

            {vagues.length ? (
              <div className="border-t border-bordure pt-4">
                <p className="mb-3 text-sm font-medium text-encre-2">
                  Vague qui a emporté la course
                </p>
                {/* LA RAMPE EST ORDONNEE, PAS CATEGORIELLE : les vagues vont
                    du plus proche au plus lointain. Trois teintes distinctes
                    diraient « trois choses differentes » au lieu de
                    « premiere, deuxieme, troisieme ». */}
                <BarresHorizontales parts={vagues} total={acceptees} />
                <p className="mt-3 text-sm text-encre-3">
                  Rayons actuels : 2, 4 et 6 km. Si presque tout se prend en vague 1, le premier
                  rayon suffit et les suivants font attendre pour rien.
                </p>
              </div>
            ) : null}
          </>
        ) : (
          <Vide />
        )}
      </div>
    </Carte>
  );
}

function BlocDelais({
  courses,
  dispatch,
}: {
  courses: Indicateurs['deliveries'];
  dispatch: StatsDispatch | null;
}) {
  return (
    <Carte>
      <TitreDeCarte titre="Délais moyens" sous="Chaque moyenne avec son effectif" />
      <div className="space-y-4 px-5 pb-5 pt-4">
        <div>
          <p className="mb-3 text-xs uppercase tracking-wide text-encre-3">
            Depuis la création de la course
          </p>
          <dl className="space-y-3 text-sm">
            <Delai
              terme="Jusqu'à l'affectation"
              secondes={courses?.avgSecondsToAssignment}
              effectif={courses?.assignmentSamples}
            />
            <Delai
              terme="Jusqu'à la collecte"
              secondes={courses?.avgSecondsToPickup}
              effectif={courses?.pickupSamples}
            />
            <Delai
              terme="Jusqu'à la livraison"
              secondes={courses?.avgSecondsToDelivery}
              effectif={courses?.deliverySamples}
            />
          </dl>
        </div>

        <div className="border-t border-bordure pt-4">
          {/* DEUX « DELAIS D'AFFECTATION » QUI NE MESURENT PAS LA MEME CHOSE :
              celui de Delivery part de la creation de la course, celui de
              Dispatch de l'ouverture de la recherche — apres le paiement.
              L'ecart entre les deux, c'est le temps de payer. */}
          <p className="mb-3 text-xs uppercase tracking-wide text-encre-3">
            Depuis l&apos;ouverture de la recherche
          </p>
          <dl className="space-y-3 text-sm">
            <Delai
              terme="Jusqu'à l'affectation"
              secondes={dispatch?.avgSecondsToAssignment}
              effectif={dispatch?.assignmentSamples}
            />
            <Delai
              terme="Réponse du livreur à une offre"
              secondes={dispatch?.avgSecondsToOfferResponse}
              effectif={dispatch?.offerResponseSamples}
            />
          </dl>
        </div>
      </div>
    </Carte>
  );
}

function BlocLivreurs({ livreurs }: { livreurs: StatsLivreurs | null }) {
  return (
    <Carte>
      <TitreDeCarte titre="Livreurs" sous="Effectifs à l'instant, flux sur la période" />
      <div className="space-y-4 px-5 pb-5 pt-4">
        {livreurs ? (
          <>
            <dl className="space-y-3 text-sm">
              <Mesure terme="Inscrits" valeur={nombre(val(livreurs.total))} />
              {(livreurs.byOperational ?? []).map((t) => (
                <div
                  key={String(t.status)}
                  className="flex items-center justify-between gap-3"
                >
                  <dt className="flex items-center gap-2 text-encre-2">
                    <span
                      aria-hidden
                      className="h-2.5 w-2.5 rounded-full"
                      style={{
                        backgroundColor:
                          COULEUR_TON_LIVREUR[lireOperationnel(t.status).ton],
                      }}
                    />
                    {lireOperationnel(t.status).libelle}
                  </dt>
                  <dd className="chiffres-tabulaires font-medium text-encre">
                    {nombre(val(t.count))}
                  </dd>
                </div>
              ))}
            </dl>

            <div className="border-t border-bordure pt-4">
              <dl className="space-y-3 text-sm">
                {(livreurs.byVerification ?? []).map((t) => (
                  <Mesure
                    key={String(t.status)}
                    terme={lireVerification(t.status).libelle}
                    valeur={nombre(val(t.count))}
                  />
                ))}
                <Mesure
                  terme="Inscriptions sur la période"
                  valeur={nombre(val(livreurs.registeredInWindow))}
                />
                <Mesure
                  terme="Dossiers validés sur la période"
                  valeur={nombre(val(livreurs.verifiedInWindow))}
                />
              </dl>
            </div>
          </>
        ) : (
          <Vide />
        )}
      </div>
    </Carte>
  );
}

function Mesure({
  terme,
  valeur,
  contexte,
}: {
  terme: string;
  valeur: string;
  contexte?: string;
}) {
  return (
    <div className="flex items-baseline justify-between gap-4">
      <dt className="text-encre-2">
        {terme}
        {contexte ? <span className="ml-2 text-xs text-encre-3">{contexte}</span> : null}
      </dt>
      <dd className="chiffres-tabulaires font-medium text-encre">{valeur}</dd>
    </div>
  );
}

/**
 * Un délai ne s'affiche jamais seul.
 *
 * SANS SON EFFECTIF, « 18 MIN » SE LIT COMME UNE MESURE alors qu'au
 * lancement il reposera sur trois trajets.
 */
function Delai({
  terme,
  secondes,
  effectif,
}: {
  terme: string;
  secondes?: number | string;
  effectif?: number | string;
}) {
  const n = val(effectif);

  return (
    <div className="flex items-baseline justify-between gap-4">
      <dt className="text-encre-2">{terme}</dt>
      <dd className="text-right">
        <span className="chiffres-tabulaires font-medium text-encre">
          {n ? duree(secondes) : '—'}
        </span>
        <span className="ml-2 text-xs text-encre-3">
          {n ? `sur ${nombre(n)}` : 'aucune donnée'}
        </span>
      </dd>
    </div>
  );
}
