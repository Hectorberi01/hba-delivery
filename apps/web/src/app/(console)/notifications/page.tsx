'use client';

import { useState } from 'react';
import {
  Bouton,
  Carte,
  CarteStat,
  Chargement,
  Erreur,
  Pastille,
  TitreDeCarte,
  Vide,
} from '@/composants/base';
import { dateHeure, depuis, nombre } from '@/lib/format';
import { CANAUX, COULEUR_TON, lireCanal, lireEnvoi } from '@/lib/statuts';
import { useAttente } from '@/lib/client/attente';
import { useRessource } from '@/lib/client/ressource';
import type { LigneNotification, PageDeNotifications } from '@/lib/types';

const PAR_PAGE = 50;

const STATUTS_FILTRE = [
  { valeur: 0, libelle: 'Tous' },
  { valeur: 2, libelle: 'Envoyés' },
  { valeur: 3, libelle: 'Échoués' },
  { valeur: 4, libelle: 'Ignorés' },
  { valeur: 1, libelle: 'En attente' },
] as const;

export default function PageNotifications() {
  const [canal, setCanal] = useState(0);
  const [statut, setStatut] = useState(0);
  const [destinataire, setDestinataire] = useState('');
  const [decalage, setDecalage] = useState(0);
  const [courante, setCourante] = useState<LigneNotification | null>(null);

  // LA RECHERCHE ATTEND LA FIN DE LA FRAPPE, comme dans l'annuaire : sans
  // cela, un numero de huit chiffres part en huit requetes.
  const fin = useAttente(destinataire.trim());

  const parametres = new URLSearchParams({
    pageSize: String(PAR_PAGE),
    offset: String(decalage),
  });
  if (canal) parametres.set('channel', String(canal));
  if (statut) parametres.set('status', String(statut));
  if (fin) parametres.set('recipient', fin);

  const journal = useRessource<PageDeNotifications>(`admin/v1/notifications?${parametres}`);

  const lignes = journal.donnees?.notifications ?? [];
  const total = journal.donnees?.total ?? 0;

  // LES COMPTEURS PORTENT SUR LA PAGE, ET LE DISENT. Un total par statut
  // demanderait une agregation que le service ne rend pas ; l'inventer a
  // partir de cinquante lignes donnerait un chiffre faux et credible.
  const echecs = lignes.filter((ligne) => lireEnvoi(ligne.status).libelle === 'Échoué').length;
  const ignores = lignes.filter((ligne) => lireEnvoi(ligne.status).libelle === 'Ignoré').length;

  function filtrer(action: () => void) {
    action();
    setDecalage(0);
    setCourante(null);
  }

  return (
    <>
      <div className="grid gap-4 sm:grid-cols-3">
        <CarteStat
          titre="Envois retenus"
          valeur={nombre(total)}
          detail="Sur l'ensemble du journal, filtres compris."
        />
        <CarteStat
          titre="Échecs sur cette page"
          valeur={nombre(echecs)}
          detail={`Refusés par l'opérateur, sur ${nombre(lignes.length)} lignes affichées.`}
        />
        <CarteStat
          titre="Ignorés sur cette page"
          valeur={nombre(ignores)}
          detail="Aucun fournisseur sur ce canal : rien n'est parti."
        />
      </div>

      <div className="flex flex-wrap items-center gap-2">
        <div className="relief-doux flex gap-1 rounded-xl p-1">
          {Object.entries(CANAUX).map(([valeur, libelle]) => (
            <button
              key={valeur}
              type="button"
              onClick={() => filtrer(() => setCanal(Number(valeur)))}
              aria-pressed={canal === Number(valeur)}
              className={`rounded-lg px-3 py-1.5 text-sm font-medium transition ${
                canal === Number(valeur) ? 'bg-marque text-white' : 'text-encre-2 hover:text-encre'
              }`}
            >
              {valeur === '0' ? 'Tous les canaux' : libelle}
            </button>
          ))}
        </div>

        <div className="relief-doux flex gap-1 rounded-xl p-1">
          {STATUTS_FILTRE.map((entree) => (
            <button
              key={entree.valeur}
              type="button"
              onClick={() => filtrer(() => setStatut(entree.valeur))}
              aria-pressed={statut === entree.valeur}
              className={`rounded-lg px-3 py-1.5 text-sm font-medium transition ${
                statut === entree.valeur ? 'bg-marque text-white' : 'text-encre-2 hover:text-encre'
              }`}
            >
              {entree.libelle}
            </button>
          ))}
        </div>

        {/* ON CHERCHE PAR LA FIN DU NUMERO, parce que c'est tout ce que
            l'ecran montre. Le support tape les chiffres que le client vient de
            lui dicter ; la console n'a jamais le numero complet. */}
        <input
          type="search"
          value={destinataire}
          onChange={(e) => filtrer(() => setDestinataire(e.target.value))}
          placeholder="Fin du numéro — 4 derniers chiffres…"
          className="creux w-64 rounded-xl border-0 px-4 py-2.5 text-sm text-encre outline-none placeholder:text-encre-3"
        />
      </div>

      <div className="grid min-h-0 flex-1 grid-rows-[minmax(0,1fr)] gap-5 xl:grid-cols-[1.5fr_1fr]">
        <Carte className="flex min-h-0 flex-col overflow-hidden">
          <TitreDeCarte
            titre="Journal des envois"
            sous={
              journal.donnees
                ? `${nombre(lignes.length)} affichés sur ${nombre(total)} — les plus récents d'abord`
                : 'SMS, WhatsApp, push et e-mail'
            }
          />

          {journal.chargement ? <Chargement quoi="du journal" /> : null}
          {journal.erreur ? <Erreur message={journal.erreur} /> : null}
          {!journal.chargement && !journal.erreur && lignes.length === 0 ? (
            <Vide message="Aucun envoi ne correspond à ces filtres." />
          ) : null}

          {lignes.length ? (
            <div className="min-h-0 flex-1 overflow-auto">
              <table className="w-full border-collapse text-sm">
                <thead>
                  <tr className="text-left text-xs uppercase tracking-wide text-encre-3">
                    <th className="sticky top-0 z-10 border-b border-bordure bg-surface px-5 py-2.5 font-medium">
                      Quand
                    </th>
                    <th className="sticky top-0 z-10 border-b border-bordure bg-surface px-3 py-2.5 font-medium">
                      Canal
                    </th>
                    <th className="sticky top-0 z-10 border-b border-bordure bg-surface px-3 py-2.5 font-medium">
                      Destinataire
                    </th>
                    <th className="sticky top-0 z-10 border-b border-bordure bg-surface px-3 py-2.5 font-medium">
                      Modèle
                    </th>
                    <th className="sticky top-0 z-10 border-b border-bordure bg-surface px-5 py-2.5 font-medium">
                      Résultat
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {lignes.map((ligne) => {
                    const resultat = lireEnvoi(ligne.status);

                    return (
                      <tr
                        key={ligne.id}
                        onClick={() => setCourante(ligne)}
                        className={`cursor-pointer border-b border-bordure transition hover:bg-plan ${
                          courante?.id === ligne.id ? 'bg-plan' : ''
                        }`}
                      >
                        <td className="px-5 py-3 text-encre-2">{depuis(ligne.createdAt)}</td>
                        <td className="px-3 py-3 text-encre-2">{lireCanal(ligne.channel)}</td>
                        <td className="chiffres-tabulaires px-3 py-3 text-encre">
                          {ligne.recipientMasked || '—'}
                        </td>
                        <td className="px-3 py-3 font-mono text-[13px] text-encre-2">
                          {ligne.templateId || '—'}
                        </td>
                        <td className="px-5 py-3">
                          <Pastille
                            couleur={COULEUR_TON[resultat.ton]}
                            libelle={resultat.libelle}
                          />
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          ) : null}

          {total > PAR_PAGE ? (
            <div className="flex shrink-0 items-center justify-between gap-3 border-t border-bordure px-5 py-3">
              <p className="text-sm text-encre-3">
                {nombre(decalage + 1)} à {nombre(Math.min(decalage + PAR_PAGE, total))} sur{' '}
                {nombre(total)}
              </p>
              <div className="flex gap-2">
                <Bouton
                  variante="discret"
                  disabled={decalage === 0}
                  onClick={() => setDecalage(Math.max(0, decalage - PAR_PAGE))}
                >
                  Précédents
                </Bouton>
                <Bouton
                  variante="discret"
                  disabled={decalage + PAR_PAGE >= total}
                  onClick={() => setDecalage(decalage + PAR_PAGE)}
                >
                  Suivants
                </Bouton>
              </div>
            </div>
          ) : null}
        </Carte>

        <div className="min-h-0 overflow-hidden">
          {courante ? <Detail ligne={courante} /> : <SansSelection />}
        </div>
      </div>
    </>
  );
}

function SansSelection() {
  return (
    <Carte className="flex min-h-0 flex-col pb-5">
      <TitreDeCarte titre="Détail de l'envoi" />
      <Vide message="Choisissez une ligne du journal." />
    </Carte>
  );
}

function Detail({ ligne }: { ligne: LigneNotification }) {
  const resultat = lireEnvoi(ligne.status);

  return (
    <Carte className="flex h-full min-h-0 flex-col overflow-hidden">
      <TitreDeCarte titre={ligne.templateId || 'Envoi'} sous={lireCanal(ligne.channel)} />

      <div className="min-h-0 flex-1 space-y-5 overflow-y-auto px-5 pb-5 pt-4">
        <dl className="space-y-2.5">
          <Ligne libelle="Résultat">
            <Pastille couleur={COULEUR_TON[resultat.ton]} libelle={resultat.libelle} />
          </Ligne>
          <Ligne libelle="Destinataire">
            <span className="chiffres-tabulaires">{ligne.recipientMasked || '—'}</span>
          </Ligne>
          <Ligne libelle="Déposé le">{dateHeure(ligne.createdAt)}</Ligne>
          <Ligne libelle="Terminé le">
            {ligne.completedAt ? dateHeure(ligne.completedAt) : 'Pas encore'}
          </Ligne>
          <Ligne libelle="Tentatives">{nombre(ligne.attempts ?? 0)}</Ligne>
          <Ligne libelle="Opérateur">{ligne.provider || '—'}</Ligne>
          {/* LA REFERENCE DE L'OPERATEUR EST CE QUI PERMET DE RAPPROCHER UNE
              FACTURE : c'est le seul identifiant commun entre leur relevé et
              notre journal. */}
          <Ligne libelle="Référence opérateur">
            <span className="font-mono text-[13px]">{ligne.providerMessageId || '—'}</span>
          </Ligne>
          <Ligne libelle="Corrélation">
            <span className="font-mono text-[13px]">{ligne.correlationId || '—'}</span>
          </Ligne>
        </dl>

        {ligne.error ? (
          <section>
            <h3 className="text-sm font-semibold text-encre">
              {resultat.libelle === 'Ignoré' ? 'Pourquoi rien n’est parti' : 'Ce que l’opérateur a répondu'}
            </h3>
            <p className="creux-doux mt-2 break-words rounded-xl px-4 py-3 text-sm text-encre-2">
              {ligne.error}
            </p>
          </section>
        ) : null}

        {/* CE QUI A ETE ECRIT N'EST PAS CONSERVE, et le dire evite qu'on le
            cherche. Un code de connexion lisible des mois plus tard serait une
            base de secrets perimes dont personne n'a besoin. */}
        <p className="text-xs text-encre-3">
          Le texte envoyé n&apos;est pas conservé : seuls le modèle et le destinataire le sont.
        </p>
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
