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
import { dateHeure, depuis, instant, nombre, val, xof } from '@/lib/format';
import { COULEUR_TON } from '@/lib/statuts';
import { entre, libelleMouvement, lireVersement } from '@/lib/versements';
import { envoyer, useRessource } from '@/lib/client/ressource';
import type {
  DemandeVersement,
  FileDesVersements,
  PageDeLivreurs,
  ReleveLivreur,
} from '@/lib/types';

type Vue = { cle: string; libelle: string; requete: string; vide: string };

/**
 * TROIS VUES, PAS CINQ.
 *
 * « A traiter » est l'absence de filtre cote service : les demandees ET les
 * approuvees, la plus ancienne d'abord. Offrir en plus un onglet par etat
 * aurait decoupe la file en deux moities dont l'une se serait fait oublier —
 * or ce sont precisement les approuvees sans virement qu'il faut voir.
 */
const VUES: Vue[] = [
  {
    cle: 'attente',
    libelle: 'À traiter',
    requete: 'admin/v1/payouts?limit=100',
    vide: 'Aucune demande en attente.',
  },
  {
    cle: 'versees',
    libelle: 'Versées',
    requete: 'admin/v1/payouts?status=PAID&limit=100',
    vide: 'Aucun versement consigné pour le moment.',
  },
  {
    cle: 'refusees',
    libelle: 'Refusées',
    requete: 'admin/v1/payouts?status=REJECTED&limit=100',
    vide: 'Aucun refus.',
  },
];

