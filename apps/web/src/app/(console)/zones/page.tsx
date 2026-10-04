'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import {
  Badge,
  Bouton,
  Carte,
  Champ,
  Chargement,
  Erreur,
  TitreDeCarte,
  Vide,
} from '@/composants/base';
import {
  chargerGoogleMaps,
  cleGoogleMaps,
  type Coord,
  type GListener,
  type GMap,
  type GMarker,
  type GPolygon,
} from '@/lib/client/google-maps';
import { envoyer, useRessource } from '@/lib/client/ressource';
import type { ZoneTarifaire } from '@/lib/types';

/** Cotonou. Le centre ne sert qu'avant la première zone. */
const COTONOU: Coord = { lat: 6.3703, lng: 2.3912 };

/**
 * Les couleurs des zones, dans l'ordre d'affichage.
 *
 * CE SONT CELLES DE LA CONSOLE, et pas une palette de plus : les quatre séries de
 * globals.css, reprises en hexadécimal parce que Google Maps ne lit pas une
 * variable CSS. Au-delà de quatre zones, le cycle recommence — deux zones de même
 * couleur sont alors voisines dans la liste, jamais sur la carte, puisqu'elles ne
 * se superposent pas.
 */
const COULEURS = ['#2a78d6', '#eb6834', '#1baf7a', '#eda100'];

/** Trois sommets au minimum : en dessous, un contour n'enferme aucune surface. */
const SOMMETS_MINIMUM = 3;

/**
 * Les zones tarifaires, sur une carte.
 *
 * CE QUE CET ÉCRAN RÉPARE. Jusqu'au 1er octobre 2026, `ConfiguredZoneLocator`
 * rendait la zone de repli configurée pour TOUT point du globe : le zonage
 * tarifaire était décoratif, une seule grille s'appliquait partout, et le filtre
 * par zone de l'écran des grilles filtrait un champ que personne ne pouvait faire
 * varier. Les zones sont désormais de vrais polygones (point 40).
 *
 * ON DESSINE EN CLIQUANT, PAS EN TIRANT DES POIGNÉES. La bibliothèque « drawing »
 * de Google apporterait un script et une dizaine de types pour un geste que trois
 * clics font aussi bien ; reprendre un tracé remplace la poignée. C'est assumé, et
 * c'est le premier endroit à améliorer si le découpage devient fin.
 *
 * REDESSINER DÉPLACE DES PRIX, SANS TOUCHER À UNE SEULE GRILLE : un point qui
 * passe de « périphérie » à « centre » change de barème au prochain devis. C'est
 * pourquoi l'écriture est réservée à `admin` — la lecture, elle, est ouverte au
 * back-office, parce que la carte explique un prix et un dispatch.
 */
