'use client';

import { Carte, Chargement, Erreur, Pastille, TitreDeCarte, Vide } from '@/composants/base';
import { nombre } from '@/lib/format';
import { COULEUR_TON } from '@/lib/statuts';
import { useRessource } from '@/lib/client/ressource';

type Reglage = { cle: string; empeche: string };

type ServiceSonde = {
  nom: string;
  adresse: string;
  sante: string;
  millisecondes: number;
  detail?: string | null;
  reglages?: Reglage[];
};

type Canal = { canal: string; adaptateur: string; configure: boolean };

type EtatPlateforme = {
  services?: ServiceSonde[];
  canaux?: Canal[];
};

/**
 * L'etat de la plateforme, service par service.
 *
 * DEUX CHOSES DIFFERENTES SUR LE MEME ECRAN, ET L'ECRAN LE DIT. La SANTE est
 * mesuree maintenant, par une sonde gRPC. L'inventaire des reglages est
 * DECLARE : la passerelle ne lit pas la configuration des autres conteneurs,
 * et afficher « present » pour un secret qu'elle n'a pas vu serait un mensonge
 * confortable. Seul Notification sait dire si ses canaux ont un fournisseur,
 * et c'est lui qui le dit.
 *
 * AUCUNE VALEUR DE SECRET N'ARRIVE JUSQU'ICI, et rien dans cette page ne peut
 * en ecrire une : on nomme des cles et ce que leur absence empeche.
 */
export default function PagePlateforme() {
  const etat = useRessource<EtatPlateforme>('admin/v1/plateforme');

  const services = etat.donnees?.services ?? [];
  const canaux = etat.donnees?.canaux ?? [];
  const debout = services.filter((service) => service.sante === 'sert').length;

  return (
    <div className="space-y-5">
      <Carte className="px-5 py-4">
        <p className="text-sm text-encre-2">Services qui répondent</p>
        <p className="mt-2 text-[28px] font-semibold leading-none tracking-tight text-encre">
          {nombre(debout)} / {nombre(services.length)}
        </p>
        <p className="mt-1.5 text-xs text-encre-3">
          Sonde gRPC, échéance de deux secondes, mesurée à l’ouverture de la page.
        </p>
      </Carte>

      <Carte className="overflow-hidden pb-1">
        <TitreDeCarte
          titre="Services"
          sous="Santé mesurée ; réglages attendus, déclarés — leur présence réelle n’est pas lisible d’ici"
        />

        {etat.chargement ? <Chargement quoi="de la plateforme" /> : null}
        {etat.erreur ? <Erreur message={etat.erreur} /> : null}
        {!etat.chargement && !etat.erreur && services.length === 0 ? (
          <Vide message="Aucun service déclaré." />
        ) : null}

        {services.length ? (
          <ul className="mt-4 divide-y divide-bordure">
            {services.map((service) => (
              <li key={service.nom} className="px-5 py-4">
                <div className="flex flex-wrap items-baseline justify-between gap-3">
                  <span className="font-medium text-encre">{service.nom}</span>
                  <span className="flex items-center gap-4">
                    <span className="chiffres-tabulaires text-xs text-encre-3">
                      {nombre(service.millisecondes)} ms
                    </span>
                    <Pastille
                      couleur={
                        service.sante === 'sert'
                          ? COULEUR_TON.bon
                          : service.sante === 'ne sert pas'
                            ? COULEUR_TON.attention
                            : COULEUR_TON.critique
                      }
                      libelle={service.sante}
                    />
                  </span>
                </div>

                <p className="mt-0.5 font-mono text-[13px] text-encre-3">
                  {service.adresse}
                  {service.detail && service.sante !== 'sert' ? ` · ${service.detail}` : ''}
                </p>

                {service.reglages?.length ? (
                  <dl className="mt-3 space-y-1.5">
                    {service.reglages.map((reglage) => (
                      <div key={reglage.cle} className="flex flex-wrap items-baseline gap-x-2">
                        <dt className="font-mono text-[13px] text-encre-2">{reglage.cle}</dt>
                        {/* CE QUE SON ABSENCE EMPECHE, et non « present » ou
                            « absent » : la passerelle ne lit pas la
                            configuration des autres conteneurs. */}
                        <dd className="text-sm text-encre-3">sans lui : {reglage.empeche}</dd>
                      </div>
                    ))}
                  </dl>
                ) : null}
              </li>
            ))}
          </ul>
        ) : null}
      </Carte>

      <Carte className="overflow-hidden pb-5">
        <TitreDeCarte
          titre="Canaux de notification"
          sous="Mesuré par Notification lui-même : ce qu’il a réellement résolu au démarrage"
        />

        {canaux.length ? (
          <ul className="mt-4 divide-y divide-bordure">
            {canaux.map((canal) => (
              <li key={canal.canal} className="flex flex-wrap items-baseline justify-between gap-3 px-5 py-3">
                <span className="text-encre">{canal.canal}</span>
                <span className="flex items-center gap-4">
                  <span className="font-mono text-[13px] text-encre-3">{canal.adaptateur}</span>
                  <Pastille
                    couleur={canal.configure ? COULEUR_TON.bon : COULEUR_TON.attention}
                    libelle={canal.configure ? 'fournisseur configuré' : 'aucun fournisseur'}
                  />
                </span>
              </li>
            ))}
          </ul>
        ) : (
          <Vide message="Non mesuré — Notification n’a pas répondu. Sa sonde, ci-dessus, dit pourquoi." />
        )}

        {/* AUCUN FOURNISSEUR N'EST UNE DECISION, PAS UNE PANNE, et le journal
            des notifications consigne alors « ignore » plutot qu'« echoue ». */}
        <p className="px-5 pt-4 text-xs text-encre-3">
          Un canal sans fournisseur n’est pas en panne : ses messages sont consignés « ignorés » dans
          le journal des notifications, et ne partent pas.
        </p>
      </Carte>

      <Carte className="px-5 py-4">
        <p className="text-sm font-medium text-encre">Les secrets ne se modifient pas ici</p>
        <p className="mt-1.5 text-sm text-encre-2">
          Cette page nomme ce dont chaque service a besoin et ce que son absence empêche. Elle ne lit
          aucune valeur et n’en écrit aucune : les secrets vivent dans le <code>.env</code> de chaque
          service, posé sur le serveur, et un changement passe par un redéploiement.
        </p>
      </Carte>
    </div>
  );
}
