'use client';

import { useMemo, useState } from 'react';
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
import { dateHeure, depuis } from '@/lib/format';
import { COULEUR_TON, lireStatutCompte } from '@/lib/statuts';
import { envoyer, useRessource } from '@/lib/client/ressource';
import type { CompteBackOffice, EquipeBackOffice } from '@/lib/types';

/**
 * LES QUATRE ROLES SONT CEUX DU REFERENTIEL, A LA LETTRE. Ni « manager », ni
 * « superviseur » : un nom de role invente ici ne serait reconnu par aucun
 * service.
 */
const ROLES = ['admin', 'ops', 'support', 'finance'] as const;

type Panneau = { sorte: 'compte'; id: string } | { sorte: 'creation' };

export function EcranEquipe({ moi }: { moi: string }) {
  const [recherche, setRecherche] = useState('');
  const [panneau, setPanneau] = useState<Panneau | null>(null);

  const equipe = useRessource<EquipeBackOffice>('admin/v1/accounts/back-office?limit=200');
  const comptes = useMemo(() => equipe.donnees?.accounts ?? [], [equipe.donnees]);

  // LE FILTRE EST LOCAL, ET C'EST ASSUME. La route ne prend pas de terme de
  // recherche parce qu'une equipe de back-office tient en une page : filtrer
  // ici evite d'ouvrir un parametre que personne n'a demande. Si l'equipe
  // depassait le plafond de deux cents, il faudrait l'ouvrir pour de bon.
  const terme = recherche.trim().toLowerCase();
  const visibles = terme
    ? comptes.filter((compte) =>
        [compte.displayName, compte.email, ...(compte.roles ?? [])]
          .filter(Boolean)
          .some((champ) => String(champ).toLowerCase().includes(terme)),
      )
    : comptes;

  const courant =
    panneau?.sorte === 'compte'
      ? (comptes.find((compte) => compte.id === panneau.id) ?? null)
      : null;

  return (
    <div className="grid min-h-0 flex-1 grid-rows-[minmax(0,1fr)] gap-5 xl:grid-cols-[1.1fr_1fr]">
      <Carte className="flex min-h-0 flex-col overflow-hidden">
        <TitreDeCarte
          titre="L'équipe"
          sous={
            equipe.donnees
              ? `${comptes.length} compte${comptes.length > 1 ? 's' : ''} de back-office`
              : 'Comptes admin, ops, support et finance'
          }
          action={
            <div className="flex items-center gap-2">
              <input
                type="search"
                value={recherche}
                onChange={(e) => setRecherche(e.target.value)}
                placeholder="Nom, e-mail, rôle…"
                className="creux w-48 rounded-xl border-0 px-4 py-2.5 text-sm text-encre outline-none placeholder:text-encre-3"
              />
              <Bouton onClick={() => setPanneau({ sorte: 'creation' })}>Nouveau</Bouton>
            </div>
          }
        />

        {equipe.chargement ? <Chargement quoi="de l'équipe" /> : null}
        {equipe.erreur ? <Erreur message={equipe.erreur} /> : null}
        {!equipe.chargement && !equipe.erreur && visibles.length === 0 ? (
          <Vide
            message={
              terme
                ? `Personne ne correspond à « ${recherche.trim()} ».`
                : 'Aucun compte de back-office.'
            }
          />
        ) : null}

        {visibles.length ? (
          <div className="min-h-0 flex-1 overflow-y-auto px-5 pb-5 pt-4">
            <ul className="space-y-2">
              {visibles.map((compte) => {
                const statut = lireStatutCompte(compte.status);
                const actif = courant?.id === compte.id;

                return (
                  <li key={compte.id}>
                    <button
                      type="button"
                      onClick={() => setPanneau({ sorte: 'compte', id: compte.id })}
                      aria-current={actif ? 'true' : undefined}
                      className={`w-full rounded-xl px-4 py-3 text-left transition ${
                        actif ? 'creux' : 'relief-doux pressable'
                      }`}
                    >
                      <span className="flex items-center gap-3">
                        <Initiales nom={compte.displayName || compte.email || '?'} />
                        <span className="min-w-0 flex-1">
                          <span className="block truncate font-medium text-encre">
                            {compte.displayName || 'Sans nom'}
                            {compte.id === moi ? (
                              <span className="ml-2 text-xs font-normal text-encre-3">vous</span>
                            ) : null}
                          </span>
                          <span className="block truncate text-sm text-encre-3">
                            {compte.email || '—'}
                          </span>
                        </span>
                        <span className="flex shrink-0 flex-wrap justify-end gap-1.5">
                          {(compte.roles ?? []).map((role) => (
                            <Badge key={role} couleur={COULEUR_TON.neutre}>
                              {role}
                            </Badge>
                          ))}
                        </span>
                      </span>

                      {statut.ton !== 'bon' ? (
                        <span className="mt-2 block">
                          <Pastille couleur={COULEUR_TON[statut.ton]} libelle={statut.libelle} />
                        </span>
                      ) : null}
                    </button>
                  </li>
                );
              })}
            </ul>
          </div>
        ) : null}
      </Carte>

      <div className="min-h-0 overflow-hidden">
        {panneau?.sorte === 'creation' ? (
          <NouveauCompte
            cree={() => {
              equipe.recharger();
              setPanneau(null);
            }}
          />
        ) : courant ? (
          <FicheDuCompte
            compte={courant}
            moi={moi}
            change={equipe.recharger}
            key={courant.id}
          />
        ) : (
          <Carte className="flex min-h-0 flex-col pb-5">
            <TitreDeCarte titre="Fiche" sous="Qui administre la plateforme" />
            <Vide message="Choisissez un compte, ou créez-en un." />
          </Carte>
        )}
      </div>
    </div>
  );
}

