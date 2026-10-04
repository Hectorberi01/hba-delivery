'use client';

import { useState } from 'react';
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
import { COULEUR_TON } from '@/lib/statuts';
import { envoyer, useRessource } from '@/lib/client/ressource';
import type { PointDeRetrait } from '@/lib/types';

/**
 * Les points de retrait : les adresses ou le livreur vient chercher.
 *
 * C'EST LE COMMERCANT QUI LES TIENT, PAS LE BACK-OFFICE. Les routes vivent
 * sous /api/merchant/v1 et exigent le role merchant ; la console, elle, les
 * affiche en lecture. C'est le bon sens : personne ne connait mieux que lui
 * l'endroit ou se presenter.
 */
export default function PagePointsDeRetrait() {
  const points = useRessource<PointDeRetrait[]>('merchant/v1/pickup-points');
  const [ouvert, setOuvert] = useState(false);

  const liste = points.donnees ?? [];

  return (
    <>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-lg font-semibold text-encre">Points de retrait</h1>
          <p className="text-sm text-encre-3">Où le livreur vient chercher vos colis</p>
        </div>

        <Bouton onClick={() => setOuvert((valeur) => !valeur)}>
          {ouvert ? 'Fermer' : 'Ajouter un point'}
        </Bouton>
      </div>

      {ouvert ? (
        <Nouveau
          ajoute={() => {
            points.recharger();
            setOuvert(false);
          }}
        />
      ) : null}

      <Carte className="overflow-hidden pb-2">
        <TitreDeCarte titre="Vos adresses de collecte" />

        {points.chargement ? <Chargement quoi="de vos points de retrait" /> : null}
        {points.erreur ? <Erreur message={points.erreur} /> : null}
        {!points.chargement && !points.erreur && liste.length === 0 ? (
          <Vide message="Aucun point de retrait. Sans lui, aucune course ne peut partir de chez vous." />
        ) : null}

        {liste.length ? (
          <ul className="mt-4 divide-y divide-bordure">
            {liste.map((point) => (
              <li key={point.id} className="flex flex-wrap items-center justify-between gap-3 px-5 py-3.5">
                <div className="min-w-0">
                  <p className="font-medium text-encre">{point.name || 'Sans nom'}</p>
                  <p className="truncate text-sm text-encre-2">
                    {point.address?.location?.landmark || 'Aucun repère'}
                  </p>
                </div>

                <div className="flex items-center gap-3">
                  <Badge couleur={point.isActive ? COULEUR_TON.bon : COULEUR_TON.neutre}>
                    {point.isActive ? 'ouvert' : 'fermé'}
                  </Badge>

                  {/* FERMER N'EST PAS SUPPRIMER, et le service n'offre que ca :
                      un point ferme ne recoit plus de course et garde son
                      historique. */}
                  <Bascule point={point} change={points.recharger} />
                </div>
              </li>
            ))}
          </ul>
        ) : null}
      </Carte>
    </>
  );
}

function Bascule({ point, change }: { point: PointDeRetrait; change: () => void }) {
  const [envoi, setEnvoi] = useState(false);

  async function basculer() {
    setEnvoi(true);

    try {
      await envoyer(`merchant/v1/pickup-points/${point.id}/active`, { active: !point.isActive });
      change();
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Bouton variante="discret" disabled={envoi} onClick={basculer}>
      {point.isActive ? 'Fermer' : 'Rouvrir'}
    </Bouton>
  );
}

function Nouveau({ ajoute }: { ajoute: () => void }) {
  const [nom, setNom] = useState('');
  const [repere, setRepere] = useState('');
  const [telephone, setTelephone] = useState('');
  const [contact, setContact] = useState('');
  const [latitude, setLatitude] = useState('6.3703');
  const [longitude, setLongitude] = useState('2.3912');
  const [probleme, setProbleme] = useState<string | null>(null);
  const [envoi, setEnvoi] = useState(false);

  async function creer() {
    setEnvoi(true);
    setProbleme(null);

    try {
      await envoyer('merchant/v1/pickup-points', {
        name: nom,
        latitude: Number(latitude),
        longitude: Number(longitude),
        landmark: repere,
        phone: telephone,
        contactName: contact,
        openingHours: [],
      });

      ajoute();
    } catch (cause) {
      setProbleme(cause instanceof Error ? cause.message : 'Échec de l’ajout.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <Carte className="pb-5">
      <TitreDeCarte titre="Nouveau point de retrait" />

      <div className="grid gap-4 px-5 pt-4 sm:grid-cols-2">
        <Champ libelle="Nom" value={nom} onChange={(e) => setNom(e.target.value)} />
        <Champ
          libelle="Repère"
          value={repere}
          onChange={(e) => setRepere(e.target.value)}
          aide="Ce que le livreur cherchera des yeux."
        />
        <Champ libelle="Téléphone sur place" value={telephone} onChange={(e) => setTelephone(e.target.value)} />
        <Champ libelle="Personne à demander" value={contact} onChange={(e) => setContact(e.target.value)} />
        <Champ
          libelle="Latitude"
          type="number"
          step="0.000001"
          value={latitude}
          onChange={(e) => setLatitude(e.target.value)}
        />
        <Champ
          libelle="Longitude"
          type="number"
          step="0.000001"
          value={longitude}
          onChange={(e) => setLongitude(e.target.value)}
        />
      </div>

      <div className="px-5 pt-4">
        {/* LES HORAIRES NE SONT PAS DEMANDES ICI, et le service les accepte :
            le formulaire envoie une liste vide. Les ouvrir demanderait un
            editeur par jour de la semaine, qui merite son propre ecran. */}
        <p className="text-xs text-encre-3">
          Les horaires d’ouverture ne se saisissent pas encore depuis cet écran.
        </p>

        <Bouton className="mt-3" disabled={envoi || !nom || !repere} onClick={creer}>
          Ajouter
        </Bouton>

        {probleme ? (
          <p role="alert" className="mt-3 text-sm text-critique">
            {probleme}
          </p>
        ) : null}
      </div>
    </Carte>
  );
}
