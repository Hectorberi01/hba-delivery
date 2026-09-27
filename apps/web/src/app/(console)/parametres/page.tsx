'use client';

import { useState } from 'react';
import { Bouton, Carte, Champ, LacuneApi, TitreDeCarte } from '@/composants/base';
import { envoyer } from '@/lib/client/ressource';

const SECTIONS = [
  { cle: 'securite', libelle: 'Sécurité' },
  { cle: 'equipe', libelle: 'Équipe et rôles' },
  { cle: 'integrations', libelle: 'Intégrations' },
  { cle: 'general', libelle: 'Général' },
  { cle: 'tarification', libelle: 'Tarification' },
  { cle: 'zones', libelle: 'Zones de livraison' },
  { cle: 'notifications', libelle: 'Notifications' },
] as const;

type Cle = (typeof SECTIONS)[number]['cle'];

export default function PageParametres() {
  const [section, setSection] = useState<Cle>('securite');

  return (
    <div className="grid gap-4 lg:grid-cols-[240px_minmax(0,1fr)]">
      <Carte className="h-fit p-2">
        <nav aria-label="Sections des parametres">
          <ul className="space-y-1">
            {SECTIONS.map((entree) => (
              <li key={entree.cle}>
                <button
                  type="button"
                  onClick={() => setSection(entree.cle)}
                  aria-current={section === entree.cle ? 'true' : undefined}
                  className={`w-full rounded-xl px-3.5 py-2.5 text-left text-sm font-medium transition ${
                    section === entree.cle
                      ? 'creux text-marque'
                      : 'text-encre-2 hover:text-encre'
                  }`}
                >
                  {entree.libelle}
                </button>
              </li>
            ))}
          </ul>
        </nav>
      </Carte>

      <div className="space-y-4">
        {section === 'securite' ? <Securite /> : null}
        {section === 'equipe' ? <Equipe /> : null}
        {section === 'integrations' ? <Integrations /> : null}
        {section === 'general' ? (
          <SectionEnAttente
            titre="Paramètres généraux"
            quoi="Nom de la plateforme, contact officiel, siège, devise, langue"
            route="GET / PUT /api/admin/v1/parametres"
          />
        ) : null}
        {section === 'tarification' ? (
          <SectionEnAttente
            titre="Tarification"
            quoi="Grilles tarifaires par zone et par type de course"
            route="GET / PUT /api/admin/v1/grilles"
          />
        ) : null}
        {section === 'zones' ? (
          <SectionEnAttente
            titre="Zones de livraison"
            quoi="Périmètres de service, rayons, activation par zone"
            route="GET / PUT /api/admin/v1/zones"
          />
        ) : null}
        {section === 'notifications' ? (
          <SectionEnAttente
            titre="Notifications"
            quoi="Canaux SMS et WhatsApp, modèles de message, alertes internes"
            route="GET / PUT /api/admin/v1/notifications"
          />
        ) : null}
      </div>
    </div>
  );
}

function SectionEnAttente({ titre, quoi, route }: { titre: string; quoi: string; route: string }) {
  return (
    <Carte>
      <TitreDeCarte titre={titre} sous="Pas encore servi par la passerelle" />
      <div className="px-5 pb-5 pt-4">
        <LacuneApi quoi={quoi} route={route} />
      </div>
    </Carte>
  );
}

function Securite() {
  const [actuel, setActuel] = useState('');
  const [nouveau, setNouveau] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [probleme, setProbleme] = useState<string | null>(null);
  const [envoi, setEnvoi] = useState(false);

  async function changer() {
    setEnvoi(true);
    setMessage(null);
    setProbleme(null);

    try {
      await envoyer('web/v1/auth/password', {
        currentPassword: actuel,
        newPassword: nouveau,
      });

      // CHANGER DE MOT DE PASSE FERME LES AUTRES SESSIONS. On le dit avant que
      // l'utilisateur ne se demande pourquoi son autre onglet l'a ejecte.
      setMessage('Mot de passe changé. Vos autres sessions sont fermées.');
      setActuel('');
      setNouveau('');
    } catch (cause) {
      setProbleme(cause instanceof Error ? cause.message : 'Échec du changement.');
    } finally {
      setEnvoi(false);
    }
  }

  async function revoquer() {
    setEnvoi(true);
    setMessage(null);
    setProbleme(null);

    try {
      await envoyer('web/v1/auth/sessions/revoke-all', {});
      setMessage('Toutes les sessions sont révoquées. Reconnectez-vous.');
      window.setTimeout(() => {
        window.location.href = '/connexion';
      }, 1500);
    } catch (cause) {
      setProbleme(cause instanceof Error ? cause.message : 'Échec de la révocation.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Carte>
      <TitreDeCarte titre="Sécurité du compte" sous="Mot de passe et sessions" />
      <div className="max-w-md space-y-4 px-5 pb-5 pt-4">
        <Champ
          libelle="Mot de passe actuel"
          type="password"
          autoComplete="current-password"
          value={actuel}
          onChange={(e) => setActuel(e.target.value)}
        />
        <Champ
          libelle="Nouveau mot de passe"
          type="password"
          autoComplete="new-password"
          value={nouveau}
          onChange={(e) => setNouveau(e.target.value)}
          aide="Changer de mot de passe ferme toutes vos autres sessions."
        />

        <Bouton disabled={envoi || !actuel || nouveau.length < 8} onClick={changer}>
          Changer le mot de passe
        </Bouton>

        <div className="border-t border-bordure pt-4">
          <p className="text-sm text-encre-2">
            Un doute sur un poste resté connecté quelque part ?
          </p>
          <Bouton variante="discret" disabled={envoi} onClick={revoquer} className="mt-2">
            Révoquer toutes mes sessions
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

const ROLES = ['admin', 'ops', 'support', 'finance'] as const;

function Equipe() {
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
      setMessage(`Compte cree pour ${email}. Le mot de passe doit être changé à la première connexion.`);
      setEmail('');
      setNom('');
      setMotDePasse('');
    } catch (cause) {
      setProbleme(cause instanceof Error ? cause.message : 'Échec de la création.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Carte>
      <TitreDeCarte titre="Créer un compte de back-office" sous="Rôle admin uniquement" />
      <div className="max-w-md space-y-4 px-5 pb-5 pt-4">
        {/* LES QUATRE ROLES SONT CEUX DU REFERENTIEL, A LA LETTRE. Ni « manager »,
            ni « superviseur » : un nom de role invente ici ne serait reconnu
            par aucun service. */}
        <Champ libelle="E-mail" type="email" value={email} onChange={(e) => setEmail(e.target.value)} />
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
          aide="Transmise à la personne par un autre canal, puis changée par elle."
        />

        <Bouton disabled={envoi || !email || !nom || !roles.length || motDePasse.length < 8} onClick={creer}>
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

type PartenaireCree = {
  partnerId: string;
  clientId: string;
  clientSecret: string;
  webhookSigningSecret: string;
};

function Integrations() {
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
      const cree = await envoyer<PartenaireCree>('admin/v1/partners', {
        partnerName: nom,
        source,
        webhookUrl: webhook || null,
        scopes: [],
        rateLimitPerMinute: 120,
      });
      setSecrets(cree);
      setNom('');
      setWebhook('');
    } catch (cause) {
      setProbleme(cause instanceof Error ? cause.message : 'Échec de la création.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Carte>
      <TitreDeCarte titre="Client OAuth partenaire" sous="Rôle admin uniquement" />
      <div className="max-w-xl space-y-4 px-5 pb-5 pt-4">
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
