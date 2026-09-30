'use client';

import { useState } from 'react';
import {
  Bouton,
  Carte,
  CarteStat,
  Champ,
  Chargement,
  Erreur,
  TitreDeCarte,
  Vide,
} from '@/composants/base';
import { xof } from '@/lib/format';
import { envoyer, useRessource } from '@/lib/client/ressource';

/**
 * LES COMPTES DE FACTURATION DES DONNEURS D'ORDRE.
 *
 * CET ECRAN N'EXISTAIT PAS, ET LE PARCOURS B2B ETAIT BOUCHE DE BOUT EN BOUT.
 * « Ouvrir un compte » et « recharger » n'etaient exposes que sur le gRPC
 * interne de Billing, que le proxy ne route pas : aucune porte, meme manuelle,
 * ne permettait de creer un compte. La premiere course de tout partenaire
 * echouait en « compte de facturation introuvable ».
 *
 * ON NE CHERCHE PAS UN TITULAIRE, ON LE NOMME. Il n'existe pas d'annuaire des
 * commercants dans cette console, et Billing ne connait que des identifiants :
 * l'operateur saisit le couple, comme il le lit dans la fiche du partenaire
 * qu'un admin a cree. Inventer une recherche supposerait de decider ou la
 * chercher, ce qui n'est pas tranche.
 *
 * TROIS GESTES, ET AUCUN DEBIT. Lire, ouvrir, recharger. Un debit est la
 * consequence d'une course : le service le refuse au back-office, faute de cas
 * d'usage — et ce n'est pas a un ecran de contourner cette regle.
 */
type Compte = {
  id: string;
  ownerType: string;
  ownerId: string;
  mode: string;
  status: string;
  balanceXof: number;
  creditLimitXof: number;
  availableXof: number;
  lowBalanceThresholdXof: number;
};

type Recharge = {
  id: string;
  amountXof: number;
  balanceAfterXof: number;
  reference: string;
};

const TYPES = [
  { cle: 'merchant', libelle: 'Commerçant' },
  { cle: 'partner', libelle: 'Partenaire' },
];

export default function PageFacturation() {
  const [type, setType] = useState('partner');
  const [identifiant, setIdentifiant] = useState('');

  // LE COMPTE N'EST DEMANDE QU'UNE FOIS LE COUPLE COMPLET. Interroger a chaque
  // frappe produirait une cascade de « compte introuvable » pendant que
  // l'operateur tape, et le message d'erreur serait celui du dernier caractere.
  const [interroge, setInterroge] = useState<string | null>(null);

  const compte = useRessource<Compte>(interroge);

  return (
    <>
      <Carte className="pb-5">
        <TitreDeCarte
          titre="Compte de facturation"
          sous="Le titulaire se nomme par son type et son identifiant, tels qu'ils figurent dans sa fiche."
        />

        <form
          className="mt-4 grid gap-4 px-5 sm:grid-cols-[180px_1fr_auto] sm:items-end"
          onSubmit={(evenement) => {
            evenement.preventDefault();
            const id = identifiant.trim();
            setInterroge(id ? `admin/v1/billing/accounts/${type}/${encodeURIComponent(id)}` : null);
          }}
        >
          <label className="block">
            <span className="mb-1.5 block text-sm font-medium text-encre-2">Type</span>
            <select
              value={type}
              onChange={(evenement) => setType(evenement.target.value)}
              className="creux w-full rounded-xl border-0 px-4 py-3 text-sm text-encre outline-none"
            >
              {TYPES.map((choix) => (
                <option key={choix.cle} value={choix.cle}>
                  {choix.libelle}
                </option>
              ))}
            </select>
          </label>

          <Champ
            libelle="Identifiant"
            value={identifiant}
            onChange={(evenement) => setIdentifiant(evenement.target.value)}
            placeholder="p-7, m-42…"
          />

          <Bouton type="submit">Afficher</Bouton>
        </form>
      </Carte>

      {compte.chargement ? <Chargement quoi="le compte" /> : null}

      {/* UN COMPTE ABSENT N'EST PAS UNE PANNE, C'EST LE CAS QU'ON VIENT TRAITER.
          Le service repond « introuvable » aussi bien pour un compte qui n'existe
          pas que pour un identifiant mal recopie — il ne distingue pas les deux,
          volontairement, pour qu'on ne puisse pas enumerer les comptes. L'ecran
          propose donc l'ouverture, sans affirmer laquelle des deux causes c'est. */}
      {!compte.chargement && compte.erreur ? (
        <Ouverture
          type={type}
          identifiant={identifiant.trim()}
          motif={compte.erreur}
          apres={compte.recharger}
        />
      ) : null}

      {!compte.chargement && compte.donnees ? (
        <>
          <div className="grid gap-4 sm:grid-cols-3">
            <CarteStat
              titre="Solde"
              valeur={xof(compte.donnees.balanceXof)}
              detail={`Seuil d'alerte : ${xof(compte.donnees.lowBalanceThresholdXof)}`}
            />
            <CarteStat
              titre="Disponible"
              valeur={xof(compte.donnees.availableXof)}
              detail="Solde + plafond de crédit"
            />
            <CarteStat
              titre="Régime"
              valeur={compte.donnees.mode === 'Postpaid' ? 'Postpayé' : 'Prépayé'}
              detail={compte.donnees.status === 'Suspended' ? 'Compte suspendu' : 'Compte actif'}
            />
          </div>

          <Recharger
            type={compte.donnees.ownerType}
            identifiant={compte.donnees.ownerId}
            apres={compte.recharger}
          />
        </>
      ) : null}

      {!compte.chargement && !compte.erreur && !compte.donnees && !interroge ? (
        <Vide message="Saisissez un titulaire pour voir son compte." />
      ) : null}
    </>
  );
}