export default function PageVersements() {
  const [vue, setVue] = useState(VUES[0]);
  const [selection, setSelection] = useState<string | null>(null);

  const file = useRessource<FileDesVersements>(vue.requete);

  // L'ANNUAIRE SERT UNIQUEMENT A METTRE UN NOM SUR UN IDENTIFIANT. Payment ne
  // connait pas les livreurs et n'a pas a les connaitre : il n'a pas le droit
  // de lire la base de Driver. C'est donc la console qui rapproche les deux
  // listes, et c'est le bon endroit — un service qui irait chercher le nom
  // chez l'autre creerait une dependance pour un libelle.
  const annuaire = useRessource<PageDeLivreurs>('admin/v1/drivers?pageSize=200');

  const demandes = useMemo(() => file.donnees?.payouts ?? [], [file.donnees]);

  const noms = useMemo(() => {
    const table = new Map<string, string>();
    for (const livreur of annuaire.donnees?.drivers ?? []) {
      if (livreur.displayName) table.set(livreur.id, livreur.displayName);
    }
    return table;
  }, [annuaire.donnees]);

  const courante = demandes.find((d) => d.id === selection) ?? demandes[0] ?? null;

  const enAttente = demandes.filter((d) => d.status === 'PAYOUT_STATUS_REQUESTED');
  const approuvees = demandes.filter((d) => d.status === 'PAYOUT_STATUS_APPROVED');

  const totalDemande = enAttente.reduce((somme, d) => somme + val(d.amountXof), 0);
  const totalAVirer = approuvees.reduce((somme, d) => somme + val(d.amountXof), 0);

  // LE PLUS VIEUX DOSSIER EST UN INDICATEUR DE DETTE DE SERVICE, pas une
  // curiosite : la file est servie dans l'ordre d'arrivee, donc le premier de
  // la liste est aussi celui qui attend depuis le plus longtemps.
  const doyenne = vue.cle === 'attente' ? demandes[0] : null;

  return (
    <>
      <Carte className="px-5 py-4">
        <h1 className="text-lg font-semibold text-encre">Versements aux livreurs</h1>
        <p className="mt-1 max-w-3xl text-sm text-encre-3">
          Un livreur demande une somme, la finance l&apos;approuve ou la refuse, fait le virement
          chez l&apos;opérateur, puis vient consigner sa référence ici. C&apos;est cette dernière
          étape — et elle seule — qui débite le compte du livreur : tant qu&apos;elle n&apos;a pas
          eu lieu, «&nbsp;versé&nbsp;» ne voudrait rien dire.
        </p>
      </Carte>

      {vue.cle === 'attente' ? (
        <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
          <CarteStat
            titre="Demandes à répondre"
            valeur={nombre(enAttente.length)}
            detail="Le livreur attend un accord ou un refus."
          />
          <CarteStat
            titre="Montant demandé"
            valeur={xof(totalDemande)}
            detail="Somme des demandes sans réponse."
          />
          <CarteStat
            titre="Virements à faire"
            valeur={`${nombre(approuvees.length)} · ${xof(totalAVirer)}`}
            detail="Approuvées, argent pas encore parti."
          />
          <CarteStat
            titre="Plus ancienne"
            valeur={doyenne ? depuis(doyenne.requestedAt) : '—'}
            detail={doyenne ? "Tête de file : c'est elle qu'on traite d'abord." : 'File vide.'}
          />
        </div>
      ) : null}

      <div className="flex flex-wrap items-center gap-1">
        {VUES.map((candidate) => (
          <button
            key={candidate.cle}
            type="button"
            onClick={() => {
              setVue(candidate);
              setSelection(null);
            }}
            className={`rounded-xl px-3.5 py-2 text-sm font-medium transition ${
              candidate.cle === vue.cle ? 'creux text-marque' : 'relief-doux pressable text-encre-2'
            }`}
          >
            {candidate.libelle}
          </button>
        ))}
      </div>

      <div className="grid gap-4 xl:grid-cols-[minmax(0,1fr)_420px]">
        <Carte>
          <TitreDeCarte
            titre={vue.libelle}
            sous={
              vue.cle === 'attente'
                ? 'La plus ancienne en premier : une file se sert dans l’ordre d’arrivée.'
                : 'La plus récente en premier.'
            }
          />

          {file.chargement ? (
            <Chargement quoi="des demandes" />
          ) : file.erreur ? (
            <Erreur message={file.erreur} />
          ) : demandes.length === 0 ? (
            <Vide message={vue.vide} />
          ) : (
            <ul className="mt-4 divide-y divide-bordure">
              {demandes.map((demande) => (
                <Ligne
                  key={demande.id}
                  demande={demande}
                  nom={noms.get(demande.driverId)}
                  actif={courante?.id === demande.id}
                  choisir={() => setSelection(demande.id)}
                />
              ))}
            </ul>
          )}
        </Carte>

        {courante ? (
          <Dossier
            demande={courante}
            nom={noms.get(courante.driverId)}
            recharger={file.recharger}
          />
        ) : null}
      </div>
    </>
  );
}

function Ligne({
  demande,
  nom,
  actif,
  choisir,
}: {
  demande: DemandeVersement;
  nom?: string;
  actif: boolean;
  choisir: () => void;
}) {
  const etat = lireVersement(demande.status);

  return (
    <li>
      <button
        type="button"
        onClick={choisir}
        aria-current={actif ? 'true' : undefined}
        className={`flex w-full items-center gap-4 px-5 py-3.5 text-left transition ${
          actif ? 'creux-doux' : 'hover:bg-plan'
        }`}
      >
        <Initiales nom={nom ?? demande.driverId} />

        <span className="min-w-0 flex-1">
          <span className="block truncate text-sm font-medium text-encre">
            {nom ?? demande.driverId}
          </span>
          <span className="block text-xs text-encre-3">
            Demandé {dateHeure(demande.requestedAt)} · il y a {depuis(demande.requestedAt)}
          </span>
        </span>

        <span className="shrink-0 text-right">
          <span className="block text-sm font-semibold text-encre">{xof(demande.amountXof)}</span>
          <Badge couleur={COULEUR_TON[etat.ton]}>{etat.libelle}</Badge>
        </span>
      </button>
    </li>
  );
}

