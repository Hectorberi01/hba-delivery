'use client';

import { useState } from 'react';
import {
  Badge,
  Bouton,
  Carte,
  Champ,
  Chargement,
  Erreur,
  Initiales,
  Pastille,
  TitreDeCarte,
  Vide,
} from '@/composants/base';
import { dateHeure, depuis, nombre } from '@/lib/format';
import { COULEUR_TON } from '@/lib/statuts';
import { useAttente } from '@/lib/client/attente';
import { envoyer, useRessource } from '@/lib/client/ressource';
import type { Commercant, PageDeCommercants } from '@/lib/types';

/**
 * Les commercants.
 *
 * LE SERVICE LES GERAIT, LA PASSERELLE LES EXPOSAIT, ET PERSONNE NE POUVAIT EN
 * CREER UN : Directory sait lister, lire, creer et modifier un commercant
 * depuis l'origine, les routes existent sous /api/admin/v1/merchants, et aucun
 * ecran ne s'en servait. Rien de neuf cote serveur pour cette page.
 *
 * COMMERCANT N'EST PAS PARTENAIRE. Un commercant est une ENTREPRISE, avec un
 * nom legal, un contact et des points de retrait ; un partenaire est un
 * SYSTEME qui appelle l'API en OAuth. Les deux pages restent separees pour que
 * la confusion n'ait pas d'endroit ou s'installer.
 */
