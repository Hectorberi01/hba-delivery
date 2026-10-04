'use client';

import { useMemo, useState } from 'react';
import Link from 'next/link';
import {
  Badge,
  Carte,
  CarteStat,
  Chargement,
  Erreur,
  Initiales,
  TitreDeCarte,
  Vide,
} from '@/composants/base';
import { libelleVehicule, livreursVus, type LivreurVu } from '@/lib/agregats';
import { depuis, nombre, val } from '@/lib/format';
import {
  COULEUR_TON_LIVREUR,
  estDisponible,
  estEnAttenteDeKyc,
  estEnService,
  lireOperationnel,
} from '@/lib/statuts';
import { useRessource } from '@/lib/client/ressource';
import type { Nombre, PageDeCourses, PageDeLivreurs, StatsLivreurs } from '@/lib/types';

export default function PageLivreurs() {
  // DEUX SOURCES, ET AUCUNE NE SAIT CE QUE SAIT L'AUTRE. L'annuaire vient du
  // service Driver : qui existe, dans quel etat, avec quel vehicule. Les
  // courses viennent de Delivery : qui a roule, et avec quel resultat. Le
  // service Driver ne compte pas les livraisons, et c'est voulu — il n'a pas
  // a savoir ce qu'est une course.
  const annuaire = useRessource<PageDeLivreurs>('admin/v1/drivers?pageSize=200');
  const activite = useRessource<PageDeCourses>('admin/v1/deliveries?pageSize=200');

  // LES TUILES NE COMPTENT PLUS LA PAGE CHARGEE. L'annuaire s'arrete a 200
  // lignes : compter dedans donnait un « 200 livreurs » qui aurait ete faux
  // des le 201e. Le service Driver compte sa propre base, l'ecran l'affiche.
  const compteurs = useRessource<StatsLivreurs>('admin/v1/kpi/drivers');

  const [filtre, setFiltre] = useState('');

  const livreurs = useMemo(() => annuaire.donnees?.drivers ?? [], [annuaire.donnees]);
  const courses = useMemo(() => activite.donnees?.deliveries ?? [], [activite.donnees]);

  const parIdentifiant = useMemo(() => {
    const table = new Map<string, LivreurVu>();
    for (const vu of livreursVus(courses)) table.set(vu.identifiant, vu);
    return table;
  }, [courses]);

  const visibles = useMemo(() => {
    const terme = filtre.trim().toLowerCase();
    if (!terme) return livreurs;
    return livreurs.filter(
      (livreur) =>
        (livreur.displayName ?? '').toLowerCase().includes(terme) ||
        (livreur.phone ?? '').toLowerCase().includes(terme) ||
        livreur.id.toLowerCase().includes(terme),
    );
  }, [livreurs, filtre]);

  const stats = compteurs.donnees;
  const total = stats ? val(stats.total) : null;
  const enService = sommeSi(stats?.byOperational, estEnService);
  const disponibles = sommeSi(stats?.byOperational, estDisponible);
  const enAttente = sommeSi(stats?.byVerification, estEnAttenteDeKyc);

  // « — » PLUTOT QU'UN ZERO : si le service ne repond pas, l'ecran dit qu'il
  // ne sait pas. Un zero se lirait comme « aucun livreur en service ».
  const chiffre = (n: number | null) => (n === null ? '—' : nombre(n));
  const source = compteurs.erreur ? 'Service indisponible' : null;

  // Le total de l'annuaire reste celui de la liste paginee : c'est lui qui
  // legende « X sur Y » sous le filtre, et il parle bien des lignes lues.
  const totalAnnuaire = annuaire.donnees?.total ?? livreurs.length;

  return (
    <>
      {/* PAS DE « NOTE MOYENNE » : aucune note n'existe dans le domaine. La
          quatrieme tuile compte les dossiers en attente — c'est la seule
          chose que ce chiffre puisse dire de vrai, et c'est une file
          d'attente sur laquelle quelqu'un doit agir. */}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <CarteStat
          titre="Livreurs"
          valeur={chiffre(total)}
          detail={source ?? 'Inscrits sur la plateforme'}
        />
        <CarteStat
          titre="En service"
          valeur={chiffre(enService)}
          detail={source ?? 'Hors ligne exclus'}
        />
        <CarteStat
          titre="Disponibles"
          valeur={chiffre(disponibles)}
          detail={source ?? 'Prêts à recevoir une offre'}
        />
        <CarteStat
          titre="En attente de KYC"
          valeur={chiffre(enAttente)}
          detail={source ?? 'Dossiers à examiner'}
        />
      </div>

      {/* L'ANNUAIRE PREND TOUTE LA LARGEUR DEPUIS LE 2 OCTOBRE 2026, le dossier
          ayant sa propre page. Les vignettes passent donc de deux a quatre
          colonnes sur un grand ecran. */}
      <Carte>
          <TitreDeCarte
            titre="Annuaire"
            sous={`${nombre(visibles.length)} sur ${nombre(totalAnnuaire)}`}
            action={
              <input
                type="search"
                value={filtre}
                onChange={(e) => setFiltre(e.target.value)}
                placeholder="Filtrer par nom ou téléphone…"
                className="creux w-60 rounded-xl border-0 px-4 py-2.5 text-sm text-encre outline-none placeholder:text-encre-3"
              />
            }
          />

          {annuaire.chargement ? <Chargement quoi="de l'annuaire" /> : null}
          {annuaire.erreur ? <Erreur message={annuaire.erreur} /> : null}
          {!annuaire.chargement && !annuaire.erreur && !visibles.length ? (
            <Vide message="Aucun livreur ne correspond." />
          ) : null}

          {visibles.length ? (
            <ul className="grid gap-3 px-5 pb-5 pt-4 sm:grid-cols-2 xl:grid-cols-3 2xl:grid-cols-4">
              {visibles.map((livreur) => {
                const operationnel = lireOperationnel(livreur.operationalStatus);
                const vu = parIdentifiant.get(livreur.id);

                return (
                  <li key={livreur.id}>
                    {/* UN LIEN, PLUS UN BOUTON. La vignette menait a un panneau
                        voisin : un bouton suffisait. Elle mene maintenant a une
                        page, et seul un lien s'ouvre dans un onglet, se copie et
                        s'annonce comme une navigation. */}
                    <Link
                      href={`/livreurs/${livreur.id}`}
                      className="relief-doux pressable block w-full rounded-xl px-4 py-3.5 text-left transition"
                    >
                      <span className="flex items-center gap-3">
                        <Initiales nom={livreur.displayName ?? '?'} />
                        <span className="min-w-0 flex-1">
                          <span className="block truncate font-medium text-encre">
                            {livreur.displayName || 'Sans nom'}
                          </span>
                          <span className="block text-sm text-encre-3">
                            {libelleVehicule(livreur.vehicle?.type)}
                          </span>
                        </span>
                        <Badge couleur={COULEUR_TON_LIVREUR[operationnel.ton]}>
                          {operationnel.libelle}
                        </Badge>
                      </span>

                      <span className="mt-3 flex items-baseline justify-between text-sm">
                        <span className="text-encre-3">
                          {vu ? depuis(vu.derniereActivite) : 'Aucune course vue'}
                        </span>
                        <span className="chiffres-tabulaires text-encre-2">
                          {nombre(vu?.courses.length ?? 0)} course
                          {(vu?.courses.length ?? 0) > 1 ? 's' : ''}
                        </span>
                      </span>
                    </Link>
                  </li>
                );
              })}
            </ul>
          ) : null}
        </Carte>

      <Carte>
        <TitreDeCarte titre="Ce qui manque encore" sous="Une notion absente du domaine" />
        <div className="px-5 pb-5 pt-4">
          {/* NOTE ET ZONE NE SONT PAS DES ROUTES MANQUANTES : elles n'existent
              nulle part dans le domaine. Les afficher demanderait d'abord de
              decider ce qu'on mesure, et le referentiel ne le dit pas. */}
          <div className="creux-doux rounded-xl px-5 py-4">
            <p className="text-sm font-medium text-encre-2">Note du livreur et zone principale</p>
            <p className="mt-1 text-sm text-encre-3">
              Ni note, ni évaluation, ni découpage en secteurs n&apos;existent dans les contrats :
              le service Driver connaît des positions, pas des zones. Les afficher demanderait
              d&apos;abord de décider ce que l&apos;on mesure et qui le saisit — c&apos;est une
              décision produit, pas un manque technique.
            </p>
          </div>
        </div>
      </Carte>
    </>
  );
}

/**
 * Somme les tranches dont le statut satisfait le predicat.
 *
 * Renvoie null quand le service n'a rien renvoye : l'absence de reponse et
 * un compte de zero ne s'affichent pas pareil.
 */

/**
 * Somme les tranches dont le statut satisfait le predicat.
 *
 * Renvoie null quand le service n'a rien renvoye : l'absence de reponse et
 * un compte de zero ne s'affichent pas pareil.
 */
function sommeSi(
  tranches: { status?: number | string; count?: Nombre }[] | undefined,
  garde: (statut: number | string | undefined) => boolean,
): number | null {
  if (!tranches) return null;

  return tranches.reduce(
    (total, tranche) => (garde(tranche.status) ? total + val(tranche.count) : total),
    0,
  );
}