export default function ZonesPage() {
  const [inactives, setInactives] = useState(false);

  const zones = useRessource<ZoneTarifaire[]>(
    `admin/v1/zones${inactives ? '?inactives=true' : ''}`,
  );

  /** Le tracé en cours. Vide : on ne dessine pas. */
  const [trace, setTrace] = useState<Coord[]>([]);
  const [dessine, setDessine] = useState(false);

  /** La zone qu'on redessine, s'il y en a une. */
  const [reprise, setReprise] = useState<ZoneTarifaire | null>(null);

  const liste = useMemo(() => zones.donnees ?? [], [zones.donnees]);

  return (
    // DEUX COLONNES, ET LA CARTE PREND LA HAUTEUR.
    //
    // ELLES ETAIENT EMPILEES : carte, puis formulaire, puis decoupage. Dessiner
    // une zone demandait donc de descendre pour la nommer, puis de remonter pour
    // verifier le trace, puis de redescendre pour la retrouver dans la liste.
    // Cote a cote, le trace et sa liste se regardent ensemble.
    //
    // LE FORMULAIRE RESTE SOUS LA CARTE, PAS DANS LA COLONNE DE DROITE : on le
    // remplit juste apres avoir pose le dernier sommet, et le regard n'a pas a
    // traverser l'ecran entre le geste et sa confirmation.
    //
    // « min-h-0 » SUR LA PAGE ET SUR CHAQUE COLONNE : sans lui, un enfant en
    // defilement impose sa hauteur naturelle et la grille deborde au lieu de se
    // partager.
    <div className="grid min-h-0 flex-1 gap-4 xl:grid-cols-[minmax(0,1fr)_380px]">
      <div className="flex min-h-0 flex-col gap-4">
      <Carte className="flex min-h-0 flex-1 flex-col">
        <TitreDeCarte
          titre="Zones tarifaires"
          sous="Un polygone par zone ; la priorité départage les chevauchements"
          action={
            <label className="flex items-center gap-2 text-sm text-encre-2">
              <input
                type="checkbox"
                checked={inactives}
                onChange={(evenement) => setInactives(evenement.target.checked)}
                className="h-4 w-4"
              />
              Montrer les désactivées
            </label>
          }
        />

        {zones.chargement ? <Chargement quoi="des zones" /> : null}
        {zones.erreur ? <Erreur message={zones.erreur} /> : null}

        <div className="min-h-0 flex-1">
          <CarteDesZones
            zones={liste}
            trace={trace}
            onPoint={(point) => {
              if (!dessine) return;
              setTrace((points) => [...points, point]);
            }}
          />
        </div>

        <div className="flex flex-wrap items-center gap-3 px-5 py-4">
          {dessine ? (
            <>
              <Bouton
                variante="discret"
                disabled={trace.length === 0}
                onClick={() => setTrace((points) => points.slice(0, -1))}
              >
                Annuler le dernier point
              </Bouton>
              <Bouton
                variante="discret"
                onClick={() => {
                  setDessine(false);
                  setTrace([]);
                  setReprise(null);
                }}
              >
                Abandonner le tracé
              </Bouton>
              <p className="text-sm text-encre-3">
                {trace.length < SOMMETS_MINIMUM
                  ? `Cliquez sur la carte : ${SOMMETS_MINIMUM - trace.length} sommet(s) de plus.`
                  : `${trace.length} sommets. Nommez la zone ci-dessous pour l'enregistrer.`}
              </p>
            </>
          ) : (
            <Bouton
              onClick={() => {
                setReprise(null);
                setTrace([]);
                setDessine(true);
              }}
            >
              Dessiner une zone
            </Bouton>
          )}
        </div>
      </Carte>

      {dessine && trace.length >= SOMMETS_MINIMUM ? (
        <FormulaireDeZone
          // LA CLÉ REMONTE LE FORMULAIRE quand on passe d'une reprise à une autre :
          // les valeurs initiales d'un useState ne sont lues qu'au montage.
          key={reprise?.code ?? 'neuve'}
          reprise={reprise}
          trace={trace}
          apres={() => {
            setDessine(false);
            setTrace([]);
            setReprise(null);
            zones.recharger();
          }}
        />
      ) : null}
      </div>

      {/* LA COLONNE DE DROITE DEFILE SEULE. Vingt zones ne doivent pas pousser la
          carte hors de l'ecran : c'est elle qu'on garde sous les yeux pendant
          qu'on parcourt la liste. */}
      <Carte className="flex min-h-0 flex-col overflow-y-auto">
        <TitreDeCarte titre="Le découpage" sous="Par priorité décroissante" />

        {liste.length === 0 && !zones.chargement ? (
          <Vide
            message={
              "Aucune zone dessinée. Tant qu'il n'y en a pas, le prix d'une course "
              + 'dépend du repli configuré dans Pricing — la même grille partout.'
            }
          />
        ) : null}

        {liste.length > 0 ? (
          <ul className="space-y-3 px-5 pb-5 pt-2">
            {liste.map((zone, index) => (
              <LigneDeZone
                key={zone.code ?? zone.id ?? index}
                zone={zone}
                couleur={COULEURS[index % COULEURS.length]}
                onRedessiner={() => {
                  setReprise(zone);
                  setTrace([]);
                  setDessine(true);
                }}
                apres={zones.recharger}
              />
            ))}
          </ul>
        ) : null}
      </Carte>
    </div>
  );
}

/**
 * La carte, et les polygones dessus.
 *
 * PAS DE CLÉ, PAS DE CARTE, ET ON LE DIT. Une zone vide à la place d'un plan
 * laisse chercher une panne de réseau pendant dix minutes.
 */
