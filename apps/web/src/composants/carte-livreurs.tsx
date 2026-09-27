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
 */
const RAFRAICHISSEMENT_MS = 15_000;

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
    <div>
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

      <div className="creux relative mt-3 overflow-hidden rounded-xl">
        <div ref={hote} className="h-[420px] w-full" role="application" aria-label="Carte des livreurs" />

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
