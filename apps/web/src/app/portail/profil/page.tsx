'use client';

import { useEffect, useState } from 'react';
import { Bouton, Carte, Champ, Chargement, Erreur, TitreDeCarte } from '@/composants/base';
import { dateHeure } from '@/lib/format';
import { envoyer, useRessource } from '@/lib/client/ressource';
import type { Commercant } from '@/lib/types';

/**
 * La fiche du commerce, modifiable par son proprietaire.
 *
 * LE SERVICE DECIDE QUI MODIFIE : Directory exige merchant_owner pour
 * UpdateMerchant, et un employe recevra un refus. L'ecran ne duplique pas la
 * regle, il affiche le refus.
 */
export default function PageProfilCommercant() {
  const fiche = useRessource<Commercant>('merchant/v1/profile');

  const [nom, setNom] = useState('');
  const [contact, setContact] = useState('');
  const [telephone, setTelephone] = useState('');
  const [email, setEmail] = useState('');
  const [preparation, setPreparation] = useState('');
  const [message, setMessage] = useState<string | null>(null);
  const [probleme, setProbleme] = useState<string | null>(null);
  const [envoi, setEnvoi] = useState(false);

  // LES CHAMPS SE REMPLISSENT QUAND LA FICHE ARRIVE, et pas avant : des champs
  // vides au chargement se liraient comme une fiche vide.
  useEffect(() => {
    if (!fiche.donnees) return;

    setNom(fiche.donnees.legalName ?? '');
    setContact(fiche.donnees.contactName ?? '');
    setTelephone(fiche.donnees.contactPhone ?? '');
    setEmail(fiche.donnees.contactEmail ?? '');
    setPreparation(String(fiche.donnees.averagePreparationMinutes ?? 0));
  }, [fiche.donnees]);

  async function enregistrer() {
    setEnvoi(true);
    setMessage(null);
    setProbleme(null);

    try {
      await envoyer(
        'merchant/v1/profile',
        {
          legalName: nom,
          contactName: contact,
          contactPhone: telephone,
          contactEmail: email,
          averagePreparationMinutes: Number(preparation) || 0,
        },
        { methode: 'PUT' },
      );

      setMessage('Fiche enregistrée.');
      fiche.recharger();
    } catch (cause) {
      setProbleme(cause instanceof Error ? cause.message : 'Échec de l’enregistrement.');
    } finally {
      setEnvoi(false);
    }
  }

  return (
    <>
      <h1 className="text-lg font-semibold text-encre">Mon commerce</h1>

      <Carte className="mx-auto w-full max-w-2xl pb-5">
        <TitreDeCarte titre={fiche.donnees?.legalName || 'Ma fiche'} sous="Ce que voient nos équipes" />

        {fiche.chargement ? <Chargement quoi="de votre fiche" /> : null}
        {fiche.erreur ? <Erreur message={fiche.erreur} /> : null}

        {fiche.donnees ? (
          <div className="space-y-4 px-5 pt-4">
            <Champ libelle="Nom légal" value={nom} onChange={(e) => setNom(e.target.value)} />
            <Champ
              libelle="Personne à contacter"
              value={contact}
              onChange={(e) => setContact(e.target.value)}
            />
            <Champ libelle="Téléphone" value={telephone} onChange={(e) => setTelephone(e.target.value)} />
            <Champ libelle="E-mail" type="email" value={email} onChange={(e) => setEmail(e.target.value)} />

            {/* CE DELAI N'EST PAS DECORATIF : Dispatch s'en sert pour ne pas
                envoyer un livreur avant que la commande soit prete. Trop court,
                le livreur attend ; trop long, le plat refroidit. */}
            <Champ
              libelle="Préparation moyenne (minutes)"
              type="number"
              min={0}
              value={preparation}
              onChange={(e) => setPreparation(e.target.value)}
              aide="Le livreur ne sera pas envoyé avant ce délai : c’est vous qui le connaissez."
            />

            <Bouton disabled={envoi || !nom} onClick={enregistrer}>
              Enregistrer
            </Bouton>

            {message ? <p className="text-sm text-bon">{message}</p> : null}
            {probleme ? (
              <p role="alert" className="text-sm text-critique">
                {probleme}
              </p>
            ) : null}

            <p className="border-t border-bordure pt-4 text-xs text-encre-3">
              Référencé le {fiche.donnees.createdAt ? dateHeure(fiche.donnees.createdAt) : '—'} ·
              identifiant <span className="font-mono">{fiche.donnees.id}</span>
            </p>
          </div>
        ) : null}
      </Carte>
    </>
  );
}
