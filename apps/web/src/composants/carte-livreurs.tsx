'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import {
  chargerGoogleMaps,
  cleGoogleMaps,
  type GInfoWindow,
  type GMap,
  type GMarker,
} from '@/lib/client/google-maps';
import { libelleVehicule } from '@/lib/agregats';
import { depuis } from '@/lib/format';
import { COULEUR_TON_LIVREUR, lireOperationnel } from '@/lib/statuts';
import type { PagePositions, PositionLivreur } from '@/lib/types';

/** Cotonou. Le centre ne sert qu'avant le premier point. */
const COTONOU = { lat: 6.3703, lng: 2.3912 };

/**
 * ON RAFRAICHIT PLUS SOUVENT QUE LE BATTEMENT DU TELEPHONE. L'application
 * livreur envoie un point toutes les vingt secondes ; interroger toutes les
 * quinze evite qu'un point paraisse vieux de trente-cinq secondes alors qu'il
 * en a vingt. Descendre plus bas ne montrerait rien de neuf et multiplierait
 * les appels.
 *
 * CETTE CONSOLE RESTE AU SONDAGE ALORS QUE LES DEUX APPLICATIONS MOBILES SONT
 * PASSEES AU TEMPS REEL, ET CE N'EST PAS UN OUBLI DE LA PHASE 8.
 *
 * SignalR depuis un navigateur exige le jeton EN JAVASCRIPT — un WebSocket ne
 * porte pas d'en-tete Authorization, d'ou la convention « access_token » dans la
 * chaine de requete. Or ici le jeton ne quitte JAMAIS le serveur : il vit dans un
 * cookie httpOnly et la procuration Next.js le pose elle-meme, precisement pour
 * qu'un script injecte dans la page ne puisse pas le recopier (voir
 * lib/serveur/session.ts). Le donner au navigateur mettrait un jeton
 * d'administration valable quinze minutes a portee de n'importe quelle injection.
 *
 * ET LE GAIN SERAIT MINCE : la carte ne peut pas etre plus fraiche que les points
 * qu'elle affiche, et ceux-la arrivent toutes les vingt secondes. On echangerait
 * donc la seule protection du jeton d'administration contre cinq secondes.
 *
 * LA BONNE REPONSE, LE JOUR OU L'INSTANTANE COMPTERA : un jeton court emis par
 * Identity, d'audience restreinte au hub — soixante secondes, utilisable seulement
 * sur /hubs/ops. C'est une fonctionnalite d'Identity, pas une ligne a ajouter ici.
 */
const RAFRAICHISSEMENT_MS = 15_000;

/**
 * CE QUE LE TELEPHONE DIT DU MOUVEMENT : vitesse, cap, precision.
 *
 * TROIS CHAMPS QUE SEULE LA CONSOLE VOIT. Le point 37 les refuse au client d'une
 * mission ; ici ils repondent a la question que l'exploitation se pose vraiment —
 * « pourquoi cette course traine ». Un livreur a l'arret depuis dix minutes et un
 * livreur qui roule a 30 km/h n'appellent pas le meme coup de telephone, et la
 * carte seule ne distingue pas les deux.
 *
 * RIEN N'EST AFFICHE QUAND RIEN N'EST SU, et surtout pas un zero. Un cap absent et
 * un cap de zero degre sont deux choses differentes — la seconde veut dire « plein
 * nord » — et ecrire « 0° » sur un telephone muet ferait croire a une mesure.
 *
 * LA VITESSE EST CONVERTIE EN KM/H, parce que c'est l'unite dans laquelle on lit
 * une route. Le contrat la porte en metres par seconde, qui est l'unite du capteur.
 *
 * LA PRECISION N'APPARAIT QU'AU-DELA DE CINQUANTE METRES. En dessous, elle ne dit
 * rien d'utile et encombrerait chaque bulle ; au-dela, elle explique pourquoi une
 * epingle a saute d'un pate de maisons sans que personne n'ait bouge.
 */
