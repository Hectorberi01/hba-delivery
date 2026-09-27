'use client';

import { useMemo, useState } from 'react';
import {
  Badge,
  Bouton,
  Carte,
  CarteStat,
  Chargement,
  Erreur,
  Initiales,
  TitreDeCarte,
  Vide,
} from '@/composants/base';
import { CarteDesLivreurs } from '@/composants/carte-livreurs';
import { libelleVehicule, livreursVus, type LivreurVu } from '@/lib/agregats';
import { dateHeure, depuis, instant, nombre, val } from '@/lib/format';
import {
  COULEUR_TON,
  COULEUR_TON_LIVREUR,
  estDisponible,
  estEnAttenteDeKyc,
  estEnService,
  lireOperationnel,
  lireStatut,
  lireVerification,
  STATUTS,
} from '@/lib/statuts';
import { DossierDuLivreur } from '@/composants/dossier';
import { CoursesAProposer } from '@/composants/proposer-a-ce-livreur';
import { envoyer, useRessource } from '@/lib/client/ressource';
import type {
  Course,
  Livreur,
  PageDeCourses,
  Nombre,
  PageDeLivreurs,
  StatsLivreurs,
} from '@/lib/types';

export default function PageLivreurs() {
  // DEUX SOURCES, ET AUCUNE NE SAIT CE QUE SAIT L'AUTRE. L'annuaire vient du
  // service Driver : qui existe, dans quel etat, avec quel vehicule. Les
  // courses viennent de Delivery : qui a roule, et avec quel resultat. Le
  // service Driver ne compte pas les livraisons, et c'est voulu — il n'a pas
  // a savoir ce qu'est une course.
  const annuaire = useRessource<PageDeLivreurs>('admin/v1/drivers?pageSize=200');
  const activite = useRessource<PageDeCourses>('admin/v1/deliveries?pageSize=200');

  // LES TUILES NE COMPTENT PLUS LA PAGE CHARGEE. L'annuaire s'arrete a 200
  // lignes : compter dedans donnait un « 200 livreurs » qui aurait ete faux
  // des le 201e. Le service Driver compte sa propre base, l'ecran l'affiche.
  const compteurs = useRessource<StatsLivreurs>('admin/v1/kpi/drivers');

  const [filtre, setFiltre] = useState('');
  const [selection, setSelection] = useState<string | null>(null);

  const livreurs = useMemo(() => annuaire.donnees?.drivers ?? [], [annuaire.donnees]);
  const courses = useMemo(() => activite.donnees?.deliveries ?? [], [activite.donnees]);

  const parIdentifiant = useMemo(() => {
    const table = new Map<string, LivreurVu>();
    for (const vu of livreursVus(courses)) table.set(vu.identifiant, vu);
    return table;
  }, [courses]);

  const visibles = useMemo(() => {
    const terme = filtre.trim().toLowerCase();
    if (!terme) return livreurs;
    return livreurs.filter(
      (livreur) =>
        (livreur.displayName ?? '').toLowerCase().includes(terme) ||
        (livreur.phone ?? '').toLowerCase().includes(terme) ||
        livreur.id.toLowerCase().includes(terme),
    );
  }, [livreurs, filtre]);

  const courant = visibles.find((l) => l.id === selection) ?? visibles[0] ?? null;

  const stats = compteurs.donnees;
  const total = stats ? val(stats.total) : null;
  const enService = sommeSi(stats?.byOperational, estEnService);
  const disponibles = sommeSi(stats?.byOperational, estDisponible);
  const enAttente = sommeSi(stats?.byVerification, estEnAttenteDeKyc);

  // « — » PLUTOT QU'UN ZERO : si le service ne repond pas, l'ecran dit qu'il
  // ne sait pas. Un zero se lirait comme « aucun livreur en service ».
  const chiffre = (n: number | null) => (n === null ? '—' : nombre(n));
  const source = compteurs.erreur ? 'Service indisponible' : null;

  // Le total de l'annuaire reste celui de la liste paginee : c'est lui qui
  // legende « X sur Y » sous le filtre, et il parle bien des lignes lues.
  const totalAnnuaire = annuaire.donnees?.total ?? livreurs.length;

  return (
    <>
      {/* PAS DE « NOTE MOYENNE » : aucune note n'existe dans le domaine. La
          quatrieme tuile compte les dossiers en attente — c'est la seule
          chose que ce chiffre puisse dire de vrai, et c'est une file
          d'attente sur laquelle quelqu'un doit agir. */}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <CarteStat
          titre="Livreurs"
          valeur={chiffre(total)}
          detail={source ?? 'Inscrits sur la plateforme'}
        />
        <CarteStat
          titre="En service"
          valeur={chiffre(enService)}
          detail={source ?? 'Hors ligne exclus'}
        />
        <CarteStat
          titre="Disponibles"
          valeur={chiffre(disponibles)}
          detail={source ?? 'Prêts à recevoir une offre'}
        />
        <CarteStat
          titre="En attente de KYC"
          valeur={chiffre(enAttente)}
          detail={source ?? 'Dossiers à examiner'}
        />
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_420px]">
        <Carte>
          <TitreDeCarte
            titre="Annuaire"
            sous={`${nombre(visibles.length)} sur ${nombre(totalAnnuaire)}`}
            action={
              <input
                type="search"
                value={filtre}
                onChange={(e) => setFiltre(e.target.value)}
                placeholder="Filtrer par nom ou téléphone…"
                className="creux w-60 rounded-xl border-0 px-4 py-2.5 text-sm text-encre outline-none placeholder:text-encre-3"
              />
            }
          />

          {annuaire.chargement ? <Chargement quoi="de l'annuaire" /> : null}
          {annuaire.erreur ? <Erreur message={annuaire.erreur} /> : null}
          {!annuaire.chargement && !annuaire.erreur && !visibles.length ? (
            <Vide message="Aucun livreur ne correspond." />
          ) : null}

          {visibles.length ? (
            <ul className="grid gap-3 px-5 pb-5 pt-4 sm:grid-cols-2">
              {visibles.map((livreur) => {
                const actif = courant?.id === livreur.id;
                const operationnel = lireOperationnel(livreur.operationalStatus);
                const vu = parIdentifiant.get(livreur.id);

                return (
                  <li key={livreur.id}>
                    <button
                      type="button"
                      onClick={() => setSelection(livreur.id)}
                      aria-current={actif ? 'true' : undefined}
                      className={`w-full rounded-xl px-4 py-3.5 text-left transition ${
                        actif ? 'creux' : 'relief-doux pressable'
                      }`}
                    >
                      <span className="flex items-center gap-3">
                        <Initiales nom={livreur.displayName ?? '?'} />
                        <span className="min-w-0 flex-1">
                          <span className="block truncate font-medium text-encre">
                            {livreur.displayName || 'Sans nom'}
                          </span>
                          <span className="block text-sm text-encre-3">
                            {libelleVehicule(livreur.vehicle?.type)}
                          </span>
                        </span>
                        <Badge couleur={COULEUR_TON_LIVREUR[operationnel.ton]}>
                          {operationnel.libelle}
                        </Badge>
                      </span>

                      <span className="mt-3 flex items-baseline justify-between text-sm">
                        <span className="text-encre-3">
                          {vu ? depuis(vu.derniereActivite) : 'Aucune course vue'}
                        </span>
                        <span className="chiffres-tabulaires text-encre-2">
                          {nombre(vu?.courses.length ?? 0)} course
                          {(vu?.courses.length ?? 0) > 1 ? 's' : ''}
                        </span>
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>
          ) : null}
        </Carte>

        {courant ? (
          <FicheLivreur
            livreur={courant}
            activite={parIdentifiant.get(courant.id)}
            courses={courses}
            apresAction={activite.recharger}
          />
        ) : null}
      </div>

      <Carte>
        <TitreDeCarte titre="Ce qui manque encore" sous="Deux notions absentes du domaine" />
        <div className="space-y-3 px-5 pb-5 pt-4">
          {/* NOTE ET ZONE NE SONT PAS DES ROUTES MANQUANTES : elles n'existent
              nulle part dans le domaine. Les afficher demanderait d'abord de
              decider ce qu'on mesure, et le referentiel ne le dit pas. */}
          <div className="creux-doux rounded-xl px-5 py-4">
            <p className="text-sm font-medium text-encre-2">Note du livreur et zone principale</p>
            <p className="mt-1 text-sm text-encre-3">
              Ni note, ni évaluation, ni découpage en secteurs n&apos;existent dans les contrats :
              le service Driver connaît des positions, pas des zones. Les afficher demanderait
              d&apos;abord de décider ce que l&apos;on mesure et qui le saisit — c&apos;est une
              décision produit, pas un manque technique.
            </p>
          </div>
          <CarteDesLivreurs />
        </div>
      </Carte>
    </>
  );
}

/**
 * Somme les tranches dont le statut satisfait le predicat.
 *
 * Renvoie null quand le service n'a rien renvoye : l'absence de reponse et
 * un compte de zero ne s'affichent pas pareil.
 */
function sommeSi(
  tranches: { status?: number | string; count?: Nombre }[] | undefined,
  retenir: (statut: unknown) => boolean,
): number | null {
  if (!tranches) return null;
  return tranches.reduce(
    (somme, tranche) => (retenir(tranche.status) ? somme + val(tranche.count) : somme),
    0,
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

      <CoursesAProposer livreur={livreur} courses={courses} apresAction={apresAction} />

      {/* LE DOSSIER AVANT LES BOUTONS, et non l'inverse : ops doit voir les
          pieces avant de lire « Valider le KYC ». Un bouton de decision
          place au-dessus de ce qu'il decide se presse sans regarder. */}
      <DossierDuLivreur identifiant={livreur.id} />

      <ActionsLivreur identifiant={livreur.id} nom={livreur.displayName || 'ce livreur'} />
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
