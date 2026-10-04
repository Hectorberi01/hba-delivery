'use client';

import { useEffect, useState } from 'react';
import { Badge, Bouton, Carte, Champ, Chargement, Erreur, Initiales, TitreDeCarte } from '@/composants/base';
import { COULEUR_TON } from '@/lib/statuts';
import { envoyer, useRessource } from '@/lib/client/ressource';

/**
 * Ce que rend GET /api/web/v1/auth/me — un hba.identity.v1.Principal, c'est-a-dire
 * ce que le jeton porte. NI DATE D'INSCRIPTION NI DERNIERE CONNEXION : le
 * principal sert a autoriser, pas a raconter un compte.
 */
type MonCompte = {
  subjectId?: string;
  displayName?: string;
  email?: string;
  phone?: string;
  roles?: string[];
};

export default function PageProfil() {
  const moi = useRessource<MonCompte>('web/v1/auth/me');

  return (
    <div className="mx-auto w-full max-w-2xl space-y-5">
      <Identite compte={moi.donnees} chargement={moi.chargement} erreur={moi.erreur} recharger={moi.recharger} />
      <Securite />
    </div>
  );
}

function Identite({
  compte,
  chargement,
  erreur,
  recharger,
}: {
  compte: MonCompte | null;
  chargement: boolean;
  erreur: string | null;
  recharger: () => void;
}) {
  const [nom, setNom] = useState('');
  const [envoi, setEnvoi] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [probleme, setProbleme] = useState<string | null>(null);

  // LE CHAMP SE REMPLIT QUAND LA REPONSE ARRIVE, et pas avant : initialiser
  // l'etat avec une valeur encore inconnue laisserait un champ vide que
  // l'utilisateur croirait etre son nom.
  useEffect(() => {
    if (compte?.displayName !== undefined) setNom(compte.displayName);
  }, [compte?.displayName]);

  async function enregistrer() {
    setEnvoi(true);
    setMessage(null);
    setProbleme(null);

    try {
      await envoyer('web/v1/auth/me', { displayName: nom.trim() }, { methode: 'PUT' });

      // LE JETON PORTE ENCORE L'ANCIEN NOM jusqu'a son renouvellement : un
      // claim ne se reecrit pas a distance. L'en-tete de la console lit le
      // cookie pose a la connexion, d'ou cette precision — sans elle, on croit
      // que l'enregistrement a echoue.
      setMessage('Nom enregistré. L’en-tête affichera le nouveau à votre prochaine connexion.');
      recharger();
    } catch (cause) {
      setProbleme(cause instanceof Error ? cause.message : 'Échec de l’enregistrement.');
    } finally {
      setEnvoi(false);
    }
  }

  const inchange = (compte?.displayName ?? '') === nom.trim();

  return (
    <Carte className="pb-5">
      <TitreDeCarte titre="Mon profil" sous="Ce que les autres écrans affichent de vous" />

      {chargement ? <Chargement quoi="de votre compte" /> : null}
      {erreur ? <Erreur message={erreur} /> : null}

      {compte ? (
        <div className="space-y-5 px-5 pt-4">
          <div className="flex items-center gap-4">
            <Initiales nom={compte.displayName || '?'} />
            <div className="min-w-0">
              <p className="truncate font-medium text-encre">{compte.displayName || 'Sans nom'}</p>
              <p className="flex flex-wrap gap-1.5 pt-1">
                {(compte.roles ?? []).map((role) => (
                  <Badge key={role} couleur={COULEUR_TON.neutre}>
                    {role}
                  </Badge>
                ))}
              </p>
            </div>
          </div>

          <Champ
            libelle="Nom affiché"
            value={nom}
            onChange={(e) => setNom(e.target.value)}
            aide="Entre 1 et 120 caractères. C’est le seul champ que vous pouvez changer vous-même."
          />

          <Bouton disabled={envoi || inchange || nom.trim().length === 0} onClick={enregistrer}>
            Enregistrer
          </Bouton>

          {message ? <p className="text-sm text-bon">{message}</p> : null}
          {probleme ? (
            <p role="alert" className="text-sm text-critique">
              {probleme}
            </p>
          ) : null}

          {/* L'E-MAIL EST UNE IDENTITE DE CONNEXION, PAS UNE PRESENTATION, et
              les roles sont des droits : ni l'un ni l'autre ne se changent
              depuis son propre compte. Les afficher en lecture evite d'avoir a
              demander « quel est mon e-mail, deja ? » a un administrateur. */}
          <dl className="space-y-2.5 border-t border-bordure pt-4">
            <Ligne libelle="E-mail de connexion">{compte.email || '—'}</Ligne>
            <Ligne libelle="Téléphone">
              <span className="chiffres-tabulaires">{compte.phone || '—'}</span>
            </Ligne>
            <Ligne libelle="Identifiant">
              <span className="font-mono text-[13px]">{compte.subjectId || '—'}</span>
            </Ligne>
          </dl>

          <p className="text-xs text-encre-3">
            Votre e-mail et vos rôles se changent par un administrateur, depuis la page Équipe.
          </p>
        </div>
      ) : null}
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
    <Carte className="pb-5">
      <TitreDeCarte titre="Sécurité" sous="Mot de passe et sessions" />
      <div className="space-y-4 px-5 pt-4">
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

function Ligne({ libelle, children }: { libelle: string; children: React.ReactNode }) {
  return (
    <div className="flex items-baseline justify-between gap-4">
      <dt className="shrink-0 text-sm text-encre-2">{libelle}</dt>
      <dd className="break-all text-right text-sm text-encre">{children}</dd>
    </div>
  );
}
