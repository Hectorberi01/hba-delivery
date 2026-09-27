'use client';

import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useState } from 'react';
import { Bouton, Champ } from '@/composants/base';

export function FormulaireDeConnexion() {
  const routeur = useRouter();
  const [login, setLogin] = useState('');
  const [motDePasse, setMotDePasse] = useState('');
  const [visible, setVisible] = useState(false);
  const [erreur, setErreur] = useState<string | null>(null);
  const [envoi, setEnvoi] = useState(false);

  async function soumettre(evenement: React.FormEvent) {
    evenement.preventDefault();
    setEnvoi(true);
    setErreur(null);

    try {
      const reponse = await fetch('/api/session/connexion', {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify({ login, motDePasse }),
      });

      if (!reponse.ok) {
        const corps = (await reponse.json().catch(() => null)) as { message?: string } | null;
        setErreur(corps?.message ?? 'Connexion refusée.');
        return;
      }

      routeur.replace('/tableau-de-bord');
      routeur.refresh();
    } catch {
      setErreur('La passerelle est injoignable.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <form onSubmit={soumettre} className="mt-7 space-y-4">
      {/* « login » et non « email » : Identity accepte l'e-mail OU le
          telephone, pour que l'utilisateur n'ait pas a se souvenir de ce
          qu'il a fourni a l'inscription. */}
      <Champ
        libelle="E-mail ou téléphone"
        type="text"
        name="login"
        autoComplete="username"
        required
        value={login}
        onChange={(e) => setLogin(e.target.value)}
        placeholder="vous@hba.delivery"
      />

      <div>
        <label className="block">
          <span className="mb-1.5 block text-sm font-medium text-encre-2">Mot de passe</span>
          <span className="relative block">
            <input
              type={visible ? 'text' : 'password'}
              name="password"
              autoComplete="current-password"
              required
              value={motDePasse}
              onChange={(e) => setMotDePasse(e.target.value)}
              className="creux w-full rounded-xl border-0 px-4 py-3 pr-11 text-sm text-encre outline-none focus:border-marque"
            />
            <button
              type="button"
              onClick={() => setVisible((v) => !v)}
              aria-label={visible ? 'Masquer le mot de passe' : 'Afficher le mot de passe'}
              className="absolute inset-y-0 right-0 px-3 text-xs font-medium text-encre-3 hover:text-encre-2"
            >
              {visible ? 'Masquer' : 'Afficher'}
            </button>
          </span>
        </label>
      </div>

      {erreur ? (
        <p role="alert" className="rounded-lg bg-[color-mix(in_srgb,var(--color-critique)_10%,white)] px-3 py-2 text-sm text-critique">
          {erreur}
        </p>
      ) : null}

      <Bouton type="submit" disabled={envoi} className="w-full py-2.5">
        {envoi ? 'Connexion…' : 'Se connecter'}
      </Bouton>

      <p className="text-center text-sm">
        <Link href="/mot-de-passe-oublie" className="text-marque hover:underline">
          Mot de passe oublié ?
        </Link>
      </p>
    </form>
  );
}
