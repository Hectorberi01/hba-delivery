'use client';

import { useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  Badge,
  Carte,
  CarteStat,
  Chargement,
  Erreur,
  TitreDeCarte,
  Vide,
} from '@/composants/base';
import { livreesAujourdhui, montant, ouvertes, tauxDeSucces } from '@/lib/agregats';
import { depuis, nombre, xof } from '@/lib/format';
import { COULEUR_TON, lireStatut, STATUTS, type CleStatut } from '@/lib/statuts';
import { useRessource } from '@/lib/client/ressource';
import type { PageDeCourses } from '@/lib/types';

const FILTRES: { cle: CleStatut | 'toutes'; libelle: string }[] = [
  { cle: 'toutes', libelle: 'Tous les statuts' },
  { cle: 'attente_paiement', libelle: 'Attente de paiement' },
  { cle: 'payee', libelle: 'Payée' },
  { cle: 'recherche_livreur', libelle: 'Recherche livreur' },
  { cle: 'livreur_affecte', libelle: 'Livreur affecté' },
  { cle: 'collectee', libelle: 'Colis collecté' },
  { cle: 'livree', libelle: 'Livrée' },
  { cle: 'sans_livreur', libelle: 'Aucun livreur trouvé' },
  { cle: 'annulee', libelle: 'Annulée' },
  { cle: 'echouee', libelle: 'Échouée' },
];

