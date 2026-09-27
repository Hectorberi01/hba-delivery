'use client';

/**
 * Chargement de l'API Google Maps, une seule fois par page.
 *
 * PAS DE PAQUET NPM, ET PAS DE @types/google.maps NON PLUS. Ajouter une
 * dependance obligerait a reinstaller node_modules pour trois appels ; les
 * types dont cet ecran a besoin tiennent ci-dessous. Ils sont volontairement
 * etroits : ils decrivent ce qu'on utilise, pas ce que Google expose. Le jour
 * ou la carte demandera plus, c'est ici qu'on ajoutera — ou la qu'on
 * installera les vrais types, en connaissance de cause.
 */

export type Coord = { lat: number; lng: number };

export type GMap = {
  fitBounds: (bounds: GBounds, padding?: number) => void;
  setCenter: (c: Coord) => void;
  setZoom: (z: number) => void;
};

export type GBounds = { extend: (c: Coord) => void; isEmpty: () => boolean };

export type GMarker = {
  setPosition: (c: Coord) => void;
  setMap: (m: GMap | null) => void;
  setTitle: (t: string) => void;
  setIcon: (i: unknown) => void;
  addListener: (evenement: string, rappel: () => void) => void;
  getPosition: () => Coord | undefined;
};

export type GInfoWindow = {
  setContent: (html: string) => void;
  open: (options: { map: GMap; anchor: GMarker }) => void;
  close: () => void;
};

type GoogleMaps = {
  Map: new (hote: HTMLElement, options: Record<string, unknown>) => GMap;
  Marker: new (options: Record<string, unknown>) => GMarker;
  InfoWindow: new (options?: Record<string, unknown>) => GInfoWindow;
  LatLngBounds: new () => GBounds;
  SymbolPath: { CIRCLE: number };
};

declare global {
  interface Window {
    google?: { maps?: GoogleMaps };
  }
}

let promesse: Promise<GoogleMaps> | null = null;

export function cleGoogleMaps(): string {
  return process.env.NEXT_PUBLIC_GOOGLE_MAPS_API_KEY ?? '';
}

export function chargerGoogleMaps(): Promise<GoogleMaps> {
  if (typeof window === 'undefined') {
    return Promise.reject(new Error('Google Maps ne se charge que dans le navigateur.'));
  }

  if (window.google?.maps) return Promise.resolve(window.google.maps);
  if (promesse) return promesse;

  const cle = cleGoogleMaps();
  if (!cle) {
    return Promise.reject(
      new Error('NEXT_PUBLIC_GOOGLE_MAPS_API_KEY est absente : la carte ne peut pas se charger.'),
    );
  }

  promesse = new Promise<GoogleMaps>((resoudre, rejeter) => {
    const balise = document.createElement('script');
    balise.src = `https://maps.googleapis.com/maps/api/js?key=${encodeURIComponent(cle)}&v=weekly&language=fr&region=BJ`;
    balise.async = true;

    balise.onload = () => {
      const maps = window.google?.maps;
      if (maps) resoudre(maps);
      else rejeter(new Error('Google Maps s\'est chargé sans exposer son API.'));
    };

    // ON REMET LA PROMESSE A NULL EN CAS D'ECHEC. Sans cela, une coupure
    // reseau au premier chargement condamnerait la carte pour toute la duree
    // de la session : chaque tentative suivante recevrait la promesse
    // rejetee, sans jamais réessayer.
    balise.onerror = () => {
      promesse = null;
      rejeter(new Error('Le script Google Maps n\'a pas pu être chargé.'));
    };

    document.head.appendChild(balise);
  });

  return promesse;
}