function mouvement(position: PositionLivreur): string {
  const morceaux: string[] = [];

  if (typeof position.speedMps === 'number') {
    const kmh = Math.round(position.speedMps * 3.6);
    morceaux.push(kmh === 0 ? 'à l’arrêt' : `${kmh} km/h`);
  }

  if (typeof position.headingDegrees === 'number') {
    morceaux.push(cardinal(position.headingDegrees));
  }

  if (typeof position.accuracyMeters === 'number' && position.accuracyMeters > 50) {
    morceaux.push(`± ${Math.round(position.accuracyMeters)} m`);
  }

  if (morceaux.length === 0) return '';

  return `<br><span style="color:#565e6b">${echapper(morceaux.join(' · '))}</span>`;
}

/**
 * Le cap en point cardinal plutot qu'en degres.
 *
 * « VERS LE NORD-EST » SE LIT, « 47° » SE CALCULE. Personne, devant une carte, ne
 * traduit un angle de tete — et la precision d'un degre n'apporte rien quand on
 * cherche a savoir si le livreur s'eloigne ou se rapproche.
 */
function cardinal(degres: number): string {
  const points = ['nord', 'nord-est', 'est', 'sud-est', 'sud', 'sud-ouest', 'ouest', 'nord-ouest'];
  const index = Math.round(((degres % 360) + 360) % 360 / 45) % 8;

  return `vers le ${points[index]}`;
}

