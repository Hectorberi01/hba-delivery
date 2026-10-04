'use client';

import { useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Bouton, Carte, Champ, Erreur, TitreDeCarte } from '@/composants/base';
import { xof } from '@/lib/format';
import { chargerGoogleMaps, cleGoogleMaps, type GMap, type GMarker } from '@/lib/client/google-maps';
import { envoyer, useRessource } from '@/lib/client/ressource';
import type { PointDeRetrait } from '@/lib/types';

/** Cotonou. Le centre ne sert qu'avant le premier point pose. */
const COTONOU = { lat: 6.3703, lng: 2.3912 };

type Devis = {
  quoteId: string;
  totalXof?: number;
  distanceMeters?: number;
  durationSeconds?: number;
  expiresAt?: string;
};

/**
 * Commander une course.
 *
 * DEUX TEMPS, ET C'EST LE SUJET DE L'ECRAN : on demande d'abord un devis, on
 * crée ensuite. Le prix est donc ferme AVANT que le commerçant valide, et c'est
 * ce devis-là que Delivery consomme. Sans cette étape, le montant se
 * découvrirait après le débit du compte prépayé.
 *
 * LE COMPTE PREPAYE EST DEBITE A LA CREATION, pas au devis (point 1, tranché le
 * 29 septembre 2026). Un solde insuffisant se voit donc comme un refus de la
 * création — le message du service est affiché tel quel.
 */