function CarteDesZones({
  zones,
  trace,
  onPoint,
}: {
  zones: ZoneTarifaire[];
  trace: Coord[];
  onPoint: (point: Coord) => void;
}) {
  const hote = useRef<HTMLDivElement | null>(null);
  const carte = useRef<GMap | null>(null);
  const polygones = useRef<GPolygon[]>([]);
  const sommets = useRef<GMarker[]>([]);
  const ecoute = useRef<GListener | null>(null);

  // LE RAPPEL VIT DANS UNE REF, et c'est ce qui évite de reposer l'écouteur à
  // chaque frappe. Un écouteur Google Maps capture la fonction qu'on lui donne :
  // sans cette indirection, il faudrait le retirer et le remettre à chaque
  // changement d'état, et un clic tombant entre les deux serait perdu.
  const rappel = useRef(onPoint);
  rappel.current = onPoint;

  const [echec, setEchec] = useState<string | null>(null);

  useEffect(() => {
    if (!cleGoogleMaps()) {
      setEchec('NEXT_PUBLIC_GOOGLE_MAPS_API_KEY est absente : la carte ne peut pas se charger.');
      return;
    }

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

        ecoute.current = carte.current.addListener('click', (evenement) => {
          const position = evenement.latLng;
          if (!position) return;

          rappel.current({ lat: position.lat(), lng: position.lng() });
        });
      })
      .catch((cause: unknown) => {
        if (vivant) setEchec(cause instanceof Error ? cause.message : 'Carte indisponible.');
      });

    return () => {
      vivant = false;
      ecoute.current?.remove();
      ecoute.current = null;
    };
  }, []);

  // LES POLYGONES SONT REFAITS A CHAQUE CHANGEMENT, pas mis a jour un par un. Une
  // dizaine de zones ne justifie pas de tenir un registre : le coût est nul et le
  // code qui réconcilie deux listes est le genre qui laisse des formes fantômes.
  useEffect(() => {
    const maps = window.google?.maps;
    const plan = carte.current;
    if (!maps || !plan) return;

    for (const forme of polygones.current) forme.setMap(null);
    polygones.current = [];

    zones.forEach((zone, index) => {
      const chemin = (zone.contour ?? [])
        .filter((p) => typeof p.latitude === 'number' && typeof p.longitude === 'number')
        .map((p) => ({ lat: p.latitude as number, lng: p.longitude as number }));

      if (chemin.length < SOMMETS_MINIMUM) return;

      const couleur = COULEURS[index % COULEURS.length];

      polygones.current.push(
        new maps.Polygon({
          paths: chemin,
          map: plan,
          strokeColor: couleur,
          strokeWeight: 2,

          // UNE ZONE DESACTIVEE SE VOIT SANS SE CONFONDRE : même trait, remplissage
          // presque transparent. L'effacer la rendrait introuvable alors que c'est
          // justement elle qu'on vient vérifier.
          strokeOpacity: zone.actif === false ? 0.45 : 1,
          fillColor: couleur,
          fillOpacity: zone.actif === false ? 0.05 : 0.18,

          // LA PRIORITE DECIDE DE L'EMPILEMENT, comme en base : la zone qui gagne un
          // chevauchement est dessinée au-dessus de celle qu'elle recouvre.
          zIndex: zone.priorite ?? 0,
        }),
      );
    });
  }, [zones]);

  // LE TRACE EN COURS, EN POINTILLES ET AVEC SES SOMMETS. Sans les points, un
  // clic mal placé ne se voit pas avant la fermeture du contour.
  useEffect(() => {
    const maps = window.google?.maps;
    const plan = carte.current;
    if (!maps || !plan) return;

    for (const repere of sommets.current) repere.setMap(null);
    sommets.current = [];

    for (const point of trace) {
      sommets.current.push(
        new maps.Marker({
          position: point,
          map: plan,
          icon: {
            path: maps.SymbolPath.CIRCLE,
            scale: 5,
            fillColor: '#2a78d6',
            fillOpacity: 1,
            strokeColor: '#ffffff',
            strokeWeight: 2,
          },
        }),
      );
    }
  }, [trace]);

  const enCours = useRef<GPolygon | null>(null);

  useEffect(() => {
    const maps = window.google?.maps;
    const plan = carte.current;
    if (!maps || !plan) return;

    enCours.current?.setMap(null);
    enCours.current = null;

    if (trace.length < 2) return;

    enCours.current = new maps.Polygon({
      paths: trace,
      map: plan,
      strokeColor: '#2a78d6',
      strokeWeight: 2,
      fillColor: '#2a78d6',
      fillOpacity: 0.1,
      zIndex: 1000,
    });
  }, [trace]);

  if (echec) {
    return (
      <div className="px-5 pb-5">
        <Erreur message={echec} />
      </div>
    );
  }

  // MEME REGLE QUE LA CARTE DES LIVREURS : la hauteur vient du parent quand il en
  // donne une, et retombe sur 420px sinon. « h-full » seul s'effondre a zero dans
  // un conteneur sans hauteur.
  return <div ref={hote} className="h-full min-h-[420px] w-full" />;
}

