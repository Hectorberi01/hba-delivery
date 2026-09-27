'use client';

import { useMemo, useState } from 'react';
import {
  Badge,
  Bouton,
  Carte,
  CarteStat,
  Chargement,
  Erreur,
  TitreDeCarte,
  Vide,
} from '@/composants/base';
import {
  libelleSource,
  livreesAujourdhui,
  montant,
  ouvertes,
  tauxDeSucces,
} from '@/lib/agregats';
import { dateHeure, depuis, duree, instant, nombre, xof } from '@/lib/format';
import { ProposerLaCourse } from '@/composants/proposer-course';
import { COULEUR_TON, lireStatut, STATUTS, type CleStatut } from '@/lib/statuts';
import { envoyer, useRessource } from '@/lib/client/ressource';
import type { Course, PageDeCourses } from '@/lib/types';

const FILTRES: { cle: CleStatut | 'toutes'; libelle: string }[] = [
  { cle: 'toutes', libelle: 'Tous les statuts' },
  { cle: 'attente_paiement', libelle: 'Attente de paiement' },
  { cle: 'payee', libelle: 'Payée' },
  { cle: 'recherche_livreur', libelle: 'Recherche livreur' },
  { cle: 'livreur_affecte', libelle: 'Livreur affecté' },
  { cle: 'collectee', libelle: 'Colis collecté' },
  { cle: 'livree', libelle: 'Livrée' },
  { cle: 'sans_livreur', libelle: 'Aucun livreur trouvé' },
  { cle: 'annulee', libelle: 'Annulée' },
  { cle: 'echouee', libelle: 'Échouée' },
];