export default function PageNouvelleCourse() {
  const routeur = useRouter();
  const points = useRessource<PointDeRetrait[]>('merchant/v1/pickup-points');

  const [pointId, setPointId] = useState('');
  const [destination, setDestination] = useState<{ lat: number; lng: number } | null>(null);
  const [repere, setRepere] = useState('');
  const [nomDestinataire, setNomDestinataire] = useState('');
  const [telDestinataire, setTelDestinataire] = useState('');
  const [contenu, setContenu] = useState('');
  const [poids, setPoids] = useState('1000');
  const [vehicule, setVehicule] = useState('moto');

  const [devis, setDevis] = useState<Devis | null>(null);
  const [probleme, setProbleme] = useState<string | null>(null);
  const [envoi, setEnvoi] = useState(false);

  const collecte = points.donnees?.find((point) => point.id === pointId);
  const depart = collecte?.address?.location?.point;

  // LE DEVIS TOMBE DES QU'UNE DONNEE DU PRIX CHANGE. Garder a l'ecran un prix
  // calcule pour une autre adresse serait la seule facon de facturer autre
  // chose que ce qui a ete montre.
  useEffect(() => {
    setDevis(null);
  }, [pointId, destination, poids, vehicule]);

  async function demanderLeDevis() {
    if (!depart || !destination) return;

    setEnvoi(true);
    setProbleme(null);

    try {
      const calcule = await envoyer<Devis>('merchant/v1/quotes', {
        pickupLatitude: depart.latitude,
        pickupLongitude: depart.longitude,
        dropoffLatitude: destination.lat,
        dropoffLongitude: destination.lng,
        packageWeightGrams: Number(poids) || 0,
        vehicleType: vehicule,
      });

      setDevis(calcule);
    } catch (cause) {
      setProbleme(cause instanceof Error ? cause.message : 'Le devis a échoué.');
    } finally {
      setEnvoi(false);
    }
  }

  async function commander() {
    if (!devis || !depart || !destination || !collecte) return;

    setEnvoi(true);
    setProbleme(null);

    try {
      await envoyer(
        'merchant/v1/deliveries',
        {
          quoteId: devis.quoteId,
          pickupPointId: collecte.id,
          pickup: {
            latitude: depart.latitude,
            longitude: depart.longitude,
            landmark: collecte.address?.location?.landmark ?? collecte.name ?? '',
            phone: collecte.address?.location?.phone ?? '',
            contactName: collecte.address?.location?.contactName ?? '',
          },
          dropoff: {
            latitude: destination.lat,
            longitude: destination.lng,
            landmark: repere,
            phone: telDestinataire,
            contactName: nomDestinataire,
          },
          recipientName: nomDestinataire,
          recipientPhone: telDestinataire,
          packageDescription: contenu,
          packageWeightGrams: Number(poids) || 0,
        },
        // LA CLE D'IDEMPOTENCE PROTEGE DU DOUBLE CLIC, et surtout du renvoi
        // apres une reponse perdue : deux courses creees, deux debits.
        { idempotence: crypto.randomUUID() },
      );

      routeur.push('/portail');
      routeur.refresh();
    } catch (cause) {
      setProbleme(cause instanceof Error ? cause.message : 'La création a échoué.');
    } finally {
      setEnvoi(false);
    }
  }

  const pret = Boolean(depart && destination && nomDestinataire && telDestinataire && repere);

  return (
    <>
      <h1 className="text-lg font-semibold text-encre">Commander une course</h1>

      <div className="grid gap-5 lg:grid-cols-2">
        <Carte className="pb-5">
          <TitreDeCarte titre="Le départ" sous="Un de vos points de retrait" />

          <div className="space-y-4 px-5 pt-4">
            {points.erreur ? <Erreur message={points.erreur} /> : null}

            {points.donnees?.length ? (
              <label className="block">
                <span className="mb-1.5 block text-sm font-medium text-encre-2">
                  Point de retrait
                </span>
                <select
                  value={pointId}
                  onChange={(e) => setPointId(e.target.value)}
                  className="creux w-full rounded-xl border-0 px-4 py-3 text-sm text-encre outline-none"
                >
                  <option value="">Choisir…</option>
                  {points.donnees
                    .filter((point) => point.isActive !== false)
                    .map((point) => (
                      <option key={point.id} value={point.id}>
                        {point.name || 'Sans nom'}
                      </option>
                    ))}
                </select>
              </label>
            ) : (
              // SANS POINT DE RETRAIT, PAS DE DEPART. Le dire ici evite de
              // chercher pourquoi le formulaire ne se valide jamais.
              <p className="text-sm text-encre-3">
                Vous n’avez aucun point de retrait actif. Ajoutez-en un dans « Points de retrait » :
                c’est l’adresse où le livreur vient chercher le colis.
              </p>
            )}

            {collecte ? (
              <p className="creux-doux rounded-xl px-4 py-3 text-sm text-encre-2">
                {collecte.address?.location?.landmark || 'Aucun repère enregistré'}
              </p>
            ) : null}
          </div>
        </Carte>

        <Carte className="pb-5">
          <TitreDeCarte titre="La destination" sous="Cliquez sur la carte pour poser le point" />

          <div className="space-y-4 px-5 pt-4">
            <CarteDeDestination position={destination} choisir={setDestination} />

            <Champ
              libelle="Repère"
              value={repere}
              onChange={(e) => setRepere(e.target.value)}
              aide="Ce que le livreur cherchera des yeux : « pharmacie Zogbo, portail bleu »."
            />
          </div>
        </Carte>
      </div>

      <Carte className="pb-5">
        <TitreDeCarte titre="Le destinataire et le colis" />

        <div className="grid gap-4 px-5 pt-4 sm:grid-cols-2">
          <Champ
            libelle="Nom du destinataire"
            value={nomDestinataire}
            onChange={(e) => setNomDestinataire(e.target.value)}
          />
          <Champ
            libelle="Téléphone du destinataire"
            value={telDestinataire}
            onChange={(e) => setTelDestinataire(e.target.value)}
            placeholder="+229…"
          />
          <Champ
            libelle="Contenu"
            value={contenu}
            onChange={(e) => setContenu(e.target.value)}
            placeholder="Repas, documents, pièces…"
          />
          <Champ
            libelle="Poids (grammes)"
            type="number"
            min={0}
            value={poids}
            onChange={(e) => setPoids(e.target.value)}
          />

          <label className="block">
            <span className="mb-1.5 block text-sm font-medium text-encre-2">Véhicule</span>
            <select
              value={vehicule}
              onChange={(e) => setVehicule(e.target.value)}
              className="creux w-full rounded-xl border-0 px-4 py-3 text-sm text-encre outline-none"
            >
              <option value="moto">Moto</option>
              <option value="velo">Vélo</option>
              <option value="tricycle">Tricycle</option>
              <option value="voiture">Voiture</option>
              <option value="camionnette">Camionnette</option>
            </select>
          </label>
        </div>
      </Carte>

      <Carte className="pb-5">
        <TitreDeCarte titre="Le prix" sous="Ferme avant que vous validiez" />

        <div className="space-y-4 px-5 pt-4">
          {devis ? (
            <div className="creux-doux rounded-xl px-4 py-3">
              <p className="chiffres-tabulaires text-[26px] font-semibold leading-none text-encre">
                {xof(devis.totalXof ?? 0)}
              </p>
              <p className="mt-1.5 text-xs text-encre-3">
                {Math.round((devis.distanceMeters ?? 0) / 100) / 10} km ·{' '}
                {Math.round((devis.durationSeconds ?? 0) / 60)} min estimées. Ce montant sera débité
                de votre compte à la validation.
              </p>
            </div>
          ) : (
            <p className="text-sm text-encre-3">
              Choisissez un départ et une destination, puis demandez le prix.
            </p>
          )}

          <div className="flex flex-wrap gap-2">
            <Bouton variante="discret" disabled={envoi || !depart || !destination} onClick={demanderLeDevis}>
              {devis ? 'Recalculer' : 'Demander le prix'}
            </Bouton>

            <Bouton disabled={envoi || !devis || !pret} onClick={commander}>
              Valider la course
            </Bouton>
          </div>

          {probleme ? (
            <p role="alert" className="text-sm text-critique">
              {probleme}
            </p>
          ) : null}
        </div>
      </Carte>
    </>
  );
}