function LigneDeZone({
  zone,
  couleur,
  onRedessiner,
  apres,
}: {
  zone: ZoneTarifaire;
  couleur: string;
  onRedessiner: () => void;
  apres: () => void;
}) {
  const [occupe, setOccupe] = useState(false);
  const [echec, setEchec] = useState<string | null>(null);

  const dessinee = (zone.contour ?? []).length >= SOMMETS_MINIMUM;

  async function basculer() {
    setOccupe(true);
    setEchec(null);

    try {
      await envoyer(`admin/v1/zones/${zone.code}/active`, { actif: zone.actif === false });
      apres();
    } catch (cause: unknown) {
      setEchec(cause instanceof Error ? cause.message : 'Changement impossible.');
    } finally {
      setOccupe(false);
    }
  }

  return (
    <li className="creux-doux rounded-xl px-4 py-3">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <span
            aria-hidden
            className="h-4 w-4 rounded"
            style={{ backgroundColor: couleur, opacity: zone.actif === false ? 0.35 : 1 }}
          />
          <div>
            <p className="text-sm font-medium text-encre">{zone.nom ?? zone.code}</p>
            <p className="text-xs text-encre-3">
              {zone.code} · priorité {zone.priorite ?? 0} ·{' '}
              {dessinee ? `${(zone.contour ?? []).length} sommets` : 'pas encore dessinée'}
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          {zone.actif === false ? (
            <Badge couleur="var(--color-encre-3)">Désactivée</Badge>
          ) : null}
          <Bouton variante="discret" onClick={onRedessiner}>
            Redessiner
          </Bouton>
          <Bouton
            variante={zone.actif === false ? 'discret' : 'danger'}
            disabled={occupe}
            onClick={basculer}
          >
            {zone.actif === false ? 'Réactiver' : 'Désactiver'}
          </Bouton>
        </div>
      </div>

      {echec ? <Erreur message={echec} /> : null}
    </li>
  );
}

/**
 * Nommer et enregistrer le tracé.
 *
 * LE CODE NE SE MODIFIE PAS SUR UNE REPRISE, et il ne doit pas : c'est lui qui lie
 * la zone à ses grilles. Le changer créerait une seconde zone et laisserait les
 * grilles de la première sans territoire.
 */
function FormulaireDeZone({
  reprise,
  trace,
  apres,
}: {
  reprise: ZoneTarifaire | null;
  trace: Coord[];
  apres: () => void;
}) {
  const [code, setCode] = useState(reprise?.code ?? '');
  const [nom, setNom] = useState(reprise?.nom ?? '');
  const [priorite, setPriorite] = useState(`${reprise?.priorite ?? 0}`);

  const [occupe, setOccupe] = useState(false);
  const [echec, setEchec] = useState<string | null>(null);

  const complet = code.trim() !== '' && nom.trim() !== '';

  async function enregistrer() {
    setOccupe(true);
    setEchec(null);

    try {
      await envoyer('admin/v1/zones', {
        code: code.trim().toLowerCase(),
        nom: nom.trim(),
        priorite: Number.parseInt(priorite, 10) || 0,

        // EN [latitude, longitude], COMME TOUT LE DÉPÔT. L'ordre inverse — celui du
        // GeoJSON — placerait Cotonou en Somalie.
        contour: trace.map((point) => ({ latitude: point.lat, longitude: point.lng })),
      });

      apres();
    } catch (cause: unknown) {
      setEchec(cause instanceof Error ? cause.message : 'Enregistrement impossible.');
    } finally {
      setOccupe(false);
    }
  }

  return (
    <Carte>
      <TitreDeCarte
        titre={reprise ? `Redessiner « ${reprise.nom ?? reprise.code} »` : 'Nommer la zone'}
        sous={
          reprise
            ? 'Le contour remplacera le précédent ; les prix déjà facturés ne changent pas'
            : 'Le code lie la zone à ses grilles tarifaires'
        }
      />

      <div className="grid gap-4 px-5 pb-5 pt-4 sm:grid-cols-3">
        <Champ
          libelle="Code"
          value={code}
          disabled={reprise !== null}
          onChange={(evenement) => setCode(evenement.target.value)}
          placeholder="cotonou-centre"
          aide={
            reprise
              ? 'Il ne change pas : les grilles de cette zone le nomment.'
              : 'En minuscules, sans espace. Les grilles le reprendront.'
          }
        />
        <Champ
          libelle="Nom"
          value={nom}
          onChange={(evenement) => setNom(evenement.target.value)}
          placeholder="Cotonou — centre"
        />
        <Champ
          libelle="Priorité"
          type="number"
          value={priorite}
          onChange={(evenement) => setPriorite(evenement.target.value)}
          aide="La plus grande gagne quand deux zones se chevauchent."
        />
      </div>

      <div className="flex flex-wrap items-center gap-3 px-5 pb-5">
        <Bouton disabled={occupe || !complet} onClick={enregistrer}>
          {occupe ? 'Enregistrement…' : `Enregistrer (${trace.length} sommets)`}
        </Bouton>

        {!complet ? (
          <p className="text-xs text-encre-3">Le code et le nom sont obligatoires.</p>
        ) : null}
      </div>

      {echec ? <Erreur message={echec} /> : null}
    </Carte>
  );
}