/** Ouvrir un compte. Toujours en prepaye : le plafond vient ensuite. */
function Ouverture({
  type,
  identifiant,
  motif,
  apres,
}: {
  type: string;
  identifiant: string;
  motif: string;
  apres: () => void;
}) {
  const [seuil, setSeuil] = useState('5000');
  const [occupe, setOccupe] = useState(false);
  const [echec, setEchec] = useState<string | null>(null);

  if (!identifiant) return <Erreur message={motif} />;

  return (
    <Carte className="pb-5">
      <TitreDeCarte
        titre="Aucun compte pour ce titulaire"
        sous={motif}
      />

      <p className="mt-3 px-5 text-sm text-encre-2">
        L&apos;ouvrir permettra à {identifiant} de créer des courses, une fois son compte
        rechargé. Le compte naît à zéro et en prépayé ; un plafond de crédit se décide
        plus tard, sur un compte dont on a vu le comportement.
      </p>

      <div className="mt-4 grid gap-4 px-5 sm:grid-cols-[1fr_auto] sm:items-end">
        <Champ
          libelle="Seuil d'alerte (F CFA)"
          type="number"
          min={0}
          value={seuil}
          onChange={(evenement) => setSeuil(evenement.target.value)}
          aide="En dessous, le titulaire doit être prévenu. Zéro veut dire « ne jamais prévenir »."
        />

        <Bouton
          disabled={occupe}
          onClick={async () => {
            setOccupe(true);
            setEchec(null);

            try {
              await envoyer('admin/v1/billing/accounts', {
                ownerType: type,
                ownerId: identifiant,
                lowBalanceThresholdXof: Number(seuil) || 0,
              });
              apres();
            } catch (cause: unknown) {
              setEchec(cause instanceof Error ? cause.message : 'Ouverture impossible.');
            } finally {
              setOccupe(false);
            }
          }}
        >
          {occupe ? 'Ouverture…' : 'Ouvrir le compte'}
        </Bouton>
      </div>

      {echec ? <Erreur message={echec} /> : null}
    </Carte>
  );
}

/**
 * Porter au compte un virement recu.
 *
 * LA REFERENCE DU VIREMENT EST OBLIGATOIRE, ET ELLE FAIT PLUS QU'INFORMER :
 * c'est d'elle que le service derive la cle d'idempotence. Deux envois de la
 * meme reference ne creditent qu'une fois, dans toute la base — donc un double
 * clic, un rechargement de page ou une reprise apres coupure sont sans danger.
 *
 * EN REVANCHE, UNE REFERENCE MAL RECOPIEE EST UN AUTRE VIREMENT aux yeux du
 * systeme. C'est pour cela qu'elle est demandee deux fois avant l'envoi.
 */
function Recharger({
  type,
  identifiant,
  apres,
}: {
  type: string;
  identifiant: string;
  apres: () => void;
}) {
  const [montant, setMontant] = useState('');
  const [reference, setReference] = useState('');
  const [relecture, setRelecture] = useState('');
  const [occupe, setOccupe] = useState(false);
  const [echec, setEchec] = useState<string | null>(null);
  const [fait, setFait] = useState<Recharge | null>(null);

  const montantValide = Number(montant) > 0;
  const referencesIdentiques = reference.trim() !== '' && reference.trim() === relecture.trim();

  return (
    <Carte className="pb-5">
      <TitreDeCarte
        titre="Constater un virement"
        sous="HBA ne reçoit aucune notification de recharge : c'est la finance qui porte au compte ce qu'elle a vu arriver."
      />

      <div className="mt-4 grid gap-4 px-5 sm:grid-cols-3">
        <Champ
          libelle="Montant reçu (F CFA)"
          type="number"
          min={1}
          value={montant}
          onChange={(evenement) => setMontant(evenement.target.value)}
        />
        <Champ
          libelle="Référence du virement"
          value={reference}
          onChange={(evenement) => setReference(evenement.target.value)}
          aide="Celle de l'opérateur ou de la banque."
        />
        <Champ
          libelle="Référence, à nouveau"
          value={relecture}
          onChange={(evenement) => setRelecture(evenement.target.value)}
          aide="Une référence différente serait comptée comme un second virement."
        />
      </div>

      <div className="mt-4 flex flex-wrap items-center gap-4 px-5">
        <Bouton
          disabled={occupe || !montantValide || !referencesIdentiques}
          onClick={async () => {
            setOccupe(true);
            setEchec(null);
            setFait(null);

            try {
              const resultat = await envoyer<Recharge>(
                `admin/v1/billing/accounts/${type}/${encodeURIComponent(identifiant)}/credit`,
                { amountXof: Number(montant), reference: reference.trim() },
              );

              setFait(resultat);
              setMontant('');
              setReference('');
              setRelecture('');
              apres();
            } catch (cause: unknown) {
              setEchec(cause instanceof Error ? cause.message : 'Recharge impossible.');
            } finally {
              setOccupe(false);
            }
          }}
        >
          {occupe ? 'Enregistrement…' : 'Porter au compte'}
        </Bouton>

        {!referencesIdentiques && reference.trim() !== '' ? (
          <span className="text-sm text-encre-3">Les deux références doivent être identiques.</span>
        ) : null}
      </div>

      {fait ? (
        <p className="mt-4 px-5 text-sm text-encre-2">
          {xof(fait.amountXof)} portés au compte pour la référence {fait.reference}. Nouveau solde :{' '}
          {xof(fait.balanceAfterXof)}.
        </p>
      ) : null}

      {echec ? <Erreur message={echec} /> : null}
    </Carte>
  );
}