/**
 * La carte de destination.
 *
 * UN CLIC POSE LE POINT, et il n'y a pas de recherche d'adresse : a Cotonou
 * l'adressage postal ne couvre pas la ville, et c'est le repere qui guide le
 * livreur. Le meme raisonnement vaut dans l'application cliente.
 *
 * SANS CLE GOOGLE MAPS, L'ECRAN NE MENT PAS : il demande les coordonnees a la
 * main plutot que d'afficher un cadre gris.
 */
function CarteDeDestination({
  position,
  choisir,
}: {
  position: { lat: number; lng: number } | null;
  choisir: (p: { lat: number; lng: number }) => void;
}) {
  const hote = useRef<HTMLDivElement | null>(null);
  const carte = useRef<GMap | null>(null);
  const marqueur = useRef<GMarker | null>(null);
  const [panne, setPanne] = useState<string | null>(null);

  useEffect(() => {
    if (!cleGoogleMaps()) {
      setPanne('Carte indisponible : la clé Google Maps n’est pas configurée.');
      return;
    }

    let vivant = true;

    chargerGoogleMaps()
      .then((maps) => {
        if (!vivant || !hote.current) return;

        const instance = new maps.Map(hote.current, {
          center: COTONOU,
          zoom: 13,
          disableDefaultUI: true,
          zoomControl: true,
        });

        instance.addListener('click', (evenement) => {
          const point = evenement.latLng;
          if (!point) return;

          const choisi = { lat: point.lat(), lng: point.lng() };

          if (marqueur.current) {
            marqueur.current.setPosition(choisi);
          } else {
            marqueur.current = new maps.Marker({ position: choisi, map: instance });
          }

          choisir(choisi);
        });

        carte.current = instance;
      })
      .catch((cause: unknown) => {
        if (vivant) setPanne(cause instanceof Error ? cause.message : 'Carte indisponible.');
      });

    return () => {
      vivant = false;
    };
  }, [choisir]);

  if (panne) {
    return (
      <div className="space-y-3">
        <p className="text-sm text-encre-3">{panne}</p>
        <div className="grid gap-3 sm:grid-cols-2">
          <Champ
            libelle="Latitude"
            type="number"
            step="0.000001"
            value={position?.lat ?? ''}
            onChange={(e) => choisir({ lat: Number(e.target.value), lng: position?.lng ?? 0 })}
          />
          <Champ
            libelle="Longitude"
            type="number"
            step="0.000001"
            value={position?.lng ?? ''}
            onChange={(e) => choisir({ lat: position?.lat ?? 0, lng: Number(e.target.value) })}
          />
        </div>
      </div>
    );
  }

  return (
    <div>
      <div ref={hote} className="h-64 w-full overflow-hidden rounded-xl" />
      <p className="mt-2 text-xs text-encre-3">
        {position
          ? `Point posé : ${position.lat.toFixed(5)}, ${position.lng.toFixed(5)}`
          : 'Aucun point posé.'}
      </p>
    </div>
  );
}