/**
 * Le dossier d'une demande : le compte du livreur, l'historique de la demande,
 * et les gestes encore possibles.
 *
 * LE RELEVE EST LA POUR COMPRENDRE, PAS POUR RECALCULER. Le service a deja
 * refuse toute demande au-dela du disponible, et rien ne peut faire baisser un
 * solde entre-temps : un credit ne s'efface pas, et un seul versement est en
 * cours a la fois. Ce qu'il apporte, c'est le contexte — un livreur qui
 * demande 2 000 F sur 180 000 F gagnes n'est pas le meme dossier qu'un livreur
 * qui demande tout ce qu'il a.
 */
function Dossier({
  demande,
  nom,
  recharger,
}: {
  demande: DemandeVersement;
  nom?: string;
  recharger: () => void;
}) {
  const releve = useRessource<ReleveLivreur>(
    `admin/v1/drivers/${demande.driverId}/earnings?limit=25`,
  );

  const etat = lireVersement(demande.status);

  return (
    <div className="space-y-4">
      <Carte className="px-5 py-4">
        <div className="flex items-start justify-between gap-4">
          <div className="min-w-0">
            <h2 className="truncate text-base font-semibold text-encre">
              {nom ?? demande.driverId}
            </h2>
            <p className="mt-0.5 font-mono text-xs text-encre-3">{demande.driverId}</p>
          </div>
          <Badge couleur={COULEUR_TON[etat.ton]}>{etat.libelle}</Badge>
        </div>

        <p className="mt-3 text-sm text-encre-3">{etat.explication}</p>

        <dl className="mt-4 grid grid-cols-3 gap-2 text-center">
          <Chiffre libelle="Gagné" valeur={xof(releve.donnees?.earnedXof ?? 0)} />
          <Chiffre libelle="Déjà versé" valeur={xof(releve.donnees?.paidOutXof ?? 0)} />
          <Chiffre libelle="Reste dû" valeur={xof(releve.donnees?.dueXof ?? 0)} accent />
        </dl>

        {releve.erreur ? (
          <p className="mt-3 text-xs text-critique">
            Le relevé n&apos;a pas pu être lu : {releve.erreur}
          </p>
        ) : null}

        <p className="mt-3 text-xs text-encre-3">
          Demande de <span className="font-semibold text-encre-2">{xof(demande.amountXof)}</span>,
          déposée {dateHeure(demande.requestedAt)}.
        </p>
      </Carte>

      <Gestes demande={demande} recharger={recharger} />

      <Carte>
        <TitreDeCarte
          titre="Mouvements du compte"
          sous="Les 25 derniers. Les cumuls ci-dessus portent sur tout le compte."
        />
        {releve.chargement ? (
          <Chargement quoi="du relevé" />
        ) : (releve.donnees?.entries?.length ?? 0) === 0 ? (
          <Vide message="Aucun mouvement." />
        ) : (
          <ul className="mt-3 divide-y divide-bordure">
            {(releve.donnees?.entries ?? []).map((mouvement) => (
              <li key={mouvement.id} className="flex items-center gap-3 px-5 py-2.5">
                <span className="min-w-0 flex-1">
                  <span className="block text-sm text-encre">
                    {libelleMouvement(mouvement.kind)}
                    {mouvement.deliveryReference ? (
                      <span className="text-encre-3"> · {mouvement.deliveryReference}</span>
                    ) : null}
                  </span>
                  <span className="block text-xs text-encre-3">
                    {dateHeure(mouvement.occurredAt)}
                  </span>
                </span>
                <span
                  className={`shrink-0 text-sm font-semibold ${
                    entre(mouvement.direction) ? 'text-encre' : 'text-encre-3'
                  }`}
                >
                  {entre(mouvement.direction) ? '+' : '−'} {xof(mouvement.amountXof)}
                </span>
              </li>
            ))}
          </ul>
        )}
      </Carte>
    </div>
  );
}

