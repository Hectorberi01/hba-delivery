'use client';

import { useState } from 'react';
import {
  Badge,
  Bouton,
  Carte,
  Champ,
  Chargement,
  Erreur,
  Pastille,
  TitreDeCarte,
  Vide,
} from '@/composants/base';
import { dateHeure, depuis, nombre } from '@/lib/format';
import { COULEUR_TON } from '@/lib/statuts';
import { envoyer, useRessource } from '@/lib/client/ressource';
import type { ListePartenaires, ResumePartenaire } from '@/lib/types';

/**
 * Les clients OAuth des partenaires B2B.
 *
 * ELLE VIVAIT DANS PARAMETRES JUSQU'AU 2 OCTOBRE 2026, sous l'onglet
 * « Integrations », et ne savait que creer : on pouvait ouvrir un client
 * partenaire sans jamais pouvoir verifier lesquels existaient. La liste est
 * arrivee avec la page.
 *
 * RESERVEE A admin. La passerelle l'exige et Identity revérifie (ADR 0007).
 */
export default function PagePartenaires() {
  const [panneau, setPanneau] = useState<{ sorte: 'fiche'; id: string } | { sorte: 'creation' } | null>(
    null,
  );

  const liste = useRessource<ListePartenaires>('admin/v1/partners?limit=200');
  const partenaires = liste.donnees?.partners ?? [];

  const courant =
    panneau?.sorte === 'fiche'
      ? (partenaires.find((p) => p.partnerId === panneau.id) ?? null)
      : null;

  return (
    <div className="grid min-h-0 flex-1 grid-rows-[minmax(0,1fr)] gap-5 xl:grid-cols-[1.1fr_1fr]">
      <Carte className="flex min-h-0 flex-col overflow-hidden">
        <TitreDeCarte
          titre="Partenaires"
          sous={
            liste.donnees
              ? `${nombre(partenaires.length)} client${partenaires.length > 1 ? 's' : ''} OAuth`
              : 'Systèmes tiers appelant l’API HBA'
          }
          action={<Bouton onClick={() => setPanneau({ sorte: 'creation' })}>Nouveau</Bouton>}
        />

        {liste.chargement ? <Chargement quoi="des partenaires" /> : null}
        {liste.erreur ? <Erreur message={liste.erreur} /> : null}
        {!liste.chargement && !liste.erreur && partenaires.length === 0 ? (
          <Vide message="Aucun client partenaire. Le B2B n’est ouvert à personne." />
        ) : null}

        {partenaires.length ? (
          <div className="min-h-0 flex-1 overflow-y-auto px-5 pb-5 pt-4">
            <ul className="space-y-2">
              {partenaires.map((partenaire) => (
                <li key={partenaire.partnerId}>
                  <button
                    type="button"
                    onClick={() => setPanneau({ sorte: 'fiche', id: partenaire.partnerId })}
                    aria-current={courant?.partnerId === partenaire.partnerId ? 'true' : undefined}
                    className={`w-full rounded-xl px-4 py-3 text-left transition ${
                      courant?.partnerId === partenaire.partnerId ? 'creux' : 'relief-doux pressable'
                    }`}
                  >
                    <span className="flex items-baseline justify-between gap-3">
                      <span className="truncate font-medium text-encre">
                        {partenaire.partnerName || 'Sans nom'}
                      </span>
                      <Badge couleur={COULEUR_TON.neutre}>{partenaire.source || '—'}</Badge>
                    </span>

                    <span className="mt-1 flex items-baseline justify-between gap-3">
                      <span className="truncate font-mono text-[13px] text-encre-3">
                        {partenaire.clientId || '—'}
                      </span>
                      <Pastille
                        couleur={partenaire.enabled ? COULEUR_TON.bon : COULEUR_TON.critique}
                        libelle={partenaire.enabled ? 'Actif' : 'Coupé'}
                      />
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </div>
        ) : null}
      </Carte>

      <div className="min-h-0 overflow-hidden">
        {panneau?.sorte === 'creation' ? (
          <NouveauPartenaire
            cree={() => {
              liste.recharger();
            }}
          />
        ) : courant ? (
          <Fiche partenaire={courant} key={courant.partnerId} />
        ) : (
          <Carte className="flex min-h-0 flex-col pb-5">
            <TitreDeCarte titre="Fiche partenaire" />
            <Vide message="Choisissez un partenaire, ou créez-en un." />
          </Carte>
        )}
      </div>
    </div>
  );
}

function Fiche({ partenaire }: { partenaire: ResumePartenaire }) {
  const [motif, setMotif] = useState('');
  const [secrets, setSecrets] = useState<PartenaireCree | null>(null);
  const [probleme, setProbleme] = useState<string | null>(null);
  const [envoi, setEnvoi] = useState(false);

  async function tourner() {
    setEnvoi(true);
    setProbleme(null);

    try {
      const tournes = await envoyer<PartenaireCree>(
        `admin/v1/partners/${partenaire.partnerId}/rotate-secret`,
        { reason: motif },
      );
      setSecrets(tournes);
      setMotif('');
    } catch (cause) {
      setProbleme(cause instanceof Error ? cause.message : 'Échec de la rotation.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Carte className="flex h-full min-h-0 flex-col overflow-hidden">
      <TitreDeCarte
        titre={partenaire.partnerName || 'Partenaire'}
        sous={partenaire.source || undefined}
      />

      <div className="min-h-0 flex-1 space-y-5 overflow-y-auto px-5 pb-5 pt-4">
        <dl className="space-y-2.5">
          <Ligne libelle="État">
            <Pastille
              couleur={partenaire.enabled ? COULEUR_TON.bon : COULEUR_TON.critique}
              libelle={partenaire.enabled ? 'Actif' : 'Coupé'}
            />
          </Ligne>
          {/* L'IDENTIFIANT CLIENT N'EST PAS UN SECRET : c'est ce que le
              partenaire présente, et le retrouver est la raison d'être de
              cette page. Le secret, lui, n'est lisible qu'à la création et à
              la rotation. */}
          <Ligne libelle="Client ID">
            <span className="font-mono text-[13px]">{partenaire.clientId || '—'}</span>
          </Ligne>
          <Ligne libelle="Partner ID">
            <span className="font-mono text-[13px]">{partenaire.partnerId}</span>
          </Ligne>
          <Ligne libelle="Webhook">{partenaire.webhookUrl || 'Aucun'}</Ligne>
          <Ligne libelle="Plafond">
            {nombre(partenaire.rateLimitPerMinute ?? 0)} appels par minute
          </Ligne>
          <Ligne libelle="Portées">
            {partenaire.scopes?.length ? (
              <span className="flex flex-wrap justify-end gap-1.5">
                {partenaire.scopes.map((portee) => (
                  <Badge key={portee} couleur={COULEUR_TON.neutre}>
                    {portee}
                  </Badge>
                ))}
              </span>
            ) : (
              'Aucune portée déclarée'
            )}
          </Ligne>
          <Ligne libelle="Créé le">{dateHeure(partenaire.createdAt)}</Ligne>
          <Ligne libelle="Secret tourné">
            {partenaire.secretRotatedAt ? depuis(partenaire.secretRotatedAt) : 'Jamais'}
          </Ligne>
        </dl>

        <section className="border-t border-bordure pt-4">
          <h3 className="text-sm font-semibold text-encre">Faire tourner le secret</h3>
          <p className="mt-2 text-sm text-encre-2">
            L&apos;ancien secret cesse de fonctionner immédiatement : le partenaire ne pourra plus
            obtenir de jeton tant qu&apos;il n&apos;aura pas le nouveau.
          </p>

          <textarea
            value={motif}
            onChange={(e) => setMotif(e.target.value)}
            rows={2}
            placeholder="Motif — obligatoire"
            className="creux mt-3 w-full rounded-xl border-0 px-4 py-3 text-sm text-encre outline-none placeholder:text-encre-3"
          />

          <Bouton
            variante="danger"
            className="mt-3"
            disabled={envoi || motif.trim().length === 0}
            onClick={tourner}
          >
            Faire tourner
          </Bouton>

          {probleme ? (
            <p role="alert" className="mt-3 text-sm text-critique">
              {probleme}
            </p>
          ) : null}

          {secrets ? (
            <div className="mt-4">
              <Secrets secrets={secrets} onFermer={() => setSecrets(null)} />
            </div>
          ) : null}
        </section>
      </div>
    </Carte>
  );
}

type PartenaireCree = {
  partnerId: string;
  clientId: string;
  clientSecret: string;
  webhookSigningSecret: string;
};

function NouveauPartenaire({ cree }: { cree: () => void }) {
  const [nom, setNom] = useState('');
  const [source, setSource] = useState('PARTNER_API');
  const [webhook, setWebhook] = useState('');
  const [secrets, setSecrets] = useState<PartenaireCree | null>(null);
  const [probleme, setProbleme] = useState<string | null>(null);
  const [envoi, setEnvoi] = useState(false);

  async function creer() {
    setEnvoi(true);
    setProbleme(null);

    try {
      const nouveau = await envoyer<PartenaireCree>('admin/v1/partners', {
        partnerName: nom,
        source,
        webhookUrl: webhook || null,
        scopes: [],
        rateLimitPerMinute: 120,
      });
      setSecrets(nouveau);
      setNom('');
      setWebhook('');
      cree();
    } catch (cause) {
      setProbleme(cause instanceof Error ? cause.message : 'Échec de la création.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Carte className="flex h-full min-h-0 flex-col overflow-hidden">
      <TitreDeCarte titre="Nouveau client OAuth" sous="Rôle admin uniquement" />

      <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-5 pb-5 pt-4">
        <Champ libelle="Nom du partenaire" value={nom} onChange={(e) => setNom(e.target.value)} />

        <label className="block">
          <span className="mb-1.5 block text-sm font-medium text-encre-2">Source</span>
          <select
            value={source}
            onChange={(e) => setSource(e.target.value)}
            className="creux w-full rounded-xl border-0 px-4 py-3 text-sm text-encre outline-none placeholder:text-encre-3"
          >
            <option value="HBA_EXPRESS">HBA_EXPRESS</option>
            <option value="HBA_FOOD">HBA_FOOD</option>
            <option value="PARTNER_API">PARTNER_API</option>
          </select>
        </label>

        <Champ
          libelle="URL de webhook"
          value={webhook}
          onChange={(e) => setWebhook(e.target.value)}
          placeholder="https://partenaire.example/hba/webhook"
        />

        <Bouton disabled={envoi || !nom} onClick={creer}>
          Créer le client
        </Bouton>

        {probleme ? (
          <p role="alert" className="text-sm text-critique">
            {probleme}
          </p>
        ) : null}

        {secrets ? <Secrets secrets={secrets} onFermer={() => setSecrets(null)} /> : null}
      </div>
    </Carte>
  );
}

function Secrets({ secrets, onFermer }: { secrets: PartenaireCree; onFermer: () => void }) {
  return (
    // AFFICHES UNE SEULE FOIS, ET L'ECRAN DOIT LE DIRE. Ni Identity ni la
    // console ne peuvent les relire : fermer ce bloc sans les copier oblige a
    // une rotation de secret.
    <div className="rounded-xl border border-attention bg-[color-mix(in_srgb,var(--color-attention)_10%,white)] p-4">
      <p className="text-sm font-semibold text-encre">
        Copiez ces deux secrets maintenant : ils ne seront plus jamais affichés.
      </p>

      <dl className="mt-3 space-y-2 text-sm">
        <LigneSecret terme="Partner ID" valeur={secrets.partnerId} />
        <LigneSecret terme="Client ID" valeur={secrets.clientId} />
        <LigneSecret terme="Client secret" valeur={secrets.clientSecret} />
        <LigneSecret terme="Secret de signature webhook" valeur={secrets.webhookSigningSecret} />
      </dl>

      <Bouton variante="discret" className="mt-4" onClick={onFermer}>
        J&apos;ai copié les secrets
      </Bouton>
    </div>
  );
}

function LigneSecret({ terme, valeur }: { terme: string; valeur: string }) {
  return (
    <div>
      <dt className="text-xs uppercase tracking-wide text-encre-3">{terme}</dt>
      <dd className="creux-doux mt-0.5 break-all rounded-lg px-2.5 py-1.5 font-mono text-[13px] text-encre">
        {valeur}
      </dd>
    </div>
  );
}

function Ligne({ libelle, children }: { libelle: string; children: React.ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-4">
      <dt className="shrink-0 text-sm text-encre-2">{libelle}</dt>
      <dd className="break-all text-right text-sm text-encre">{children}</dd>
    </div>
  );
}
