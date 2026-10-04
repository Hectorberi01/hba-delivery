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

  /** AJOUTE POUR LE DESSIN DES ZONES : un clic pose un sommet. */
  addListener: (evenement: string, rappel: (e: GMapMouseEvent) => void) => GListener;
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

/** Ce qu'un clic sur la carte rend. La position est absente sur un clic hors sol. */
export type GMapMouseEvent = { latLng?: { lat: () => number; lng: () => number } };

/** De quoi retirer un ecouteur quand l'ecran se demonte. */
export type GListener = { remove: () => void };

/**
 * Un polygone, pose et repris.
 *
 * NI EDITABLE NI DEPLACABLE DANS CE TYPE, ET C'EST UN CHOIX. Un polygone
 * modifiable par poignees expose son chemin par un MVCArray dont les evenements
 * demanderaient trois types de plus — pour un geste qui se remplace par « reprendre
 * le trace ». Le jour ou le dessin a la poignee sera demande, c'est ici qu'il
 * faudra l'ajouter, en connaissance de cause.
 */
export type GPolygon = {
  setMap: (m: GMap | null) => void;
  setPaths: (chemin: Coord[]) => void;
  setOptions: (options: Record<string, unknown>) => void;
  addListener: (evenement: string, rappel: () => void) => GListener;
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

  // AJOUTE POUR LES ZONES. Pas la bibliotheque « drawing » de Google : son
  // DrawingManager apporterait une dependance de script et une dizaine de types
  // pour un geste que trois clics font aussi bien. On pose les sommets a la main.
  Polygon: new (options: Record<string, unknown>) => GPolygon;
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