export default function PageCommandes() {
  const { donnees, chargement, erreur, recharger } = useRessource<PageDeCourses>(
    'admin/v1/deliveries?pageSize=100',
  );

  const [recherche, setRecherche] = useState('');
  const [filtre, setFiltre] = useState<CleStatut | 'toutes'>('toutes');
  const [selection, setSelection] = useState<string | null>(null);

  const courses = useMemo(() => donnees?.deliveries ?? [], [donnees]);

  const visibles = useMemo(() => {
    const terme = recherche.trim().toLowerCase();

    return courses.filter((course) => {
      if (filtre !== 'toutes' && lireStatut(course.status) !== filtre) return false;
      if (!terme) return true;

      return [
        course.reference,
        course.id,
        course.recipientName,
        course.recipientPhone,
        course.dropoff?.landmark,
        course.driver?.displayName,
      ]
        .filter(Boolean)
        .some((valeur) => String(valeur).toLowerCase().includes(terme));
    });
  }, [courses, filtre, recherche]);

  const courante = visibles.find((course) => course.id === selection) ?? visibles[0] ?? null;

  const taux = tauxDeSucces(courses);

  return (
    <>
      {/* LES QUATRE CHIFFRES PORTENT SUR LA PAGE CHARGEE, pas sur la base :
          ListDeliveries rend des courses, aucun agregat. Chaque tuile le dit
          sous sa valeur — un nombre sans perimetre finit par etre lu comme un
          total d'entreprise, et celui-la bougerait avec la taille de page. */}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <CarteStat
          titre="Courses"
          valeur={nombre(courses.length)}
          detail="Sur la page chargée"
        />
        <CarteStat
          titre="En cours"
          valeur={nombre(ouvertes(courses).length)}
          detail="Non encore clôturées"
        />
        <CarteStat
          titre="Livrées aujourd'hui"
          valeur={nombre(livreesAujourdhui(courses).length)}
          detail="Remise confirmée depuis minuit"
        />
        <CarteStat
          titre="Taux de succès"
          valeur={taux === null ? '—' : `${Math.round(taux * 100)} %`}
          detail="Livrées sur courses closes"
        />
      </div>

      <Carte className="px-4 py-3">
        <div className="flex flex-wrap items-center gap-3">
          <input
            type="search"
            value={recherche}
            onChange={(e) => setRecherche(e.target.value)}
            placeholder="Rechercher une référence, un destinataire, un livreur…"
            className="creux min-w-[260px] flex-1 rounded-xl border-0 px-4 py-3 text-sm text-encre outline-none placeholder:text-encre-3"
          />
          <select
            value={filtre}
            onChange={(e) => setFiltre(e.target.value as CleStatut | 'toutes')}
            className="creux rounded-xl border-0 px-3.5 py-3 text-sm text-encre-2 outline-none"
          >
            {FILTRES.map((option) => (
              <option key={option.cle} value={option.cle}>
                {option.libelle}
              </option>
            ))}
          </select>
          <span className="text-sm text-encre-3">
            {visibles.length} sur {courses.length}
          </span>
          {/* Le filtrage et la recherche portent sur la page deja chargee. Une
              recherche cote service demanderait un parametre que
              ListDeliveries n'a pas. */}
        </div>
      </Carte>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_400px]">
        <Carte className="overflow-hidden">
          <TitreDeCarte titre="Courses" sous="Les plus récentes d'abord" />

          {chargement ? <Chargement quoi="des courses" /> : null}
          {erreur ? <Erreur message={erreur} /> : null}
          {!chargement && !erreur && !visibles.length ? (
            <Vide message="Aucune course ne correspond." />
          ) : null}

          {visibles.length ? (
            <div className="mt-4 overflow-x-auto">
              <table className="w-full border-collapse text-sm">
                <thead>
                  <tr className="border-y border-bordure bg-plan text-left text-xs uppercase tracking-wide text-encre-3">
                    <th className="px-5 py-2.5 font-medium">Référence</th>
                    <th className="px-3 py-2.5 font-medium">Destinataire</th>
                    <th className="px-3 py-2.5 font-medium">Déposé à</th>
                    <th className="px-3 py-2.5 font-medium">Livreur</th>
                    <th className="px-3 py-2.5 font-medium">Statut</th>
                    <th className="px-3 py-2.5 text-right font-medium">Montant</th>
                    <th className="px-5 py-2.5 text-right font-medium">Créée</th>
                  </tr>
                </thead>
                <tbody>
                  {visibles.map((course) => {
                    const cle = lireStatut(course.status);
                    const definition = STATUTS[cle];
                    const active = courante?.id === course.id;

                    return (
                      <tr
                        key={course.id}
                        onClick={() => setSelection(course.id)}
                        className={`cursor-pointer border-b border-bordure transition ${
                          active ? 'bg-marque-doux' : 'hover:bg-plan'
                        }`}
                      >
                        <td className="px-5 py-3 font-medium text-encre">
                          {course.reference ?? course.id.slice(0, 8)}
                        </td>
                        <td className="px-3 py-3 text-encre-2">{course.recipientName || '—'}</td>
                        <td className="max-w-[220px] truncate px-3 py-3 text-encre-2">
                          {course.dropoff?.landmark || '—'}
                        </td>
                        <td className="px-3 py-3 text-encre-2">
                          {course.driver?.displayName || (
                            <span className="text-encre-3">Non assigné</span>
                          )}
                        </td>
                        <td className="px-3 py-3">
                          <Badge couleur={COULEUR_TON[definition.ton]}>{definition.libelle}</Badge>
                        </td>
                        <td className="chiffres-tabulaires px-3 py-3 text-right font-medium text-encre">
                          {xof(montant(course))}
                        </td>
                        <td className="px-5 py-3 text-right text-encre-3">
                          {depuis(course.createdAt)}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          ) : null}
        </Carte>

        {courante ? <PanneauCourse course={courante} apresAction={recharger} /> : null}
      </div>
    </>
  );
}

function PanneauCourse({ course, apresAction }: { course: Course; apresAction: () => void }) {
  const cle = lireStatut(course.status);
  const definition = STATUTS[cle];

  // L'INSTANT EST CALCULE UNE FOIS, PAS TESTE SUR LA VALEUR BRUTE. Un
  // { seconds, nanos } est toujours « vrai » : tester la presence de l'objet
  // allumait la pastille pour une date qu'on ne savait pas lire.
  const etapes = [
    { libelle: 'Créée', date: instant(course.createdAt) },
    { libelle: 'Payée', date: instant(course.paidAt) },
    { libelle: 'Livreur affecté', date: instant(course.assignedAt) },
    { libelle: 'Colis collecté', date: instant(course.pickedUpAt) },
    { libelle: 'Clôturée', date: instant(course.completedAt) },
  ];

  const depart = etapes[0].date;

  return (
    <Carte className="h-fit">
      <div className="flex items-start justify-between gap-3 px-5 pt-5">
        <div>
          <p className="text-xs uppercase tracking-wide text-encre-3">Course sélectionnée</p>
          <h2 className="mt-1 text-lg font-semibold text-encre">
            {course.reference ?? course.id.slice(0, 8)}
          </h2>
        </div>
        <Badge couleur={COULEUR_TON[definition.ton]}>{definition.libelle}</Badge>
      </div>

      <dl className="mt-5 space-y-3 px-5 text-sm">
        <Ligne terme="Origine" valeur={libelleSource(course.source)} />
        <Ligne terme="Destinataire" valeur={course.recipientName} />
        <Ligne terme="Téléphone" valeur={course.recipientPhone} />
        <Ligne terme="Enlèvement" valeur={course.pickup?.landmark} />
        <Ligne terme="Livraison" valeur={course.dropoff?.landmark} />
        <Ligne terme="Colis" valeur={course.packageDescription} />
        <Ligne terme="Montant" valeur={xof(montant(course))} />
        <Ligne terme="Livreur" valeur={course.driver?.displayName} />
        {course.closureReason ? <Ligne terme="Motif de clôture" valeur={course.closureReason} /> : null}
      </dl>

      <div className="mt-6 border-t border-bordure px-5 py-5">
        <h3 className="text-sm font-semibold text-encre">Déroulé</h3>
        {/* LE DELAI DEPUIS LA CREATION EST LE DETAIL QUI COMPTE. Une heure
            seule ne dit pas si la recherche a mis dix secondes ou deux
            minutes ; c'est pourtant la question qu'on se pose en ouvrant le
            deroule d'une course qui a mal tourne. */}
        <ol className="mt-3 space-y-3">
          {etapes.map((etape) => {
            // LIE A UNE CONSTANTE LOCALE : le compilateur ne suit pas un
            // booleen intermediaire pour affiner le type d'une propriete.
            const quand = etape.date;
            const atteinte = quand !== null;
            const ecart =
              quand !== null && depart !== null && quand > depart
                ? duree((quand - depart) / 1000)
                : null;

            return (
              <li key={etape.libelle} className="flex items-start gap-3">
                <span
                  aria-hidden
                  className={`mt-1.5 h-2 w-2 shrink-0 rounded-full ${
                    atteinte ? 'bg-marque' : 'bg-bordure'
                  }`}
                />
                <span className="min-w-0 text-sm">
                  <span className={atteinte ? 'text-encre' : 'text-encre-3'}>{etape.libelle}</span>
                  <span className="ml-2 chiffres-tabulaires text-encre-3">
                    {quand !== null ? dateHeure(quand) : '—'}
                  </span>
                  {ecart ? (
                    <span className="ml-2 chiffres-tabulaires text-encre-3">+ {ecart}</span>
                  ) : null}
                </span>
              </li>
            );
          })}
        </ol>
      </div>

      {cle === 'recherche_livreur' ? (
        <ProposerLaCourse course={course} apresAction={apresAction} />
      ) : null}

      {cle === 'sans_livreur' ? <SansLivreur /> : null}

      <ActionsAdmin course={course} apresAction={apresAction} />
    </Carte>
  );
}

/**
 * CE QU'ON NE PROPOSE PAS, ET POURQUOI.
 *
 * NO_DRIVER_FOUND est un etat TERMINAL de la livraison : la table des
 * transitions n'en laisse sortir aucune. Y ajouter un bouton « proposer quand
 * meme » ne serait pas une fonctionnalite manquante mais une regle metier
 * inventee — que devient le paiement deja encaisse, le client a-t-il ete
 * prevenu, jusqu'a quand peut-on rouvrir ? L'ecran le dit au lieu de faire
 * comme si le bouton avait ete oublie.
 */
function SansLivreur() {
  return (
    <div className="border-t border-bordure px-5 py-5">
      <h3 className="text-sm font-semibold text-encre">Proposer à un livreur</h3>
      <p className="mt-1 text-sm text-encre-3">
        Impossible sur cette course : <strong>aucun livreur trouvé</strong> est un état terminal.
        La rouvrir n&apos;est pas une case à cocher — il faut d&apos;abord décider ce qu&apos;on
        fait du paiement encaissé et jusqu&apos;à quand une course peut repartir. Tant que ce
        n&apos;est pas tranché, la voie est la clôture puis une nouvelle course.
      </p>
    </div>
  );
}

function Ligne({ terme, valeur }: { terme: string; valeur?: string | null }) {
  return (
    <div className="flex items-start justify-between gap-4">
      <dt className="shrink-0 text-encre-3">{terme}</dt>
      <dd className="text-right text-encre">{valeur || '—'}</dd>
    </div>
  );
}

function ActionsAdmin({ course, apresAction }: { course: Course; apresAction: () => void }) {
  const [cible, setCible] = useState<'annulee' | 'echouee'>('annulee');
  const [motif, setMotif] = useState('');
  const [envoi, setEnvoi] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [probleme, setProbleme] = useState<string | null>(null);

  const cloturee = !STATUTS[lireStatut(course.status)].ouverte;

  async function cloturer() {
    setEnvoi(true);
    setMessage(null);
    setProbleme(null);

    try {
      await envoyer(`admin/v1/deliveries/${course.id}/close`, {
        failed: cible === 'echouee',
        reason: motif.trim(),
      });
      setMotif('');
      setMessage('Course clôturée. La décision est tracée : auteur, date, motif.');
      apresAction();
    } catch (cause) {
      setProbleme(cause instanceof Error ? cause.message : 'Échec de la clôture.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <div className="border-t border-bordure px-5 py-5">
      <h3 className="text-sm font-semibold text-encre">Clôture forcée</h3>

      {/* AUCUN BOUTON « MARQUER COMME LIVRE », ET CE N'EST PAS UN OUBLI.
          Seule la remise avec l'OTP du destinataire produit DELIVERED
          (ADR 0005). Un administrateur peut clore en ECHOUEE ou ANNULEE ; le
          service refuse le reste, et l'interface ne doit pas le proposer. */}
      <p className="mt-1 text-sm text-encre-3">
        Un administrateur clôt en <strong>annulée</strong> ou <strong>échouée</strong>. La remise
        au destinataire, elle, ne se déclare pas depuis ici : elle se prouve par l&apos;OTP.
      </p>

      {cloturee ? (
        <p className="mt-4 rounded-lg bg-plan px-3 py-2 text-sm text-encre-3">
          Cette course est déjà close.
        </p>
      ) : (
        <div className="mt-4 space-y-3">
          <div className="flex gap-2">
            {(
              [
                ['annulee', 'Annulée'],
                ['echouee', 'Échouée'],
              ] as const
            ).map(([valeur, libelle]) => (
              <button
                key={valeur}
                type="button"
                onClick={() => setCible(valeur)}
                className={`rounded-xl px-3.5 py-2 text-sm transition ${
                  cible === valeur
                    ? 'creux font-medium text-marque'
                    : 'relief-doux pressable text-encre-2 hover:text-encre'
                }`}
              >
                {libelle}
              </button>
            ))}
          </div>

          <label className="block">
            <span className="mb-1.5 block text-sm font-medium text-encre-2">
              Motif <span className="font-normal text-encre-3">— obligatoire, il est audité</span>
            </span>
            <textarea
              value={motif}
              onChange={(e) => setMotif(e.target.value)}
              rows={3}
              className="creux w-full rounded-xl border-0 px-4 py-3 text-sm text-encre outline-none placeholder:text-encre-3"
              placeholder="Ce que vous écrivez ici est conservé avec votre nom et la date."
            />
          </label>

          <Bouton
            variante="danger"
            disabled={envoi || motif.trim().length < 3}
            onClick={cloturer}
            className="w-full"
          >
            {envoi ? 'Clôture…' : `Clore en ${cible === 'echouee' ? 'échouée' : 'annulée'}`}
          </Bouton>
        </div>
      )}

      {message ? <p className="mt-3 text-sm text-bon">{message}</p> : null}
      {probleme ? (
        <p role="alert" className="mt-3 text-sm text-critique">
          {probleme}
        </p>
      ) : null}

      <div className="creux-doux mt-6 rounded-xl px-4 py-3">
        <p className="text-sm font-medium text-encre-2">Réaffectation</p>
        <p className="mt-1 text-sm text-encre-3">
          Volontairement absente. Réaffecter une course déjà acceptée touche à la politique
          d&apos;annulation — le livreur déjà en route est-il dédommagé, et sur quelle base ?
          C&apos;est le point 3 des points à trancher ; tant qu&apos;il ne l&apos;est pas,
          <code className="creux-doux mx-1 rounded px-1.5 py-0.5 text-[13px]">ForceReassign</code>
          répond UNIMPLEMENTED.
        </p>
      </div>
    </div>
  );
}