export default function PageCommercants() {
  const [recherche, setRecherche] = useState('');
  const [actifsSeuls, setActifsSeuls] = useState(false);
  const [panneau, setPanneau] = useState<{ sorte: 'fiche'; id: string } | { sorte: 'creation' } | null>(
    null,
  );

  const terme = useAttente(recherche.trim());

  const parametres = new URLSearchParams({ pageSize: '50' });
  if (terme) parametres.set('query', terme);
  if (actifsSeuls) parametres.set('onlyActive', 'true');

  const annuaire = useRessource<PageDeCommercants>(`admin/v1/merchants?${parametres}`);
  const commercants = annuaire.donnees?.merchants ?? [];

  const courant =
    panneau?.sorte === 'fiche' ? (commercants.find((m) => m.id === panneau.id) ?? null) : null;

  return (
    <div className="grid min-h-0 flex-1 grid-rows-[minmax(0,1fr)] gap-5 xl:grid-cols-[1.1fr_1fr]">
      <Carte className="flex min-h-0 flex-col overflow-hidden">
        <TitreDeCarte
          titre="Commerçants"
          sous={
            annuaire.donnees?.total !== undefined
              ? `${nombre(commercants.length)} affichés sur ${nombre(annuaire.donnees.total)}`
              : 'Entreprises qui confient des courses'
          }
          action={
            <div className="flex items-center gap-2">
              <input
                type="search"
                value={recherche}
                onChange={(e) => setRecherche(e.target.value)}
                placeholder="Nom ou téléphone…"
                className="creux w-48 rounded-xl border-0 px-4 py-2.5 text-sm text-encre outline-none placeholder:text-encre-3"
              />
              <Bouton onClick={() => setPanneau({ sorte: 'creation' })}>Nouveau</Bouton>
            </div>
          }
        />

        <div className="shrink-0 px-5 pt-3">
          <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-encre-2">
            <input
              type="checkbox"
              checked={actifsSeuls}
              onChange={(e) => setActifsSeuls(e.target.checked)}
              className="h-4 w-4 rounded border-bordure accent-[var(--color-marque)]"
            />
            Masquer les commerçants coupés
          </label>
        </div>

        {annuaire.chargement ? <Chargement quoi="des commerçants" /> : null}
        {annuaire.erreur ? <Erreur message={annuaire.erreur} /> : null}
        {!annuaire.chargement && !annuaire.erreur && commercants.length === 0 ? (
          <Vide
            message={
              terme
                ? `Aucun commerçant ne correspond à « ${terme} ».`
                : 'Aucun commerçant référencé.'
            }
          />
        ) : null}

        {commercants.length ? (
          <div className="min-h-0 flex-1 overflow-y-auto px-5 pb-5 pt-4">
            <ul className="space-y-2">
              {commercants.map((commercant) => (
                <li key={commercant.id}>
                  <button
                    type="button"
                    onClick={() => setPanneau({ sorte: 'fiche', id: commercant.id })}
                    aria-current={courant?.id === commercant.id ? 'true' : undefined}
                    className={`w-full rounded-xl px-4 py-3 text-left transition ${
                      courant?.id === commercant.id ? 'creux' : 'relief-doux pressable'
                    }`}
                  >
                    <span className="flex items-center gap-3">
                      <Initiales nom={commercant.legalName ?? '?'} />
                      <span className="min-w-0 flex-1">
                        <span className="block truncate font-medium text-encre">
                          {commercant.legalName || 'Sans nom'}
                        </span>
                        <span className="chiffres-tabulaires block truncate text-sm text-encre-3">
                          {commercant.contactPhone || '—'}
                        </span>
                      </span>
                      <span className="shrink-0 text-right">
                        <Pastille
                          couleur={commercant.isActive ? COULEUR_TON.bon : COULEUR_TON.critique}
                          libelle={commercant.isActive ? 'Actif' : 'Coupé'}
                        />
                        <span className="mt-1 block text-xs text-encre-3">
                          {nombre(commercant.pickupPoints?.length ?? 0)} point
                          {(commercant.pickupPoints?.length ?? 0) > 1 ? 's' : ''} de retrait
                        </span>
                      </span>
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
          <NouveauCommercant
            cree={(id) => {
              annuaire.recharger();
              setPanneau({ sorte: 'fiche', id });
            }}
          />
        ) : courant ? (
          <Fiche commercant={courant} key={courant.id} />
        ) : (
          <Carte className="flex min-h-0 flex-col pb-5">
            <TitreDeCarte titre="Fiche commerçant" />
            <Vide message="Choisissez un commerçant, ou créez-en un." />
          </Carte>
        )}
      </div>
    </div>
  );
}

function Fiche({ commercant }: { commercant: Commercant }) {
  const points = commercant.pickupPoints ?? [];

  return (
    <Carte className="flex h-full min-h-0 flex-col overflow-hidden">
      <TitreDeCarte
        titre={commercant.legalName || 'Commerçant'}
        sous={commercant.contactName || undefined}
      />

      <div className="min-h-0 flex-1 space-y-5 overflow-y-auto px-5 pb-5 pt-4">
        <dl className="space-y-2.5">
          <Ligne libelle="État">
            <Pastille
              couleur={commercant.isActive ? COULEUR_TON.bon : COULEUR_TON.critique}
              libelle={commercant.isActive ? 'Actif' : 'Coupé'}
            />
          </Ligne>
          <Ligne libelle="Téléphone">
            <span className="chiffres-tabulaires">{commercant.contactPhone || '—'}</span>
          </Ligne>
          <Ligne libelle="E-mail">{commercant.contactEmail || '—'}</Ligne>
          {/* CE DELAI N'EST PAS DECORATIF : Dispatch s'en sert pour ne pas
              envoyer un livreur avant que la commande soit prête. Trop court,
              le livreur attend ; trop long, le plat refroidit. */}
          <Ligne libelle="Préparation">
            {nombre(commercant.averagePreparationMinutes ?? 0)} minutes en moyenne
          </Ligne>
          <Ligne libelle="Identifiant">
            <span className="font-mono text-[13px]">{commercant.id}</span>
          </Ligne>
          <Ligne libelle="Référencé le">
            {commercant.createdAt ? dateHeure(commercant.createdAt) : '—'}
          </Ligne>
        </dl>

        <section>
          <h3 className="text-sm font-semibold text-encre">Points de retrait</h3>

          {points.length ? (
            <ul className="mt-2 space-y-2">
              {points.map((point) => (
                <li key={point.id} className="creux-doux rounded-xl px-4 py-3">
                  <p className="flex items-baseline justify-between gap-3">
                    <span className="font-medium text-encre">{point.name || 'Sans nom'}</span>
                    <Badge couleur={point.isActive ? COULEUR_TON.bon : COULEUR_TON.neutre}>
                      {point.isActive ? 'ouvert' : 'fermé'}
                    </Badge>
                  </p>
                  <p className="mt-0.5 text-sm text-encre-2">
                    {point.address?.location?.landmark || 'Aucun repère enregistré'}
                  </p>
                </li>
              ))}
            </ul>
          ) : (
            <p className="mt-2 text-sm text-encre-3">
              Aucun point de retrait. Les courses de ce commerçant n’ont pas d’adresse de collecte.
            </p>
          )}

          {/* ILS SE GERENT DEPUIS LE PORTAIL COMMERCANT, et le dire evite de
              chercher un bouton qui n'existe pas : les routes d'ajout et de
              modification vivent sous /api/merchant/v1 et exigent le rôle
              merchant. La console les montre, elle ne les écrit pas. */}
          <p className="mt-2 text-xs text-encre-3">
            Ils s’ajoutent et se modifient depuis le portail commerçant, avec le compte ci-dessous.
          </p>
        </section>

        <AccesAuPortail commercant={commercant} />
      </div>
    </Carte>
  );
}

/**
 * L'acces au portail.
 *
 * REFERENCER UNE ENTREPRISE NE LUI DONNE PAS DE COMPTE. Ce sont deux gestes et
 * deux services : Directory porte le commercant, Identity porte le compte qui
 * s'y connecte. Sans ce second geste, le commercant existe et personne ne peut
 * ouvrir son portail — donc ni ajouter un point de retrait, ni voir ses
 * courses.
 *
 * CE QUE L'ECRAN NE SAIT PAS DIRE : si un compte existe déjà. Aucune route ne
 * liste les comptes d'un commercant — Identity sait les creer, pas les
 * retrouver par merchant_id. Creer deux fois avec le meme e-mail sera refuse
 * par Identity (EMAIL_ALREADY_USED), ce qui est la seule garantie disponible
 * aujourd'hui.
 */
function AccesAuPortail({ commercant }: { commercant: Commercant }) {
  const [ouvert, setOuvert] = useState(false);
  const [email, setEmail] = useState('');
  const [telephone, setTelephone] = useState('');
  const [nom, setNom] = useState('');
  const [role, setRole] = useState('merchant_owner');
  const [motDePasse, setMotDePasse] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [probleme, setProbleme] = useState<string | null>(null);
  const [envoi, setEnvoi] = useState(false);

  async function creer() {
    setEnvoi(true);
    setMessage(null);
    setProbleme(null);

    try {
      await envoyer('admin/v1/accounts/merchant', {
        merchantId: commercant.id,
        email: email || null,
        phone: telephone || null,
        displayName: nom,
        role,
        initialPassword: motDePasse,
      });

      setMessage(
        `Accès créé. Le mot de passe doit être changé à la première connexion du portail.`,
      );
      setEmail('');
      setTelephone('');
      setNom('');
      setMotDePasse('');
    } catch (cause) {
      setProbleme(cause instanceof Error ? cause.message : 'Échec de la création.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <section className="border-t border-bordure pt-4">
      <h3 className="text-sm font-semibold text-encre">Accès au portail</h3>
      <p className="mt-2 text-sm text-encre-2">
        Référencer l’entreprise ne lui donne pas de compte : le portail commerçant demande un
        identifiant, créé ici.
      </p>

      {ouvert ? (
        <div className="mt-3 space-y-4">
          <Champ libelle="Nom affiché" value={nom} onChange={(e) => setNom(e.target.value)} />
          <Champ
            libelle="E-mail"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            aide="E-mail ou téléphone : l’un des deux suffit, les deux valent mieux."
          />
          <Champ
            libelle="Téléphone"
            value={telephone}
            onChange={(e) => setTelephone(e.target.value)}
            placeholder="+229…"
          />

          <fieldset>
            <legend className="mb-2 text-sm font-medium text-encre-2">Rôle</legend>
            <div className="flex flex-wrap gap-2">
              {/* LES DEUX NOMS DU REFERENTIEL, A LA LETTRE. Le propriétaire peut
                  créer ses propres employés depuis le portail ; l'employé, non. */}
              {(['merchant_owner', 'merchant_staff'] as const).map((candidat) => (
                <button
                  key={candidat}
                  type="button"
                  onClick={() => setRole(candidat)}
                  aria-pressed={role === candidat}
                  className={`rounded-xl px-3.5 py-2 text-sm transition ${
                    role === candidat
                      ? 'creux font-medium text-marque'
                      : 'relief-doux pressable text-encre-2 hover:text-encre'
                  }`}
                >
                  {candidat}
                </button>
              ))}
            </div>
          </fieldset>

          <Champ
            libelle="Mot de passe initial"
            type="text"
            value={motDePasse}
            onChange={(e) => setMotDePasse(e.target.value)}
            aide="Transmis au commerçant par un autre canal, puis changé par lui."
          />

          <div className="flex gap-2">
            <Bouton
              disabled={envoi || !nom || (!email && !telephone) || motDePasse.length < 8}
              onClick={creer}
            >
              Créer l’accès
            </Bouton>
            <Bouton variante="discret" onClick={() => setOuvert(false)}>
              Annuler
            </Bouton>
          </div>
        </div>
      ) : (
        <Bouton variante="discret" className="mt-3" onClick={() => setOuvert(true)}>
          Créer un accès
        </Bouton>
      )}

      {message ? <p className="mt-3 text-sm text-bon">{message}</p> : null}
      {probleme ? (
        <p role="alert" className="mt-3 text-sm text-critique">
          {probleme}
        </p>
      ) : null}
    </section>
  );
}

function NouveauCommercant({ cree }: { cree: (id: string) => void }) {
  const [nom, setNom] = useState('');
  const [contact, setContact] = useState('');
  const [telephone, setTelephone] = useState('');
  const [email, setEmail] = useState('');
  const [preparation, setPreparation] = useState('15');
  const [probleme, setProbleme] = useState<string | null>(null);
  const [envoi, setEnvoi] = useState(false);

  async function creer() {
    setEnvoi(true);
    setProbleme(null);

    try {
      const nouveau = await envoyer<Commercant>('admin/v1/merchants', {
        legalName: nom,
        contactName: contact || null,
        contactPhone: telephone,
        contactEmail: email || null,
        averagePreparationMinutes: Number(preparation) || 0,
      });

      cree(nouveau.id);
    } catch (cause) {
      setProbleme(cause instanceof Error ? cause.message : 'Échec de la création.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Carte className="flex h-full min-h-0 flex-col overflow-hidden">
      <TitreDeCarte titre="Nouveau commerçant" sous="Une entreprise, pas un compte" />

      <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-5 pb-5 pt-4">
        <Champ
          libelle="Nom légal"
          value={nom}
          onChange={(e) => setNom(e.target.value)}
          aide="Celui du registre, pas l’enseigne : c’est lui qui figurera sur les factures."
        />
        <Champ
          libelle="Personne à contacter"
          value={contact}
          onChange={(e) => setContact(e.target.value)}
        />
        <Champ
          libelle="Téléphone"
          value={telephone}
          onChange={(e) => setTelephone(e.target.value)}
          placeholder="+229…"
        />
        <Champ
          libelle="E-mail"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
        <Champ
          libelle="Préparation moyenne (minutes)"
          type="number"
          min={0}
          value={preparation}
          onChange={(e) => setPreparation(e.target.value)}
          aide="Dispatch s’en sert pour ne pas envoyer un livreur avant que ce soit prêt."
        />

        <Bouton disabled={envoi || !nom || !telephone} onClick={creer}>
          Référencer le commerçant
        </Bouton>

        {/* L'ACCES AU PORTAIL SE CREE ENSUITE, DANS LA FICHE : deux services,
            deux gestes. On ouvre donc la fiche aussitot creee. */}
        <p className="text-xs text-encre-3">
          L’accès au portail se crée ensuite, depuis la fiche : référencer l’entreprise et lui
          donner un compte sont deux gestes distincts.
        </p>

        {probleme ? (
          <p role="alert" className="text-sm text-critique">
            {probleme}
          </p>
        ) : null}
      </div>
    </Carte>
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