function FicheDuCompte({
  compte,
  moi,
  change,
}: {
  compte: CompteBackOffice;
  moi: string;
  change: () => void;
}) {
  const [motif, setMotif] = useState('');
  const [envoi, setEnvoi] = useState(false);
  const [probleme, setProbleme] = useState<string | null>(null);

  const statut = lireStatutCompte(compte.status);
  const suspendu = statut.libelle === 'Suspendu';
  const soiMeme = compte.id === moi;

  async function agir(action: 'suspend' | 'reactivate') {
    setEnvoi(true);
    setProbleme(null);

    try {
      await envoyer(`admin/v1/accounts/${compte.id}/${action}`, { reason: motif });
      setMotif('');
      change();
    } catch (cause) {
      setProbleme(cause instanceof Error ? cause.message : 'Action refusée.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Carte className="flex h-full min-h-0 flex-col overflow-hidden">
      <TitreDeCarte titre={compte.displayName || 'Compte'} sous={compte.email || undefined} />

      <div className="min-h-0 flex-1 space-y-5 overflow-y-auto px-5 pb-5 pt-4">
        <dl className="space-y-2.5">
          <Ligne libelle="Identifiant">
            <span className="font-mono text-[13px]">{compte.id}</span>
          </Ligne>
          <Ligne libelle="Statut">
            <Pastille couleur={COULEUR_TON[statut.ton]} libelle={statut.libelle} />
          </Ligne>
          <Ligne libelle="Rôles">
            <span className="flex flex-wrap justify-end gap-1.5">
              {(compte.roles ?? []).map((role) => (
                <Badge key={role} couleur={COULEUR_TON.neutre}>
                  {role}
                </Badge>
              ))}
            </span>
          </Ligne>
          <Ligne libelle="Créé le">
            {compte.createdAt ? dateHeure(compte.createdAt) : '—'}
          </Ligne>
          {/* UNE ABSENCE DE CONNEXION N'EST PAS UNE DATE VIDE. Un compte creé
              et jamais utilisé se lit autrement qu'un compte dont on ignore la
              dernière visite. */}
          <Ligne libelle="Dernière connexion">
            {compte.lastLoginAt ? depuis(compte.lastLoginAt) : 'Jamais connecté'}
          </Ligne>
          <Ligne libelle="Mot de passe">
            {compte.hasPassword ? 'Défini' : 'Aucun — ce compte ne peut pas se connecter'}
          </Ligne>
        </dl>

        {compte.statusReason ? (
          <p className="creux-doux rounded-xl px-4 py-3 text-sm text-encre-2">
            Motif consigné : {compte.statusReason}
          </p>
        ) : null}

        <section className="border-t border-bordure pt-4">
          <h3 className="text-sm font-semibold text-encre">
            {suspendu ? 'Réactiver ce compte' : 'Suspendre ce compte'}
          </h3>

          {/* ON NE SE SUSPEND PAS SOI-MEME. Le service ne l'interdit pas
              aujourd'hui, et il le devrait : suspendre révoque toutes les
              sessions du compte visé, donc un administrateur seul se
              fermerait la porte sans pouvoir la rouvrir. En attendant cette
              décision, l'écran refuse le geste et dit pourquoi. */}
          {soiMeme ? (
            <p className="mt-2 text-sm text-encre-3">
              C&apos;est votre propre compte. Suspendre révoque toutes vos sessions : si vous êtes
              le seul administrateur, plus personne ne pourra vous réactiver.
            </p>
          ) : (
            <>
              <p className="mt-2 text-sm text-encre-2">
                {suspendu
                  ? 'La personne pourra se reconnecter immédiatement.'
                  : 'Toutes ses sessions sont fermées sur-le-champ.'}
              </p>

              <textarea
                value={motif}
                onChange={(e) => setMotif(e.target.value)}
                rows={2}
                placeholder={suspendu ? 'Motif (facultatif)' : 'Motif — obligatoire'}
                className="creux mt-3 w-full rounded-xl border-0 px-4 py-3 text-sm text-encre outline-none placeholder:text-encre-3"
              />

              <Bouton
                variante={suspendu ? 'principal' : 'danger'}
                className="mt-3"
                disabled={envoi || (!suspendu && motif.trim().length === 0)}
                onClick={() => agir(suspendu ? 'reactivate' : 'suspend')}
              >
                {suspendu ? 'Réactiver' : 'Suspendre'}
              </Bouton>
            </>
          )}

          {probleme ? (
            <p role="alert" className="mt-3 text-sm text-critique">
              {probleme}
            </p>
          ) : null}
        </section>
      </div>
    </Carte>
  );
}

/**
 * La création d'un compte de back-office.
 *
 * ELLE VIVAIT DANS PARAMETRES JUSQU'AU 2 OCTOBRE 2026. Voir l'équipe et
 * l'agrandir sont le même geste à deux moments ; les séparer obligeait à
 * changer d'écran pour vérifier qu'on ne créait pas un doublon.
 */
function NouveauCompte({ cree }: { cree: () => void }) {
  const [email, setEmail] = useState('');
  const [nom, setNom] = useState('');
  const [roles, setRoles] = useState<string[]>(['support']);
  const [motDePasse, setMotDePasse] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [probleme, setProbleme] = useState<string | null>(null);
  const [envoi, setEnvoi] = useState(false);

  function basculer(role: string) {
    setRoles((actuels) =>
      actuels.includes(role) ? actuels.filter((r) => r !== role) : [...actuels, role],
    );
  }

  async function creer() {
    setEnvoi(true);
    setMessage(null);
    setProbleme(null);

    try {
      await envoyer('admin/v1/accounts/back-office', {
        email,
        displayName: nom,
        roles,
        initialPassword: motDePasse,
      });

      setMessage(
        `Compte créé pour ${email}. Le mot de passe doit être changé à la première connexion.`,
      );
      setEmail('');
      setNom('');
      setMotDePasse('');
      cree();
    } catch (cause) {
      setProbleme(cause instanceof Error ? cause.message : 'Échec de la création.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Carte className="flex h-full min-h-0 flex-col overflow-hidden">
      <TitreDeCarte titre="Nouveau compte de back-office" sous="Rôle admin uniquement" />

      <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-5 pb-5 pt-4">
        <Champ
          libelle="E-mail"
          type="email"
          value={email}
          onChange={(e) => setEmail(e.target.value)}
        />
        <Champ libelle="Nom affiché" value={nom} onChange={(e) => setNom(e.target.value)} />

        <fieldset>
          <legend className="mb-2 text-sm font-medium text-encre-2">Rôles</legend>
          <div className="flex flex-wrap gap-2">
            {ROLES.map((role) => (
              <button
                key={role}
                type="button"
                onClick={() => basculer(role)}
                aria-pressed={roles.includes(role)}
                className={`rounded-xl px-3.5 py-2 text-sm transition ${
                  roles.includes(role)
                    ? 'creux font-medium text-marque'
                    : 'relief-doux pressable text-encre-2 hover:text-encre'
                }`}
              >
                {role}
              </button>
            ))}
          </div>
        </fieldset>

        <Champ
          libelle="Mot de passe initial"
          type="text"
          value={motDePasse}
          onChange={(e) => setMotDePasse(e.target.value)}
          aide="Transmis à la personne par un autre canal, puis changé par elle."
        />

        <Bouton
          disabled={envoi || !email || !nom || !roles.length || motDePasse.length < 8}
          onClick={creer}
        >
          Créer le compte
        </Bouton>

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

function Ligne({ libelle, children }: { libelle: string; children: React.ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-4">
      <dt className="shrink-0 text-sm text-encre-2">{libelle}</dt>
      <dd className="break-all text-right text-sm text-encre">{children}</dd>
    </div>
  );
}