export default function PageCommandes() {
  const routeur = useRouter();

  const { donnees, chargement, erreur } = useRessource<PageDeCourses>(
    'admin/v1/deliveries?pageSize=100',
  );

  const [recherche, setRecherche] = useState('');
  const [filtre, setFiltre] = useState<CleStatut | 'toutes'>('toutes');

  const courses = useMemo(() => donnees?.deliveries ?? [], [donnees]);

  const visibles = useMemo(() => {
    const terme = recherche.trim().toLowerCase();

    return courses.filter((course) => {
      if (filtre !== 'toutes' && lireStatut(course.status) !== filtre) return false;
      if (!terme) return true;

      return [
        course.reference,
        course.id,
        course.recipientName,
        course.recipientPhone,
        course.dropoff?.landmark,
        course.driver?.displayName,
      ]
        .filter(Boolean)
        .some((valeur) => String(valeur).toLowerCase().includes(terme));
    });
  }, [courses, filtre, recherche]);

  const taux = tauxDeSucces(courses);

  return (
    <>
      {/* LES QUATRE CHIFFRES PORTENT SUR LA PAGE CHARGEE, pas sur la base :
          ListDeliveries rend des courses, aucun agregat. Chaque tuile le dit
          sous sa valeur — un nombre sans perimetre finit par etre lu comme un
          total d'entreprise, et celui-la bougerait avec la taille de page. */}
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <CarteStat
          titre="Courses"
          valeur={nombre(courses.length)}
          detail="Sur la page chargée"
        />
        <CarteStat
          titre="En cours"
          valeur={nombre(ouvertes(courses).length)}
          detail="Non encore clôturées"
        />
        <CarteStat
          titre="Livrées aujourd'hui"
          valeur={nombre(livreesAujourdhui(courses).length)}
          detail="Remise confirmée depuis minuit"
        />
        <CarteStat
          titre="Taux de succès"
          valeur={taux === null ? '—' : `${Math.round(taux * 100)} %`}
          detail="Livrées sur courses closes"
        />
      </div>

      <Carte className="px-4 py-3">
        <div className="flex flex-wrap items-center gap-3">
          <input
            type="search"
            value={recherche}
            onChange={(e) => setRecherche(e.target.value)}
            placeholder="Rechercher une référence, un destinataire, un livreur…"
            className="creux min-w-[260px] flex-1 rounded-xl border-0 px-4 py-3 text-sm text-encre outline-none placeholder:text-encre-3"
          />
          <select
            value={filtre}
            onChange={(e) => setFiltre(e.target.value as CleStatut | 'toutes')}
            className="creux rounded-xl border-0 px-3.5 py-3 text-sm text-encre-2 outline-none"
          >
            {FILTRES.map((option) => (
              <option key={option.cle} value={option.cle}>
                {option.libelle}
              </option>
            ))}
          </select>
          <span className="text-sm text-encre-3">
            {visibles.length} sur {courses.length}
          </span>
          {/* Le filtrage et la recherche portent sur la page deja chargee. Une
              recherche cote service demanderait un parametre que
              ListDeliveries n'a pas. */}
        </div>
      </Carte>

      {/* LA LISTE PREND TOUTE LA LARGEUR DEPUIS LE 2 OCTOBRE 2026. Elle
          partageait l'ecran avec un panneau de detail, qui lui prenait 400px et
          affichait la course choisie dans la page deja chargee. Le detail a sa
          propre page ; sept colonnes respirent enfin. */}
      <Carte className="overflow-hidden">
          <TitreDeCarte titre="Courses" sous="Les plus récentes d'abord" />

          {chargement ? <Chargement quoi="des courses" /> : null}
          {erreur ? <Erreur message={erreur} /> : null}
          {!chargement && !erreur && !visibles.length ? (
            <Vide message="Aucune course ne correspond." />
          ) : null}

          {visibles.length ? (
            <div className="mt-4 overflow-x-auto">
              <table className="w-full border-collapse text-sm">
                <thead>
                  <tr className="border-y border-bordure bg-plan text-left text-xs uppercase tracking-wide text-encre-3">
                    <th className="px-5 py-2.5 font-medium">Référence</th>
                    <th className="px-3 py-2.5 font-medium">Destinataire</th>
                    <th className="px-3 py-2.5 font-medium">Déposé à</th>
                    <th className="px-3 py-2.5 font-medium">Livreur</th>
                    <th className="px-3 py-2.5 font-medium">Statut</th>
                    <th className="px-3 py-2.5 text-right font-medium">Montant</th>
                    <th className="px-5 py-2.5 text-right font-medium">Créée</th>
                  </tr>
                </thead>
                <tbody>
                  {visibles.map((course) => {
                    const cle = lireStatut(course.status);
                    const definition = STATUTS[cle];
                    const vers = `/commandes/${course.id}`;

                    return (
                      // LA LIGNE EST CLIQUABLE, ET LA REFERENCE EST UN VRAI LIEN.
                      // Les deux, parce qu'aucun des deux ne suffit : un « onClick »
                      // sur la ligne ne s'atteint pas au clavier et ne s'ouvre pas
                      // dans un onglet, un lien seul obligerait a viser six
                      // caracteres. La ligne sert la souris, le lien sert le reste.
                      <tr
                        key={course.id}
                        onClick={() => routeur.push(vers)}
                        className="cursor-pointer border-b border-bordure transition hover:bg-plan"
                      >
                        <td className="px-5 py-3 font-medium text-encre">
                          <Link
                            href={vers}
                            onClick={(e) => e.stopPropagation()}
                            className="hover:text-marque hover:underline"
                          >
                            {course.reference ?? course.id.slice(0, 8)}
                          </Link>
                        </td>
                        <td className="px-3 py-3 text-encre-2">{course.recipientName || '—'}</td>
                        <td className="max-w-[220px] truncate px-3 py-3 text-encre-2">
                          {course.dropoff?.landmark || '—'}
                        </td>
                        <td className="px-3 py-3 text-encre-2">
                          {course.driver?.displayName || (
                            <span className="text-encre-3">Non assigné</span>
                          )}
                        </td>
                        <td className="px-3 py-3">
                          <Badge couleur={COULEUR_TON[definition.ton]}>{definition.libelle}</Badge>
                        </td>
                        <td className="chiffres-tabulaires px-3 py-3 text-right font-medium text-encre">
                          {xof(montant(course))}
                        </td>
                        <td className="px-5 py-3 text-right text-encre-3">
                          {depuis(course.createdAt)}
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          ) : null}
        </Carte>

    </>
  );
}
