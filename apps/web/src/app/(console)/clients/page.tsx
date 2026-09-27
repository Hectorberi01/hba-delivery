'use client';

import { useEffect, useMemo, useState } from 'react';
import {
  Carte,
  Chargement,
  Erreur,
  Initiales,
  Pastille,
  TitreDeCarte,
  Vide,
} from '@/composants/base';
import { montant } from '@/lib/agregats';
import { dateHeure, depuis, instant, nombre, xof } from '@/lib/format';
import { COULEUR_TON, lireStatut, STATUTS } from '@/lib/statuts';
import { useAttente } from '@/lib/client/attente';
import { useRessource } from '@/lib/client/ressource';
import type {
  AccesClient,
  FicheClient,
  JournalAcces,
  LigneAnnuaireClient,
  PageAnnuaireClients,
  PageDeCourses,
} from '@/lib/types';

export default function PageClients() {
  const [recherche, setRecherche] = useState('');
  const [selection, setSelection] = useState<string | null>(null);

  // LA RECHERCHE ATTEND LA FIN DE LA FRAPPE. Sans cette ligne, le terme
  // entre dans l'URL a chaque caractere et la page envoie autant de requetes
  // — sept inutiles pour un numero de huit chiffres, chacune etant une
  // lecture de donnees personnelles.
  const terme = useAttente(recherche.trim());
  const annuaire = useRessource<PageAnnuaireClients>(
    `admin/v1/customers?pageSize=50${terme ? `&query=${encodeURIComponent(terme)}` : ''}`,
  );

  const clients = annuaire.donnees?.customers ?? [];
  const courant = selection ?? clients[0]?.customerId ?? null;

  return (
    <>
      <div className="grid gap-5 xl:grid-cols-[1.25fr_1fr]">
        <Carte className="pb-5">
          <TitreDeCarte
            titre="Annuaire"
            sous={
              annuaire.donnees?.total !== undefined
                ? `${nombre(clients.length)} affichés sur ${nombre(annuaire.donnees.total)}`
                : 'Clients inscrits'
            }
            action={
              <input
                type="search"
                value={recherche}
                onChange={(e) => setRecherche(e.target.value)}
                placeholder="Nom ou téléphone…"
                className="creux w-64 rounded-xl border-0 px-4 py-2.5 text-sm text-encre outline-none placeholder:text-encre-3"
              />
            }
          />

          {annuaire.chargement ? <Chargement quoi="de l'annuaire" /> : null}
          {annuaire.erreur ? <Erreur message={annuaire.erreur} /> : null}
          {!annuaire.chargement && !annuaire.erreur && clients.length === 0 ? (
            <Vide
              message={
                terme
                  ? `Aucun client ne correspond à « ${terme} ».`
                  : 'Aucun client inscrit pour le moment.'
              }
            />
          ) : null}

          {clients.length ? (
            <ul className="mt-4 space-y-2 px-5">
              {clients.map((client) => (
                <li key={client.customerId}>
                  <button
                    type="button"
                    onClick={() => setSelection(client.customerId)}
                    aria-current={client.customerId === courant ? 'true' : undefined}
                    className={`w-full rounded-xl px-4 py-3 text-left transition ${
                      client.customerId === courant ? 'creux' : 'relief-doux pressable'
                    }`}
                  >
                    <span className="flex items-center gap-3">
                      <Initiales nom={client.displayName ?? '?'} />
                      <span className="min-w-0 flex-1">
                        <span className="block truncate font-medium text-encre">
                          {client.displayName || 'Sans nom'}
                        </span>
                        {/* LE NUMERO EST MASQUE DANS LA LISTE, complet dans la
                            fiche. Parcourir ne doit pas permettre de repartir
                            avec l'annuaire ; ouvrir une fiche, une par une, si. */}
                        <span className="chiffres-tabulaires block text-sm text-encre-3">
                          {client.phoneMasked || '—'}
                        </span>
                      </span>
                      <span className="shrink-0 text-xs text-encre-3">
                        {client.createdAt ? `Inscrit ${depuis(client.createdAt)}` : ''}
                      </span>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          ) : null}
        </Carte>

        <FicheDuClient identifiant={courant} />
      </div>

      <DepensesVuesDansLesCourses />
    </>
  );
}

function FicheDuClient({ identifiant }: { identifiant: string | null }) {
  const { donnees, chargement, erreur } = useRessource<FicheClient>(
    identifiant ? `admin/v1/customers/${identifiant}` : null,
  );

  if (!identifiant) {
    return (
      <Carte className="pb-5">
        <TitreDeCarte titre="Fiche client" />
        <Vide message="Choisissez un client dans l'annuaire." />
      </Carte>
    );
  }

  return (
    <Carte className="pb-5">
      <TitreDeCarte titre={donnees?.displayName || 'Fiche client'} sous="Identité et adresses" />

      {chargement ? <Chargement quoi="de la fiche" /> : null}
      {erreur ? <Erreur message={erreur} /> : null}

      {donnees ? (
        <div className="space-y-5 px-5 pt-4">
          <dl className="space-y-2.5">
            <Ligne libelle="Identifiant">
              <span className="font-mono text-[13px]">{donnees.customerId}</span>
            </Ligne>
            <Ligne libelle="Téléphone">
              <span className="chiffres-tabulaires">{donnees.phone || '—'}</span>
            </Ligne>
            <Ligne libelle="E-mail">
              {donnees.emailHidden ? <Masque /> : (donnees.email ?? '—')}
            </Ligne>
            <Ligne libelle="Inscrit le">
              {donnees.createdAt ? dateHeure(donnees.createdAt) : '—'}
            </Ligne>
          </dl>

          <section>
            <h3 className="text-sm font-semibold text-encre">Adresses enregistrées</h3>

            {donnees.addressesHidden ? (
              <p className="mt-2 text-sm text-encre-3">
                <Masque /> — votre rôle ne donne pas accès aux adresses enregistrées. Ce client en
                a peut-être.
              </p>
            ) : donnees.favoriteAddresses?.length ? (
              <ul className="mt-2 space-y-2">
                {donnees.favoriteAddresses.map((adresse) => (
                  <li key={adresse.id} className="creux-doux rounded-xl px-4 py-3">
                    <p className="text-sm font-medium text-encre">
                      {adresse.label || 'Sans libellé'}
                      {adresse.isDefault ? (
                        <span className="ml-2 text-xs font-normal text-encre-3">par défaut</span>
                      ) : null}
                    </p>
                    <p className="mt-0.5 text-sm text-encre-2">{adresse.landmark || '—'}</p>
                    <p className="mt-0.5 text-xs text-encre-3">
                      {[adresse.contactName, adresse.phone].filter(Boolean).join(' · ') || '—'}
                    </p>
                  </li>
                ))}
              </ul>
            ) : (
              <p className="mt-2 text-sm text-encre-3">Ce client n&apos;a enregistré aucune adresse.</p>
            )}
          </section>

          <HistoriqueDuClient identifiant={donnees.customerId} />

          <FactureDuClient identifiant={donnees.customerId} />

          <JournalDesAcces identifiant={donnees.customerId} />
        </div>
      ) : null}
    </Carte>
  );
}

/**
 * Les courses de ce client.
 *
 * LE FILTRE N'ELARGIT RIEN. Le back-office lisait deja toutes les courses ;
 * customerId ne fait que retrecir, et le service impose de toute facon le
 * perimetre de l'appelant par-dessus.
 */
function HistoriqueDuClient({ identifiant }: { identifiant: string }) {
  const { donnees, chargement, erreur } = useRessource<PageDeCourses>(
    `admin/v1/deliveries?customerId=${encodeURIComponent(identifiant)}&pageSize=20`,
  );

  const courses = donnees?.deliveries ?? [];

  return (
    <section>
      <h3 className="text-sm font-semibold text-encre">Courses de ce client</h3>

      {chargement ? <p className="mt-2 text-sm text-encre-3">Chargement des courses…</p> : null}
      {erreur ? (
        <p role="alert" className="mt-2 text-sm text-critique">
          {erreur}
        </p>
      ) : null}

      {!chargement && !erreur && courses.length === 0 ? (
        <p className="mt-2 text-sm text-encre-3">Aucune course pour ce client.</p>
      ) : null}

      {courses.length ? (
        <>
          <ul className="mt-2 space-y-2">
            {courses.map((course) => {
              const statut = STATUTS[lireStatut(course.status)];

              return (
                <li key={course.id} className="creux-doux rounded-xl px-4 py-3">
                  <div className="flex items-baseline justify-between gap-3">
                    <span className="font-mono text-[13px] text-encre-2">
                      {course.reference || course.id}
                    </span>
                    <span className="chiffres-tabulaires text-sm font-medium text-encre">
                      {xof(montant(course))}
                    </span>
                  </div>
                  <div className="mt-1 flex items-baseline justify-between gap-3">
                    <Pastille couleur={COULEUR_TON[statut.ton]} libelle={statut.libelle} />
                    <span className="text-xs text-encre-3">{depuis(course.createdAt)}</span>
                  </div>
                </li>
              );
            })}
          </ul>

          {/* VINGT COURSES, PAS TOUT L'HISTORIQUE. Le dire evite qu'un total
              mental fait sur cette liste soit pris pour une depense totale. */}
          <p className="mt-2 text-xs text-encre-3">
            Les {courses.length === 20 ? '20' : nombre(courses.length)} dernières courses
            {courses.length === 20 ? ' — il peut y en avoir davantage' : ''}.
          </p>
        </>
      ) : null}
    </section>
  );
}

/**
 * Le cumul facture, reserve a l'administration.
 *
 * LE 403 N'EST PAS UNE ERREUR ICI, c'est la reponse attendue pour ops et
 * support. On ne duplique pas la regle de role cote navigateur — elle vit
 * dans le service — on se contente de lire ce qu'il repond.
 *
 * FACTURE, JAMAIS PAYE. L'etiquette le dit parce que le chiffre ignore les
 * impayes et les remboursements : ceux-ci vivent dans Payment, qui agrege par
 * payeur et non par client.
 */
function FactureDuClient({ identifiant }: { identifiant: string }) {
  const [etat, setEtat] = useState<
    { sorte: 'chargement' } | { sorte: 'masque' } | { sorte: 'erreur'; message: string } | { sorte: 'ok'; courses: number; total: number }
  >({ sorte: 'chargement' });

  useEffect(() => {
    let vivant = true;
    setEtat({ sorte: 'chargement' });

    fetch(`/api/hba/admin/v1/customers/${encodeURIComponent(identifiant)}/billing`, {
      headers: { accept: 'application/json' },
    })
      .then(async (reponse) => {
        if (!vivant) return;

        if (reponse.status === 403) {
          setEtat({ sorte: 'masque' });
          return;
        }

        if (!reponse.ok) {
          setEtat({ sorte: 'erreur', message: (await reponse.text()) || `Erreur ${reponse.status}` });
          return;
        }

        const corps = (await reponse.json()) as { deliveredCount?: number; billedTotalXof?: number };
        setEtat({
          sorte: 'ok',
          courses: corps.deliveredCount ?? 0,
          total: corps.billedTotalXof ?? 0,
        });
      })
      .catch(() => {
        if (vivant) setEtat({ sorte: 'erreur', message: 'Lecture impossible.' });
      });

    return () => {
      vivant = false;
    };
  }, [identifiant]);

  return (
    <section>
      <h3 className="text-sm font-semibold text-encre">Facturé</h3>

      {etat.sorte === 'chargement' ? (
        <p className="mt-2 text-sm text-encre-3">Chargement…</p>
      ) : null}

      {etat.sorte === 'masque' ? (
        <p className="mt-2 text-sm text-encre-3">
          <Masque /> — le cumul en argent est réservé à l&apos;administration. Les courses
          ci-dessus restent visibles.
        </p>
      ) : null}

      {etat.sorte === 'erreur' ? (
        <p role="alert" className="mt-2 text-sm text-critique">
          {etat.message}
        </p>
      ) : null}

      {etat.sorte === 'ok' ? (
        <div className="creux-doux mt-2 rounded-xl px-4 py-3">
          <p className="chiffres-tabulaires text-[22px] font-semibold leading-none text-encre">
            {xof(etat.total)}
          </p>
          <p className="mt-1.5 text-xs text-encre-3">
            Sur {nombre(etat.courses)} course{etat.courses > 1 ? 's' : ''} livrée
            {etat.courses > 1 ? 's' : ''} — prix facturé, pas encaissé : impayés et remboursements
            n&apos;y figurent pas.
          </p>
        </div>
      ) : null}
    </section>
  );
}

/**
 * Qui a ouvert cette fiche.
 *
 * UN JOURNAL QUE PERSONNE NE REGARDE NE DISSUADE PERSONNE : c'est pour cela
 * qu'il est ici, a cote de la donnee qu'il protege, et pas seulement en base.
 *
 * ADMIN SEULEMENT, comme le cumul : ops et support figurent dans ce journal,
 * leur en donner la main reviendrait a les laisser verifier ce qu'on sait
 * d'eux. Un 403 se rend donc en silence — inutile d'annoncer a ops
 * l'existence d'une section qu'il ne verra jamais.
 */
function JournalDesAcces({ identifiant }: { identifiant: string }) {
  const [entrees, setEntrees] = useState<AccesClient[] | null>(null);
  const [masque, setMasque] = useState(false);

  useEffect(() => {
    let vivant = true;
    setEntrees(null);
    setMasque(false);

    fetch(`/api/hba/admin/v1/customers/${encodeURIComponent(identifiant)}/access-log?limit=20`, {
      headers: { accept: 'application/json' },
    })
      .then(async (reponse) => {
        if (!vivant) return;
        if (reponse.status === 403) {
          setMasque(true);
          return;
        }
        if (!reponse.ok) return;

        const corps = (await reponse.json()) as JournalAcces;
        setEntrees(corps.entries ?? []);
      })
      .catch(() => {
        /* Le journal est secondaire : son absence ne doit pas alarmer. */
      });

    return () => {
      vivant = false;
    };
  }, [identifiant]);

  if (masque || entrees === null) return null;

  return (
    <section>
      <h3 className="text-sm font-semibold text-encre">Qui a ouvert cette fiche</h3>

      {entrees.length === 0 ? (
        <p className="mt-2 text-sm text-encre-3">Aucune ouverture enregistrée.</p>
      ) : (
        <ul className="mt-2 space-y-1.5">
          {entrees.map((acces, rang) => (
            <li
              key={`${acces.readerId}-${acces.readAt ?? rang}`}
              className="flex items-baseline justify-between gap-3 text-sm"
            >
              <span className="min-w-0 truncate text-encre-2">
                <span className="font-mono text-[13px]">{acces.readerId.slice(0, 8)}</span>
                {acces.readerRoles ? (
                  <span className="text-encre-3"> · {acces.readerRoles}</span>
                ) : null}
              </span>
              <span className="shrink-0 text-xs text-encre-3">{depuis(acces.readAt)}</span>
            </li>
          ))}
        </ul>
      )}

      {/* CE JOURNAL N'EST PAS COMPLET, ET LE TAIRE SERAIT PIRE QUE DE NE PAS
          L'AFFICHER. Chaque service garde son journal comme il garde ses
          donnees : la lecture du cumul est tracee dans Delivery, celle d'un
          dossier KYC dans Driver, et aucune route ne les reunit encore. */}
      <p className="mt-2 text-xs text-encre-3">
        Ouvertures de fiche uniquement. Les lectures du cumul facturé et des dossiers livreurs sont
        tracées dans leurs services respectifs, et aucune route ne les réunit encore.
      </p>
    </section>
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

/** Masqué, et dit comme tel : « vide » se lirait comme « rien à voir ». */
function Masque() {
  return <span className="text-encre-3">Masqué pour votre rôle</span>;
}

/**
 * CE TABLEAU N'EST PAS L'ANNUAIRE, et son titre le dit.
 *
 * Il agrege la PAGE de courses chargee, pas tout l'historique : un client qui
 * n'apparait pas ici peut avoir commande la semaine derniere. Il reste
 * jusqu'a ce que Delivery sache filtrer par client, parce qu'il est la seule
 * source de depenses aujourd'hui.
 */
function DepensesVuesDansLesCourses() {
  const { donnees, chargement, erreur } = useRessource<PageDeCourses>(
    'admin/v1/deliveries?pageSize=200',
  );

  const lignes = useMemo(() => {
    // « derniere » est un instant en millisecondes, pas la valeur brute de
    // l'API : c'est ce qui se compare. Comparer deux { seconds, nanos } avec
    // « > » ne rend jamais vrai, et la colonne restait vide.
    const parClient = new Map<
      string,
      { identifiant: string; courses: number; total: number; derniere?: number }
    >();

    for (const course of donnees?.deliveries ?? []) {
      const cle = course.customerId || course.merchantId;
      if (!cle) continue;

      const existante = parClient.get(cle) ?? { identifiant: cle, courses: 0, total: 0 };
      existante.courses += 1;
      existante.total += montant(course);

      const creee = instant(course.createdAt);
      if (creee !== null && (existante.derniere === undefined || creee > existante.derniere)) {
        existante.derniere = creee;
      }

      parClient.set(cle, existante);
    }

    return [...parClient.values()].sort((a, b) => b.courses - a.courses);
  }, [donnees]);

  return (
    <Carte className="mt-5 overflow-hidden">
      <TitreDeCarte
        titre="Dépenses vues dans les courses"
        sous="Agrégé sur la page de courses chargée, pas sur tout l'historique"
      />

      {chargement ? <Chargement quoi="des courses" /> : null}
      {erreur ? <Erreur message={erreur} /> : null}
      {!chargement && !erreur && !lignes.length ? (
        <Vide message="Aucun donneur d'ordre sur cette page." />
      ) : null}

      {lignes.length ? (
        <div className="mt-4 overflow-x-auto">
          <table className="w-full border-collapse text-sm">
            <thead>
              <tr className="border-y border-bordure bg-plan text-left text-xs uppercase tracking-wide text-encre-3">
                <th className="px-5 py-2.5 font-medium">Identifiant</th>
                <th className="px-3 py-2.5 text-right font-medium">Courses</th>
                <th className="px-3 py-2.5 text-right font-medium">Total</th>
                <th className="px-5 py-2.5 text-right font-medium">Dernière course</th>
              </tr>
            </thead>
            <tbody>
              {lignes.map((ligne) => (
                <tr key={ligne.identifiant} className="border-b border-bordure hover:bg-plan">
                  <td className="px-5 py-3 font-mono text-[13px] text-encre-2">{ligne.identifiant}</td>
                  <td className="chiffres-tabulaires px-3 py-3 text-right text-encre">
                    {nombre(ligne.courses)}
                  </td>
                  <td className="chiffres-tabulaires px-3 py-3 text-right font-medium text-encre">
                    {xof(ligne.total)}
                  </td>
                  <td className="px-5 py-3 text-right text-encre-3">{depuis(ligne.derniere)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </Carte>
  );
}