function Chiffre({
  libelle,
  valeur,
  accent = false,
}: {
  libelle: string;
  valeur: string;
  accent?: boolean;
}) {
  return (
    <div className="creux-doux rounded-xl px-2 py-3">
      <dt className="text-[11px] uppercase tracking-wide text-encre-3">{libelle}</dt>
      <dd className={`mt-1 text-sm font-semibold ${accent ? 'text-marque' : 'text-encre'}`}>
        {valeur}
      </dd>
    </div>
  );
}

/**
 * Les gestes possibles, et RIEN DE PLUS QUE LES GESTES POSSIBLES.
 *
 * L'ECRAN N'INVENTE AUCUNE TRANSITION. Une demande approuvee ne peut plus etre
 * refusee : le domaine n'admet le refus que depuis « demandee ». Mettre un
 * bouton « Refuser » sur une approuvee donnerait une erreur 409 a chaque clic,
 * et l'operateur conclurait a une panne. La consequence est reelle et elle est
 * dite a l'ecran plutot que contournee ici : un virement qui n'aura jamais
 * lieu laisse la demande en cours, et le livreur ne peut pas en ouvrir une
 * autre. Ce qu'il faut pour en sortir est une regle a trancher, pas un bouton
 * a ajouter.
 */
function Gestes({ demande, recharger }: { demande: DemandeVersement; recharger: () => void }) {
  const [motif, setMotif] = useState('');
  const [reference, setReference] = useState('');
  const [enCours, setEnCours] = useState<string | null>(null);
  const [echec, setEchec] = useState<string | null>(null);

  async function agir(quoi: string, chemin: string, corps: unknown) {
    setEnCours(quoi);
    setEchec(null);
    try {
      await envoyer(`admin/v1/payouts/${demande.id}/${chemin}`, corps);
      setMotif('');
      setReference('');
      recharger();
    } catch (cause) {
      setEchec(cause instanceof Error ? cause.message : 'Le service a refusé ce geste.');
    } finally {
      setEnCours(null);
    }
  }

  if (demande.status === 'PAYOUT_STATUS_REQUESTED') {
    return (
      <Carte className="px-5 py-4">
        <h3 className="text-sm font-semibold text-encre">Instruire la demande</h3>

        <label className="mt-3 block">
          <span className="mb-1.5 block text-sm font-medium text-encre-2">Motif de refus</span>
          <textarea
            value={motif}
            onChange={(evenement) => setMotif(evenement.target.value)}
            rows={2}
            placeholder="Ce texte est lu par le livreur dans son application."
            className="creux w-full rounded-xl border-0 px-4 py-3 text-sm text-encre outline-none placeholder:text-encre-3"
          />
          <span className="mt-1 block text-xs text-encre-3">
            Obligatoire pour refuser. Écrivez-le pour le livreur, pas comme une note interne : sans
            motif, il redemandera la même somme demain.
          </span>
        </label>

        <div className="mt-3 flex flex-wrap gap-2">
          <Bouton
            type="button"
            disabled={enCours !== null}
            onClick={() => agir('approuver', 'approve', {})}
          >
            {enCours === 'approuver' ? 'Approbation…' : 'Approuver'}
          </Bouton>
          <Bouton
            type="button"
            variante="danger"
            disabled={enCours !== null || motif.trim().length === 0}
            onClick={() => agir('refuser', 'reject', { reason: motif.trim() })}
          >
            {enCours === 'refuser' ? 'Refus…' : 'Refuser'}
          </Bouton>
        </div>

        <p className="mt-3 text-xs text-encre-3">
          Approuver n&apos;envoie aucun argent et ne touche pas au compte du livreur. Le virement se
          fait ensuite chez l&apos;opérateur, et c&apos;est sa référence qui clôt le dossier.
        </p>

        {echec ? <p className="mt-3 text-sm text-critique">{echec}</p> : null}
      </Carte>
    );
  }

  if (demande.status === 'PAYOUT_STATUS_APPROVED') {
    return (
      <Carte className="px-5 py-4">
        <h3 className="text-sm font-semibold text-encre">Consigner le virement</h3>
        <p className="mt-1 text-sm text-encre-3">
          Approuvée {dateHeure(demande.decidedAt)}
          {demande.decidedBy ? ` par ${demande.decidedBy}` : ''}. L&apos;argent n&apos;est pas
          encore parti.
        </p>

        <label className="mt-3 block">
          <span className="mb-1.5 block text-sm font-medium text-encre-2">
            Référence du virement
          </span>
          <input
            value={reference}
            onChange={(evenement) => setReference(evenement.target.value)}
            placeholder="Référence rendue par l'opérateur"
            className="creux w-full rounded-xl border-0 px-4 py-3 text-sm text-encre outline-none placeholder:text-encre-3"
          />
          <span className="mt-1 block text-xs text-encre-3">
            C&apos;est ce qu&apos;on montrera le jour où le livreur dira n&apos;avoir rien reçu.
          </span>
        </label>

        <Bouton
          type="button"
          className="mt-3"
          disabled={enCours !== null || reference.trim().length === 0}
          onClick={() => agir('verser', 'paid', { paymentReference: reference.trim() })}
        >
          {enCours === 'verser' ? 'Enregistrement…' : 'Marquer versé'}
        </Bouton>

        <p className="mt-3 text-xs text-encre-3">
          Ce geste débite le compte du livreur de {xof(demande.amountXof)}, dans la même transaction
          que le changement d&apos;état. Il n&apos;y a pas de retour en arrière : une correction se
          fera par une écriture de plus, jamais par une rature.
        </p>

        <div className="creux-doux mt-3 rounded-xl px-4 py-3">
          <p className="text-xs font-medium text-encre-2">
            SI CE VIREMENT N&apos;A JAMAIS LIEU, LA DEMANDE RESTE BLOQUÉE ICI.
          </p>
          <p className="mt-1 text-xs text-encre-3">
            Une demande approuvée ne peut plus être refusée, et le livreur ne peut pas en ouvrir une
            autre tant que celle-ci est en cours. Ce qui manque est une règle : qui annule une
            approbation, et ce que le livreur en voit. C&apos;est une décision, pas un bouton — elle
            est consignée dans les points à trancher.
          </p>
        </div>

        {echec ? <p className="mt-3 text-sm text-critique">{echec}</p> : null}
      </Carte>
    );
  }

  return (
    <Carte className="px-5 py-4">
      <h3 className="text-sm font-semibold text-encre">Dossier clos</h3>
      <dl className="mt-3 space-y-2 text-sm">
        {demande.decidedAt ? (
          <Ancienne
            libelle="Décidée"
            valeur={`${dateHeure(demande.decidedAt)}${demande.decidedBy ? ` · ${demande.decidedBy}` : ''}`}
          />
        ) : null}
        {demande.rejectionReason ? (
          <Ancienne libelle="Motif" valeur={demande.rejectionReason} />
        ) : null}
        {demande.paidAt ? <Ancienne libelle="Versée" valeur={dateHeure(demande.paidAt)} /> : null}
        {demande.paymentReference ? (
          <Ancienne libelle="Référence" valeur={demande.paymentReference} />
        ) : null}
      </dl>
      {instant(demande.paidAt) === null && instant(demande.decidedAt) === null ? (
        <p className="mt-2 text-sm text-encre-3">Aucune trace de décision sur ce dossier.</p>
      ) : null}
    </Carte>
  );
}

function Ancienne({ libelle, valeur }: { libelle: string; valeur: string }) {
  return (
    <div className="flex gap-3">
      <dt className="w-24 shrink-0 text-encre-3">{libelle}</dt>
      <dd className="min-w-0 flex-1 break-words text-encre-2">{valeur}</dd>
    </div>
  );
}