export function CarteDesLivreurs() {
  const hote = useRef<HTMLDivElement | null>(null);
  const carte = useRef<GMap | null>(null);
  const bulle = useRef<GInfoWindow | null>(null);
  const marqueurs = useRef<Map<string, GMarker>>(new Map());
  const cadre = useRef(false);

  const [page, setPage] = useState<PagePositions | null>(null);
  const [erreur, setErreur] = useState<string | null>(null);
  const [prete, setPrete] = useState(false);

  const cle = useMemo(cleGoogleMaps, []);

  // ------------------------------------------------------- Les donnees --

  useEffect(() => {
    let vivant = true;

    async function lire() {
      try {
        const reponse = await fetch('/api/hba/admin/v1/drivers/positions', {
          headers: { accept: 'application/json' },
        });

        if (reponse.status === 401) {
          window.location.href = '/connexion';
          return;
        }

        if (!reponse.ok) throw new Error(await reponse.text());

        const valeur = (await reponse.json()) as PagePositions;
        if (vivant) {
          setPage(valeur);
          setErreur(null);
        }
      } catch (cause: unknown) {
        // UN RAFRAICHISSEMENT RATE NE VIDE PAS LA CARTE. Les points affiches
        // restent, avec leur horodatage : mieux vaut une carte qui vieillit
        // visiblement qu'un ecran vide sur une coupure de trois secondes.
        if (vivant) setErreur(cause instanceof Error ? cause.message : 'Lecture impossible.');
      }
    }

    void lire();
    const minuteur = window.setInterval(() => void lire(), RAFRAICHISSEMENT_MS);

    return () => {
      vivant = false;
      window.clearInterval(minuteur);
    };
  }, []);

  // --------------------------------------------------------- La carte --

  useEffect(() => {
    let vivant = true;

    chargerGoogleMaps()
      .then((maps) => {
        if (!vivant || !hote.current || carte.current) return;

        carte.current = new maps.Map(hote.current, {
          center: COTONOU,
          zoom: 12,
          disableDefaultUI: true,
          zoomControl: true,
          clickableIcons: false,
        });

        bulle.current = new maps.InfoWindow();
        setPrete(true);
      })
      .catch((cause: unknown) => {
        if (vivant) setErreur(cause instanceof Error ? cause.message : 'Carte indisponible.');
      });

    return () => {
      vivant = false;
    };
  }, []);

  // ------------------------------------------------- Points sur la carte --

  useEffect(() => {
    if (!prete || !carte.current || !page?.positions) return;

    const maps = window.google?.maps;
    if (!maps) return;

    const vus = new Set<string>();
    const bornes = new maps.LatLngBounds();

    for (const position of page.positions) {
      const coord = { lat: position.latitude, lng: position.longitude };
      const etat = lireOperationnel(position.operationalStatus);
      const couleur = COULEUR_TON_LIVREUR[etat.ton];

      vus.add(position.driverId);
      bornes.extend(coord);

      // UN MARQUEUR PAR LIVREUR, DEPLACE PLUTOT QUE RECREE. Tout effacer a
      // chaque rafraichissement ferait clignoter la carte toutes les quinze
      // secondes et perdrait la bulle ouverte sous le curseur d'ops.
      const existant = marqueurs.current.get(position.driverId);
      const icone = {
        path: maps.SymbolPath.CIRCLE,
        scale: 8,
        fillColor: couleur,
        fillOpacity: 1,
        strokeColor: '#ffffff',
        strokeWeight: 2,
      };

      if (existant) {
        existant.setPosition(coord);
        existant.setIcon(icone);
        existant.setTitle(position.displayName ?? position.driverId);
        continue;
      }

      const marqueur = new maps.Marker({
        map: carte.current,
        position: coord,
        icon: icone,
        title: position.displayName ?? position.driverId,
      });

      marqueur.addListener('click', () => {
        const courant = page.positions?.find((p) => p.driverId === position.driverId) ?? position;
        const libelle = lireOperationnel(courant.operationalStatus).libelle;

        bulle.current?.setContent(
          `<div style="font:14px system-ui;color:#1f2733">
             <strong>${echapper(courant.displayName ?? 'Livreur')}</strong><br>
             ${echapper(libelle)} · ${echapper(libelleVehicule(courant.vehicleType))}<br>
             <span style="color:#565e6b">Vu ${echapper(courant.seenAt ? depuis(courant.seenAt) : "à l'instant")}</span>
             ${mouvement(courant)}
           </div>`,
        );
        if (carte.current) bulle.current?.open({ map: carte.current, anchor: marqueur });
      });

      marqueurs.current.set(position.driverId, marqueur);
    }

    // UN LIVREUR QUI DISPARAIT DE LA LISTE DISPARAIT DE LA CARTE. Sans cela,
    // un point mort resterait affiche jusqu'au rechargement de la page —
    // exactement le mensonge que la fenetre de fraicheur sert a eviter.
    for (const [id, marqueur] of marqueurs.current) {
      if (!vus.has(id)) {
        marqueur.setMap(null);
        marqueurs.current.delete(id);
      }
    }

    // ON NE CADRE QU'UNE FOIS. Recadrer a chaque rafraichissement arracherait
    // la vue a ops des qu'un livreur bouge.
    if (!cadre.current && !bornes.isEmpty() && page.positions.length > 0) {
      cadre.current = true;
      if (page.positions.length === 1) {
        carte.current.setCenter({
          lat: page.positions[0].latitude,
          lng: page.positions[0].longitude,
        });
        carte.current.setZoom(14);
      } else {
        carte.current.fitBounds(bornes, 48);
      }
    }
  }, [page, prete]);

  // ------------------------------------------------------------ Rendu --

  if (!cle) {
    return (
      <Encadre titre="Carte indisponible">
        La clé <code className="creux-doux rounded px-1.5 py-0.5 text-[13px]">
          NEXT_PUBLIC_GOOGLE_MAPS_API_KEY
        </code>{' '}
        n&apos;est pas renseignée dans <code className="creux-doux rounded px-1.5 py-0.5 text-[13px]">
          apps/web/.env.local
        </code>. La route <code className="creux-doux rounded px-1.5 py-0.5 text-[13px]">
          GET /api/admin/v1/drivers/positions
        </code>{' '}
        fonctionne, elle : seule la carte manque.
      </Encadre>
    );
  }

  const positions = page?.positions ?? [];
  const fenetre = page?.freshnessSeconds ?? 0;

  return (
    // UNE COLONNE QUI PEUT S'ETIRER : compteur en haut, legende en bas, et la
    // carte prend ce qui reste. Sans « h-full », le composant garde la hauteur
    // minimale de son conteneur de carte et le parent ne peut rien lui donner.
    <div className="flex h-full flex-col">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <p className="text-sm text-encre-2">
          <strong className="font-semibold text-encre">{positions.length}</strong>{' '}
          {positions.length > 1 ? 'livreurs localisés' : 'livreur localisé'}
        </p>
        {/* LE COMPTE SANS LA FENETRE SE LIT COMME UN EFFECTIF. Ce n'en est
            pas un : c'est le nombre de telephones qui ont parle recemment. */}
        {fenetre > 0 ? (
          <p className="text-xs text-encre-3">
            Positions reçues dans les {fenetre} dernières secondes · actualisé toutes les{' '}
            {RAFRAICHISSEMENT_MS / 1000} s
          </p>
        ) : null}
      </div>

      <div className="creux relative mt-3 min-h-0 flex-1 overflow-hidden rounded-xl">
        {/* LA HAUTEUR VIENT DU PARENT QUAND IL EN DONNE UNE, et retombe sur
            420px sinon. « h-full » seul s'effondre a zero dans un conteneur
            sans hauteur : le minimum est ce qui rend ce composant posable
            n'importe ou. */}
        <div
          ref={hote}
          className="h-full min-h-[420px] w-full"
          role="application"
          aria-label="Carte des livreurs"
        />

        {positions.length === 0 && !erreur ? (
          <p className="pointer-events-none absolute inset-x-6 top-1/2 -translate-y-1/2 text-center text-sm text-encre-2">
            Aucun livreur n&apos;a envoyé de position
            {fenetre > 0 ? ` dans les ${fenetre} dernières secondes` : ' récemment'}.
          </p>
        ) : null}
      </div>

      {/* LEGENDE : la couleur seule ne dit rien a qui ne la distingue pas. */}
      <ul className="mt-3 flex flex-wrap gap-x-5 gap-y-1.5">
        {[
          { ton: 'bon' as const, libelle: 'Disponible' },
          { ton: 'encours' as const, libelle: 'En mission' },
          { ton: 'attention' as const, libelle: 'Réservé' },
          { ton: 'neutre' as const, libelle: 'Hors ligne' },
        ].map((entree) => (
          <li key={entree.libelle} className="inline-flex items-center gap-2 text-xs text-encre-2">
            <span
              aria-hidden
              className="h-2.5 w-2.5 rounded-full"
              style={{ backgroundColor: COULEUR_TON_LIVREUR[entree.ton] }}
            />
            {entree.libelle}
          </li>
        ))}
      </ul>

      {erreur ? (
        <p role="alert" className="mt-3 text-sm text-critique">
          Dernier rafraîchissement en échec : {erreur}
        </p>
      ) : null}
    </div>
  );
}

function Encadre({ titre, children }: { titre: string; children: React.ReactNode }) {
  return (
    <div className="creux-doux rounded-xl px-5 py-6">
      <p className="text-sm font-medium text-encre-2">{titre}</p>
      <p className="mt-1 text-sm text-encre-3">{children}</p>
    </div>
  );
}

/**
 * La bulle de Google Maps prend du HTML, pas du JSX.
 *
 * LE NOM D'UN LIVREUR VIENT DE SON INSCRIPTION, donc d'une saisie libre. Le
 * poser tel quel dans du HTML ouvrirait une injection dans la console
 * d'exploitation, depuis un formulaire d'inscription public.
 */
function echapper(texte: string): string {
  return texte
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#39;');
}
