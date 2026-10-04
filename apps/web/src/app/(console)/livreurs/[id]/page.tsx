'use client';

import { useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { Badge, Bouton, Carte, Chargement, Erreur, Initiales, TitreDeCarte } from '@/composants/base';
import { libelleVehicule, livreursVus, type LivreurVu } from '@/lib/agregats';
import { dateHeure, instant, nombre } from '@/lib/format';
import {
  COULEUR_TON,
  COULEUR_TON_LIVREUR,
  lireOperationnel,
  lireStatut,
  lireVerification,
  STATUTS,
} from '@/lib/statuts';
import { DossierDuLivreur } from '@/composants/dossier';
import { CoursesAProposer } from '@/composants/proposer-a-ce-livreur';
import { envoyer, useRessource } from '@/lib/client/ressource';
import type { Course, Livreur, PageDeCourses } from '@/lib/types';

/**
 * Le dossier d'un livreur, a son propre lien.
 *
 * IL ETAIT UN PANNEAU A DROITE DE L'ANNUAIRE JUSQU'AU 2 octobre 2026. Meme
 * raison que pour une course : « regarde ce dossier » est exactement ce qu'on
 * dit au support, et un panneau ne s'envoie pas.
 *
 * DEUX SOURCES, ET AUCUNE NE SAIT CE QUE SAIT L'AUTRE. Le service Driver dit qui
 * existe, dans quel etat et avec quel vehicule ; Delivery dit qui a roule et
 * avec quel resultat. Driver ne compte pas les livraisons, et c'est voulu — il
 * n'a pas a connaitre le metier de l'autre. L'activite se recalcule donc ici, a
 * partir des courses chargees.
 *
 * CE QUE CELA COUTE, ET QU'IL FAUT SAVOIR : « Activite vue dans les courses »
 * porte bien son nom. Elle ne voit que les deux cents dernieres courses, pas
 * l'historique complet, et le sous-titre le dit a l'ecran.
 */
export default function PageLivreur() {
  const parametres = useParams<{ id: string }>();
  const id = parametres?.id ?? '';

  const livreur = useRessource<Livreur>(
    id ? `admin/v1/drivers/${encodeURIComponent(id)}` : null,
  );

  const activite = useRessource<PageDeCourses>('admin/v1/deliveries?pageSize=200');

  const courses = useMemo(() => activite.donnees?.deliveries ?? [], [activite.donnees]);

  const vu = useMemo(
    () => livreursVus(courses).find((l) => l.identifiant === id),
    [courses, id],
  );

  return (
    <>
      <div className="flex items-center gap-3">
        <Link
          href="/livreurs"
          className="relief-doux pressable rounded-xl px-3 py-2 text-sm text-encre-2 hover:text-encre"
        >
          ← Livreurs
        </Link>
      </div>

      {livreur.chargement ? (
        <Carte>
          <Chargement quoi="du livreur" />
        </Carte>
      ) : null}

      {livreur.erreur ? (
        <Carte>
          <Erreur message={livreur.erreur} />
        </Carte>
      ) : null}

      {!livreur.chargement && !livreur.erreur && livreur.donnees ? (
        <FicheLivreur
          livreur={livreur.donnees}
          activite={vu}
          courses={courses}
          apresAction={activite.recharger}
        />
      ) : null}
    </>
  );
}

function FicheLivreur({
  livreur,
  activite,
  courses,
  apresAction,
}: {
  livreur: Livreur;
  activite?: LivreurVu;
  courses: Course[];
  apresAction: () => void;
}) {
  const verification = lireVerification(livreur.verificationStatus);
  const operationnel = lireOperationnel(livreur.operationalStatus);

  // TRIER SUR L'INSTANT, PAS SUR LA VALEUR BRUTE. localeCompare sur un objet
  // { seconds, nanos } compare deux « [object Object] » : le tri ne faisait
  // rien, en silence.
  const recentes = [...(activite?.courses ?? [])]
    .sort((a, b) => (instant(b.createdAt) ?? 0) - (instant(a.createdAt) ?? 0))
    .slice(0, 6);

  return (
    // DEUX COLONNES, ET LE DECOUPAGE N'EST PAS « LA MOITIE DE CHAQUE COTE ».
    //
    // A GAUCHE, QUI EST CE LIVREUR : identite, etat, activite. C'est ce qu'on
    // vient lire, et ca se lit de haut en bas sans interruption.
    //
    // A DROITE, CE QU'ON PEUT FAIRE : lui proposer une course, regarder ses
    // pieces, decider de son KYC. Les trois sont des gestes, et un geste se
    // prend a cote de ce qu'il concerne, pas trois ecrans plus bas.
    //
    // LE DOSSIER RESTE AU-DESSUS DES BOUTONS, et c'est la seule contrainte
    // d'ordre qui compte : ops doit voir les pieces avant de lire « Valider le
    // KYC ». Un bouton de decision place au-dessus de ce qu'il decide se presse
    // sans regarder. En une colonne — sur un ecran etroit — l'ordre du flux les
    // garde dans cet ordre.
    //
    // LES COLONNES NE SE SUIVENT PAS EN HAUTEUR : chacune a « items-start », donc
    // une carte courte ne force pas sa voisine a s'etirer.
    <div className="grid items-start gap-4 lg:grid-cols-2 xl:grid-cols-[minmax(0,420px)_minmax(0,1fr)]">
      <div className="space-y-4">
      <Carte>
        <div className="flex items-center gap-3 px-5 pt-5">
          <Initiales nom={livreur.displayName ?? '?'} />
          <div className="min-w-0 flex-1">
            <h2 className="truncate text-lg font-semibold text-encre">
              {livreur.displayName || 'Sans nom'}
            </h2>
            <p className="truncate font-mono text-xs text-encre-3">{livreur.id}</p>
          </div>
        </div>

        <div className="mt-3 flex flex-wrap gap-2 px-5">
          <Badge couleur={COULEUR_TON_LIVREUR[verification.ton]}>{verification.libelle}</Badge>
          <Badge couleur={COULEUR_TON_LIVREUR[operationnel.ton]}>{operationnel.libelle}</Badge>
        </div>

        <dl className="mt-5 space-y-3 px-5 text-sm">
          <Ligne terme="Téléphone" valeur={livreur.phone} />
          <Ligne terme="Véhicule" valeur={libelleVehicule(livreur.vehicle?.type)} />
          <Ligne terme="Plaque" valeur={livreur.vehicle?.plate} />
          <Ligne terme="Inscrit le" valeur={dateHeure(livreur.registeredAt)} />
          <Ligne terme="Validé le" valeur={livreur.verifiedAt ? dateHeure(livreur.verifiedAt) : '—'} />
          {livreur.statusReason ? <Ligne terme="Motif" valeur={livreur.statusReason} /> : null}
        </dl>

        <div className="mt-5 border-t border-bordure px-5 py-4">
          <h3 className="text-sm font-semibold text-encre">Activité vue dans les courses</h3>
          {activite ? (
            <>
              <dl className="mt-3 space-y-3 text-sm">
                <Ligne terme="Courses" valeur={nombre(activite.courses.length)} />
                <Ligne terme="En cours" valeur={nombre(activite.enCours)} />
                <Ligne
                  terme="Réussite"
                  valeur={
                    activite.closes
                      ? `${Math.round((activite.livrees / activite.closes) * 100)} % sur ${nombre(activite.closes)} closes`
                      : 'Aucune course close'
                  }
                />
              </dl>

              <ul className="mt-4 space-y-2">
                {recentes.map((course) => {
                  const definition = STATUTS[lireStatut(course.status)];
                  return (
                    <li key={course.id} className="flex items-center justify-between gap-3 text-sm">
                      <span className="truncate text-encre-2">
                        {course.reference ?? course.id.slice(0, 8)}
                      </span>
                      <Badge couleur={COULEUR_TON[definition.ton]}>{definition.libelle}</Badge>
                    </li>
                  );
                })}
              </ul>
            </>
          ) : (
            <p className="mt-2 text-sm text-encre-3">
              Aucune course de ce livreur sur la page chargée. Les compteurs portent sur les
              courses lues, pas sur tout son historique.
            </p>
          )}
        </div>
      </Carte>
      </div>

      <div className="space-y-4">
        <CoursesAProposer livreur={livreur} courses={courses} apresAction={apresAction} />

        <DossierDuLivreur identifiant={livreur.id} />

        <ActionsLivreur identifiant={livreur.id} nom={livreur.displayName || 'ce livreur'} />
      </div>
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

function ActionsLivreur({ identifiant, nom }: { identifiant: string; nom: string }) {
  const [motif, setMotif] = useState('');
  const [envoi, setEnvoi] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [probleme, setProbleme] = useState<string | null>(null);

  const motifManquant = motif.trim().length < 3;

  async function agir(chemin: string, corps: unknown, succes: string) {
    setEnvoi(true);
    setMessage(null);
    setProbleme(null);

    try {
      await envoyer(chemin, corps);
      setMessage(succes);
      setMotif('');
    } catch (cause) {
      setProbleme(cause instanceof Error ? cause.message : 'Action refusée.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Carte>
      <TitreDeCarte titre="Actions" sous={`Sur ${nom} — rôle admin uniquement`} />

      <div className="space-y-4 px-5 pb-5 pt-4">
        <label className="block">
          <span className="mb-1.5 block text-sm font-medium text-encre-2">
            Motif <span className="font-normal text-encre-3">— obligatoire, il est audité</span>
          </span>
          <textarea
            value={motif}
            onChange={(e) => setMotif(e.target.value)}
            rows={3}
            className="creux w-full rounded-xl border-0 px-4 py-3 text-sm text-encre outline-none placeholder:text-encre-3"
            placeholder="Conservé avec votre nom et la date."
          />
        </label>

        {/* LE SUPPORT VOIT CES BOUTONS ET N'EN OBTIENDRA RIEN : la passerelle
            et le handler exigent le rôle admin. Les masquer serait plus
            propre a l'oeil, mais l'interface ne sait pas ce que le service
            autorisera — et se fier au masquage, c'est l'erreur que l'ADR 0007
            interdit. */}
        <div className="flex flex-wrap gap-2">
          <Bouton
            disabled={envoi}
            onClick={() =>
              agir(
                `admin/v1/drivers/${identifiant}/kyc`,
                { approved: true, reason: motif.trim() },
                'Dossier validé. Le livreur peut passer en ligne.',
              )
            }
          >
            Valider le KYC
          </Bouton>
          <Bouton
            variante="discret"
            disabled={envoi || motifManquant}
            onClick={() =>
              agir(
                `admin/v1/drivers/${identifiant}/kyc`,
                { approved: false, reason: motif.trim() },
                'Dossier rejeté.',
              )
            }
          >
            Rejeter le KYC
          </Bouton>
          <Bouton
            variante="danger"
            disabled={envoi || motifManquant}
            onClick={() =>
              agir(
                `admin/v1/drivers/${identifiant}/suspend`,
                { reason: motif.trim() },
                'Livreur suspendu. Il ne recevra plus aucune offre.',
              )
            }
          >
            Suspendre
          </Bouton>
        </div>

        {message ? <p className="text-sm text-bon">{message}</p> : null}
        {probleme ? (
          <p role="alert" className="text-sm text-critique">
            {probleme}
          </p>
        ) : null}
      </div>
    </Carte>
  );
}
