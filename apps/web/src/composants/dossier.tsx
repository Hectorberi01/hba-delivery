'use client';

import { Carte, Chargement, TitreDeCarte, Vide } from '@/composants/base';
import { dateHeure } from '@/lib/format';
import { useRessource } from '@/lib/client/ressource';
import type { DossierLivreur } from '@/lib/types';

const LIBELLES: Record<string, string> = {
  NationalId: "Carte d'identité",
  DrivingLicence: 'Permis de conduire',
  VehicleRegistration: 'Carte grise',
  IdentityPhoto: 'Photo du livreur',
  VehiclePhoto: 'Photo du véhicule',
};

const VEHICULES: Record<string, string> = {
  Motorcycle: 'Moto',
  Car: 'Voiture',
  Van: 'Camionnette',
  Bicycle: 'Vélo',
  Tricycle: 'Tricycle',
  Unspecified: 'Non déclaré',
};

/**
 * Les pièces du dossier, à côté des boutons de décision.
 *
 * SANS CET ENCART, OPS VALIDAIT À L'AVEUGLE. La page proposait « Valider le
 * KYC » et « Rejeter » sans jamais montrer ce qu'il y avait à examiner —
 * c'était un bouton de confiance, pas une décision.
 */
export function DossierDuLivreur({ identifiant }: { identifiant: string }) {
  const dossier = useRessource<DossierLivreur>(
    `admin/v1/drivers/${identifiant}/application`,
  );

  return (
    <Carte>
      <TitreDeCarte titre="Dossier" sous="Pièces justificatives et véhicule" />

      {dossier.chargement ? <Chargement quoi="du dossier" /> : null}

      {dossier.erreur ? (
        <div className="px-5 pb-5 pt-4">
          <p className="text-sm text-encre-3">
            Dossier illisible. Le service Driver ne répond pas, ou le stockage des
            pièces est indisponible — dans les deux cas, ne décidez pas sans avoir vu.
          </p>
        </div>
      ) : null}

      {dossier.donnees ? <Contenu dossier={dossier.donnees} /> : null}
    </Carte>
  );
}

function Contenu({ dossier }: { dossier: DossierLivreur }) {
  const pieces = dossier.documents ?? [];
  const manquantes = dossier.missingDocuments ?? [];

  return (
    <div className="space-y-4 px-5 pb-5 pt-4">
      <dl className="space-y-2 text-sm">
        <Ligne
          terme="Véhicule"
          valeur={
            dossier.vehicle?.plate
              ? `${VEHICULES[dossier.vehicle.type ?? ''] ?? dossier.vehicle.type} — ${dossier.vehicle.plate}`
              : 'Non déclaré'
          }
        />
        <Ligne
          terme="Envoyé le"
          valeur={dossier.submittedAt ? dateHeure(dossier.submittedAt) : 'Jamais envoyé'}
        />
      </dl>

      {manquantes.length ? (
        <div className="creux-doux rounded-xl border border-attention px-4 py-3">
          <p className="text-sm text-encre-2">
            {/* NOMMÉES, PAS COMPTÉES. « 2 pièces manquantes » oblige à
                chercher lesquelles. */}
            Pièces manquantes : {manquantes.map((m) => LIBELLES[m] ?? m).join(', ')}.
          </p>
        </div>
      ) : null}

      {pieces.length ? (
        <ul className="grid grid-cols-2 gap-3">
          {pieces.map((piece) => (
            <li key={piece.type}>
              <a
                href={piece.readUrl}
                target="_blank"
                rel="noreferrer"
                className="relief-doux pressable block overflow-hidden rounded-xl"
              >
                <span className="creux block h-28 w-full">
                  {/* Une pièce peut être un PDF : l'aperçu échoue alors, et
                      le lien reste le bon moyen de l'ouvrir. */}
                  {piece.contentType === 'application/pdf' ? (
                    <span className="flex h-full items-center justify-center text-sm text-encre-3">
                      PDF
                    </span>
                  ) : (
                    // eslint-disable-next-line @next/next/no-img-element
                    <img
                      src={piece.readUrl}
                      alt={LIBELLES[piece.type ?? ''] ?? piece.type}
                      className="h-full w-full object-cover"
                    />
                  )}
                </span>
                <span className="block px-3 py-2">
                  <span className="block text-sm font-medium text-encre">
                    {LIBELLES[piece.type ?? ''] ?? piece.type}
                  </span>
                  <span className="block text-xs text-encre-3">
                    {piece.uploadedAt ? dateHeure(piece.uploadedAt) : '—'}
                  </span>
                </span>
              </a>
            </li>
          ))}
        </ul>
      ) : (
        <Vide message="Aucune pièce déposée." />
      )}

      {/* L'URL SIGNÉE EXPIRE EN QUELQUES MINUTES. Une vignette cassée n'est
          donc pas une panne, c'est un onglet resté ouvert — il faut le dire,
          sinon ops croit le dossier corrompu. */}
      {pieces.length ? (
        <p className="text-xs text-encre-3">
          Les aperçus expirent après quelques minutes. Rechargez la page si une image
          ne s&apos;affiche plus.
        </p>
      ) : null}
    </div>
  );
}

function Ligne({ terme, valeur }: { terme: string; valeur: string }) {
  return (
    <div className="flex items-baseline justify-between gap-4">
      <dt className="text-encre-3">{terme}</dt>
      <dd className="text-right font-medium text-encre">{valeur}</dd>
    </div>
  );
}
