'use client';

import { useMemo, useState } from 'react';
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
import { envoyer, useRessource } from '@/lib/client/ressource';
import { dateHeure, xof } from '@/lib/format';
import { COULEUR_TON } from '@/lib/statuts';
import type { GrilleTarifaire, ZoneTarifaire } from '@/lib/types';

/**
 * Les grilles tarifaires.
 *
 * CET ECRAN REMPLACE UNE VARIABLE D'ENVIRONNEMENT ET UN REDEMARRAGE. Jusqu'ici
 * la seule grille du systeme etait semee au demarrage par InitialTariffSeeder,
 * pour UN couple (zone, vehicule) : changer un prix demandait d'editer la
 * configuration et de RECREER le conteneur, et les quatre autres vehicules
 * n'avaient aucun prix du tout — toute course les concernant echouait sur
 * NO_TARIFF.
 *
 * ON NE MODIFIE JAMAIS UNE GRILLE, ON EN CREE UNE VERSION (ADR 0004). Un devis
 * emis hier se reclame de la grille d'hier : reecrire les montants en place
 * changerait le passe. Creer une nouvelle version clot l'ancienne a la meme
 * seconde, sans instant creux.
 *
 * FINANCE ET ADMIN SEULEMENT. Ops et support sont du back-office mais ne
 * touchent pas aux baremes ; la passerelle le verifie, Pricing le reverifie
 * (ADR 0007), et l'onglet n'apparait pas pour les autres.
 */
export default function TarifsPage() {
  const [zone, setZone] = useState('');
  const [nature, setNature] = useState('');
  const [vehicule, setVehicule] = useState('');
  const [closes, setCloses] = useState(false);

  /** Les filtres vides ne partent pas : la route les traite comme « tout ». */
  const chemin = useMemo(() => {
    const parametres = new URLSearchParams();
    if (zone.trim()) parametres.set('zone', zone.trim());
    if (nature) parametres.set('nature', nature);
    if (vehicule) parametres.set('vehicule', vehicule);
    if (closes) parametres.set('closes', 'true');

    const requete = parametres.toString();
    return `admin/v1/tariffs${requete ? `?${requete}` : ''}`;
  }, [zone, nature, vehicule, closes]);

  const grilles = useRessource<GrilleTarifaire[]>(chemin);

  /** La grille dont on prepare une nouvelle version. */
  const [reprise, setReprise] = useState<GrilleTarifaire | null>(null);
  const [formulaireOuvert, setFormulaireOuvert] = useState(false);

  return (
    // LA PAGE NE DEFILE PLUS, LE TABLEAU SI.
    //
    // Trente grilles poussaient les filtres hors de l'ecran : pour changer de
    // zone il fallait remonter, et pour voir l'effet redescendre. Les filtres et
    // le formulaire restent donc fixes, et seules les lignes bougent.
    //
    // CE N'EST PAS UNE PAGINATION, ET LA NUANCE COMPTE : le service rend tout, et
    // tout reste atteignable. On change seulement ce qui defile.
    <div className="flex min-h-0 flex-1 flex-col gap-5">
      <Carte>
        <TitreDeCarte
          titre="Grilles tarifaires"
          sous="Une grille par zone, nature, véhicule et plage horaire"
          action={
            <Bouton
              variante={formulaireOuvert ? 'discret' : 'principal'}
              onClick={() => {
                setReprise(null);
                setFormulaireOuvert((ouvert) => !ouvert);
              }}
            >
              {formulaireOuvert ? 'Fermer' : 'Nouvelle grille'}
            </Bouton>
          }
        />

        <div className="grid gap-4 px-5 pb-5 pt-4 sm:grid-cols-4">
          <Champ
            libelle="Zone"
            value={zone}
            onChange={(evenement) => setZone(evenement.target.value)}
            placeholder="cotonou"
          />
          <Liste
            libelle="Nature"
            valeur={nature}
            onChange={setNature}
            options={[
              { valeur: '', libelle: 'Les deux' },
              { valeur: 'colis', libelle: 'Colis' },
              { valeur: 'course', libelle: 'Course de personnes' },
            ]}
          />
          <Liste
            libelle="Véhicule"
            valeur={vehicule}
            onChange={setVehicule}
            options={[
              { valeur: '', libelle: 'Tous' },
              ...VEHICULES.map((v) => ({ valeur: v.code, libelle: v.libelle })),
            ]}
          />
          <label className="flex items-end gap-2 pb-3 text-sm text-encre-2">
            <input
              type="checkbox"
              checked={closes}
              onChange={(evenement) => setCloses(evenement.target.checked)}
              className="h-4 w-4"
            />
            {/* L'HISTORIQUE NE SE SUPPRIME JAMAIS, mais l'afficher toujours
                noierait la seule ligne qui compte sous toutes celles qui ne
                comptent plus. */}
            Afficher les versions closes
          </label>
        </div>
      </Carte>

      {formulaireOuvert ? (
        <FormulaireDeGrille
          // LA CLE REMONTE LE FORMULAIRE QUAND ON CHANGE DE GRILLE REPRISE.
          // Sans elle, demander « nouvelle version » d'une seconde grille alors
          // que le formulaire est deja ouvert laisserait les champs de la
          // premiere : les valeurs initiales d'un useState ne sont lues qu'au
          // montage. Elle renouvelle aussi la cle d'idempotence.
          key={reprise?.id ?? 'neuve'}
          reprise={reprise}
          rafraichir={grilles.recharger}
          apres={() => {
            setFormulaireOuvert(false);
            setReprise(null);
            grilles.recharger();
          }}
        />
      ) : null}

      <Carte className="flex min-h-0 flex-1 flex-col">
        <TitreDeCarte titre="En vigueur" sous="Ce qui chiffre les devis en ce moment" />

        {grilles.chargement ? <Chargement quoi="des grilles" /> : null}
        {grilles.erreur ? <Erreur message={grilles.erreur} /> : null}

        {grilles.donnees && grilles.donnees.length === 0 ? (
          <Vide
            message={
              nature === 'course'
                ? "Aucune grille de course. Tant qu'il n'y en a pas, chaque devis de course échoue sur NO_TARIFF."
                : 'Aucune grille pour ces critères.'
            }
          />
        ) : null}

        {grilles.donnees && grilles.donnees.length > 0 ? (
          // LE DEFILEMENT EST SUR LES DEUX AXES, et la hauteur minimale evite
          // qu'une carte haute de trois lignes se reduise a rien sur un petit
          // ecran — « flex-1 » dans un conteneur deja court donne zero.
          <div className="min-h-[280px] flex-1 overflow-auto px-5 pb-5 pt-2">
            <table className="w-full min-w-[1000px] text-sm">
              <thead>
                {/* L'ENTETE RESTE VISIBLE PENDANT QU'ON DESCEND. Treize colonnes
                    de chiffres sans leur titre ne se lisent pas : « 500 F » est
                    un plancher ou une prise en charge selon la colonne.

                    LE FOND EST OPAQUE, SANS QUOI LES LIGNES DEFILERAIENT AU
                    TRAVERS. Une entete collante transparente laisse voir le
                    texte qui passe dessous, et c'est illisible. */}
                <tr className="text-left text-xs uppercase tracking-wide text-encre-3">
                  <th className="sticky top-0 z-10 bg-surface pb-2 pt-2 pr-4 font-medium">Zone</th>
                  <th className="sticky top-0 z-10 bg-surface pb-2 pt-2 pr-4 font-medium">Nature</th>
                  <th className="sticky top-0 z-10 bg-surface pb-2 pt-2 pr-4 font-medium">Véhicule</th>
                  <th className="sticky top-0 z-10 bg-surface pb-2 pt-2 pr-4 font-medium">Plage</th>
                  <th className="sticky top-0 z-10 bg-surface pb-2 pt-2 pr-4 text-right font-medium">Prise en charge</th>
                  <th className="sticky top-0 z-10 bg-surface pb-2 pt-2 pr-4 text-right font-medium">Par km</th>
                  <th className="sticky top-0 z-10 bg-surface pb-2 pt-2 pr-4 text-right font-medium">Par min</th>
                  <th className="sticky top-0 z-10 bg-surface pb-2 pt-2 pr-4 text-right font-medium">Plancher</th>
                  <th className="sticky top-0 z-10 bg-surface pb-2 pt-2 pr-4 text-right font-medium">Au kilo</th>
                  <th className="sticky top-0 z-10 bg-surface pb-2 pt-2 pr-4 text-right font-medium">Part livreur</th>
                  <th className="sticky top-0 z-10 bg-surface pb-2 pt-2 pr-4 text-right font-medium">Retenue annul.</th>
                  <th className="sticky top-0 z-10 bg-surface pb-2 pt-2 pr-4 font-medium">Depuis</th>
                  <th className="sticky top-0 z-10 bg-surface pb-2 pt-2 font-medium" />
                </tr>
              </thead>
              <tbody className="text-encre-2">
                {grilles.donnees.map((grille) => (
                  <Ligne
                    key={grille.id ?? `${grille.zone}-${grille.nature}-${grille.vehicule}-${grille.debut}`}
                    grille={grille}
                    onNouvelleVersion={() => {
                      setReprise(grille);
                      setFormulaireOuvert(true);
                    }}
                    apres={grilles.recharger}
                  />
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </Carte>
    </div>
  );
}

/**
 * La valeur qui veut dire « toutes » dans les deux listes du formulaire.
 *
 * UNE ETOILE, ET NON LA CHAINE VIDE. Le vide veut deja dire « a choisir » dans ce
 * formulaire — le bouton reste gris tant qu'il y est — et confondre les deux aurait
 * rendu possible une creation sans nature, c'est-a-dire exactement ce que le
 * contrat refuse.
 */
const TOUT = '*';

/**
 * Le neutre des points de base d'un MULTIPLICATEUR : 10000 vaut 1,00.
 *
 * IL NE S'APPLIQUE QU'A LA MAJORATION. La part du livreur est aussi en points de
 * base, mais c'est une FRACTION — 8000 y vaut 80 % — et son neutre n'existe pas.
 * Les deux champs portent la meme unite et des sens opposes ; c'est le domaine qui
 * en decide, et cette constante empeche de les confondre une seconde fois.
 */
const NEUTRE = 10_000;

const VEHICULES = [
  { code: 'moto', libelle: 'Moto' },
  { code: 'voiture', libelle: 'Voiture' },
  { code: 'camionnette', libelle: 'Camionnette' },
  { code: 'velo', libelle: 'Vélo' },
  { code: 'tricycle', libelle: 'Tricycle' },
];

/** Le nom du contrat vers le mot que la route attend. */
const CODE_DU_VEHICULE: Record<string, string> = {
  Motorcycle: 'moto',
  Car: 'voiture',
  Van: 'camionnette',
  Bicycle: 'velo',
  Tricycle: 'tricycle',
};

const LIBELLE_DU_VEHICULE: Record<string, string> = {
  Motorcycle: 'Moto',
  Car: 'Voiture',
  Van: 'Camionnette',
  Bicycle: 'Vélo',
  Tricycle: 'Tricycle',
  Unspecified: 'Non précisé',
};

const LIBELLE_DE_LA_NATURE: Record<string, string> = {
  Delivery: 'Colis',
  Ride: 'Course',
  Unspecified: 'Non précisée',
};

function Ligne({
  grille,
  onNouvelleVersion,
  apres,
}: {
  grille: GrilleTarifaire;
  onNouvelleVersion: () => void;
  apres: () => void;
}) {
  const [confirme, setConfirme] = useState(false);
  const [occupe, setOccupe] = useState(false);
  const [echec, setEchec] = useState<string | null>(null);

  async function clore() {
    setOccupe(true);
    setEchec(null);

    try {
      await envoyer(`admin/v1/tariffs/${grille.id}/close`, {});
      apres();
    } catch (cause: unknown) {
      setEchec(cause instanceof Error ? cause.message : 'Clôture impossible.');
    } finally {
      setOccupe(false);
      setConfirme(false);
    }
  }

  return (
    <>
      <tr className="border-t border-bordure">
        <td className="py-3 pr-4">{grille.zone ?? '—'}</td>
        <td className="py-3 pr-4">
          <Badge couleur={grille.nature === 'Ride' ? COULEUR_TON.encours : COULEUR_TON.neutre}>
            {LIBELLE_DE_LA_NATURE[grille.nature ?? ''] ?? grille.nature ?? '—'}
          </Badge>
        </td>
        <td className="py-3 pr-4">
          {LIBELLE_DU_VEHICULE[grille.vehicule ?? ''] ?? grille.vehicule ?? '—'}
        </td>
        <td className="py-3 pr-4">{grille.plage ?? '—'}</td>
        <td className="py-3 pr-4 text-right">{xof(grille.priseEnCharge)}</td>
        <td className="py-3 pr-4 text-right">{xof(grille.parKilometre)}</td>
        <td className="py-3 pr-4 text-right">{xof(grille.parMinute)}</td>
        <td className="py-3 pr-4 text-right">{xof(grille.plancher)}</td>
        <td className="py-3 pr-4 text-right">
          {grille.auKilo
            ? `${xof(grille.auKilo)} au-delà de ${Math.round((grille.poidsInclusGrammes ?? 0) / 1000)} kg`
            : '—'}
        </td>
        <td className="py-3 pr-4 text-right">
          {/* POINTS DE BASE VERS POUR CENT : 7000 se lit 70 %. Le chiffre brut
              du contrat ne se lit pas, et personne ne raisonne en centiemes de
              pour cent. */}
          {pourCent(grille.partDuLivreurPointsDeBase)}
        </td>
        <td className="py-3 pr-4 text-right">
          {/* ZERO S'AFFICHE « GRATUIT », PAS « 0 % ». Les deux disent la meme
              chose au calcul et pas du tout la meme a la lecture : une colonne
              de « 0 % » se lit comme un champ oublie. */}
          {grille.retenueAnnulationPointsDeBase
            ? pourCent(grille.retenueAnnulationPointsDeBase)
            : 'gratuit'}
        </td>
        <td className="py-3 pr-4 text-xs text-encre-3">
          {grille.debut ? dateHeure(grille.debut) : '—'}
          {grille.enVigueur === false && grille.fin ? (
            <span className="block">closé le {dateHeure(grille.fin)}</span>
          ) : null}
        </td>
        <td className="py-3 text-right">
          <div className="flex items-center justify-end gap-2">
            <Bouton variante="discret" onClick={onNouvelleVersion}>
              Nouvelle version
            </Bouton>

            {/* CLORE SANS REMPLACER LAISSE LE COUPLE SANS PRIX, et c'est parfois
                ce qu'on veut : retirer le tricycle d'une ville. Le refus
                NO_TARIFF est alors la bonne réponse — meilleure qu'un prix
                inventé. */}
            {grille.enVigueur !== false ? (
              confirme ? (
                <Bouton variante="danger" disabled={occupe} onClick={clore}>
                  {occupe ? 'Clôture…' : 'Confirmer'}
                </Bouton>
              ) : (
                <Bouton variante="discret" onClick={() => setConfirme(true)}>
                  Clore
                </Bouton>
              )
            ) : null}
          </div>
        </td>
      </tr>
      {echec ? (
        <tr>
          <td colSpan={13} className="pb-3">
            <Erreur message={echec} />
          </td>
        </tr>
      ) : null}
    </>
  );
}

/**
 * Creer une grille, ou une nouvelle version d'une grille existante.
 *
 * AUCUN MONTANT N'EST PRE-REMPLI POUR UNE NATURE NEUVE, et c'est deliberé. Les
 * prix d'une course de personnes ne sont pas ceux d'un colis — ni le plancher,
 * ni la part du chauffeur — et proposer les seconds comme point de depart
 * donnerait des chiffres faux qui ont l'air justes. « Nouvelle version » reprend
 * en revanche la grille qu'on remplace : c'est le meme bareme qu'on ajuste.
 */
function FormulaireDeGrille({
  reprise,
  apres,
  rafraichir,
}: {
  reprise: GrilleTarifaire | null;
  apres: () => void;

  /**
   * Relire la liste SANS fermer le formulaire.
   *
   * IL SERT QUAND UNE DEMULTIPLICATION ECHOUE A MOITIE : sept grilles creees, trois
   * refusees. Fermer effacerait le detail des trois ; ne pas relire cacherait les
   * sept. On fait donc les deux moities separement.
   */
  rafraichir: () => void;
}) {
  const [version, setVersion] = useState(reprise?.version ?? '');
  const [zone, setZone] = useState(reprise?.zone ?? '');
  const [nature, setNature] = useState(
    reprise?.nature === 'Ride' ? 'course' : reprise?.nature === 'Delivery' ? 'colis' : '',
  );
  const [vehicule, setVehicule] = useState(CODE_DU_VEHICULE[reprise?.vehicule ?? ''] ?? '');

  const [priseEnCharge, setPriseEnCharge] = useState(texteDe(reprise?.priseEnCharge));
  const [parKilometre, setParKilometre] = useState(texteDe(reprise?.parKilometre));
  const [parMinute, setParMinute] = useState(texteDe(reprise?.parMinute));
  const [plancher, setPlancher] = useState(texteDe(reprise?.plancher));
  const [auKilo, setAuKilo] = useState(texteDe(reprise?.auKilo));
  const [poidsInclusKg, setPoidsInclusKg] = useState(
    reprise?.poidsInclusGrammes ? `${Math.round(reprise.poidsInclusGrammes / 1000)}` : '',
  );
  const [partDuLivreur, setPartDuLivreur] = useState(
    reprise?.partDuLivreurPointsDeBase ? `${reprise.partDuLivreurPointsDeBase / 100}` : '',
  );
  // 10200 SE RELIT « 2 », PAS « 102 » : la majoration est un MULTIPLICATEUR dont
  // 10000 est le neutre. Voir la conversion a l'envoi.
  const [majoration, setMajoration] = useState(
    reprise?.majorationPointsDeBase && reprise.majorationPointsDeBase > NEUTRE
      ? `${(reprise.majorationPointsDeBase - NEUTRE) / 100}`
      : '',
  );
  // LA RETENUE EST UNE FRACTION, COMME LA PART DU LIVREUR : 2000 se relit « 20 ».
  // Elle ne suit PAS la regle de la majoration juste au-dessus.
  const [retenue, setRetenue] = useState(
    reprise?.retenueAnnulationPointsDeBase
      ? `${reprise.retenueAnnulationPointsDeBase / 100}`
      : '',
  );
  const [debut, setDebut] = useState(heureDe(reprise?.plageDebutMinute));
  const [fin, setFin] = useState(heureDe(reprise?.plageFinMinute));

  const [occupe, setOccupe] = useState(false);
  const [echec, setEchec] = useState<string | null>(null);

  /**
   * Les zones reellement dessinees.
   *
   * UNE LISTE PLUTOT QU'UN CHAMP LIBRE, DEPUIS QUE LES ZONES EXISTENT (point 40).
   * Une faute de frappe dans un code de zone ne se voit pas : elle cree une grille
   * pour un territoire qui n'existe pas, que personne ne consultera jamais, et le
   * devis de la vraie zone continuera d'echouer sur NO_TARIFF.
   *
   * LE CHAMP LIBRE RESTE LE REPLI tant qu'aucune zone n'est dessinee : sans lui, un
   * depot neuf n'aurait aucun moyen de creer sa premiere grille.
   */
  const zonesConnues = useRessource<ZoneTarifaire[]>('admin/v1/zones');

  const codesDeZone = (zonesConnues.donnees ?? [])
    .map((z) => z.code ?? '')
    .filter((code) => code !== '');

  /**
   * La cle d'idempotence de CE formulaire.
   *
   * UNE PAR MONTAGE, ET C'EST LA BONNE PORTEE. Un double appui sur « Creer »
   * rejoue la meme cle et Pricing rend la grille deja creee au lieu d'en poser
   * une seconde, qui clorait la premiere a la meme seconde. Et comme le
   * formulaire se ferme — donc se demonte — apres un succes, la grille suivante
   * part avec une cle neuve : une cle de portee page aurait fait rendre
   * silencieusement la premiere grille a la seconde demande.
   */
  const [cle] = useState(
    () =>
      typeof crypto !== 'undefined' && 'randomUUID' in crypto
        ? crypto.randomUUID()
        : `grille-${Date.now()}`,
  );

  const complet =
    version.trim() !== '' &&
    zone.trim() !== '' &&
    nature !== '' &&
    vehicule !== '' &&
    plancher.trim() !== '' &&
    partDuLivreur.trim() !== '';

  /**
   * Les couples (nature, vehicule) a creer.
   *
   * « TOUT » SE DEMULTIPLIE ICI, ET NULLE PART AILLEURS (point 40). Le contrat
   * refuse une grille sans nature ni vehicule : a la creation, UNSPECIFIED est un
   * REFUS, precisement pour qu'un bareme de colis ne s'ecrive pas sous le nom d'une
   * course. On cree donc N grilles explicites — jusqu'a dix — chacune lisible,
   * modifiable et closable separement, et un prix reste toujours attribuable a une
   * grille nommee.
   *
   * L'ALTERNATIVE A ETE ECARTEE : une grille dont la nature et le vehicule seraient
   * facultatifs, resolue du plus precis au plus general. Elle tient en une ligne de
   * table et change la resolution du prix — le chemin le plus sensible du systeme.
   */
  const combinaisons = useMemo(() => {
    const natures = nature === TOUT ? ['colis', 'course'] : [nature];
    const vehicules = vehicule === TOUT ? VEHICULES.map((v) => v.code) : [vehicule];

    return natures.flatMap((n) => vehicules.map((v) => ({ nature: n, vehicule: v })));
  }, [nature, vehicule]);

  async function creer() {
    setOccupe(true);
    setEchec(null);

    const refus: string[] = [];

    for (const couple of combinaisons) {
      try {
        await envoyer(
          'admin/v1/tariffs',
          {
            version: version.trim(),
            zone: zone.trim(),
            nature: couple.nature,
            vehicule: couple.vehicule,
            priseEnCharge: entier(priseEnCharge),
            parKilometre: entier(parKilometre),
            parMinute: entier(parMinute),
            plancher: entier(plancher),

            // DEUX CHAMPS EN « POINTS DE BASE », DEUX SENS OPPOSES, ET C'EST LE
            // PIEGE QUI A FAIT REFUSER LES DIX PREMIERES GRILLES.
            //
            // LA PART DU LIVREUR EST UNE FRACTION : 10000 vaut la totalite, donc
            // 80 % s'ecrit 8000. Le domaine la veut dans ]0, 10000].
            //
            // LA MAJORATION EST UN MULTIPLICATEUR : 10000 vaut 1,00, c'est-a-dire
            // AUCUNE majoration, et le domaine refuse en dessous — « une majoration
            // ne peut pas reduire le prix ». Envoyer 200 pour « 2 % » demandait donc
            // de multiplier le prix par 0,02. L'ecran demande la hausse, et c'est
            // ici qu'elle devient un multiplicateur.
            majorationPointsDeBase: NEUTRE + Math.round(nombreDecimal(majoration) * 100),
            partDuLivreurPointsDeBase: Math.round(nombreDecimal(partDuLivreur) * 100),

            // DEUX BORNES ABSENTES VALENT LA JOURNEE ENTIERE, et c'est Pricing qui
            // le traduit : en JSON comme en protobuf3, l'absence et le zero sont la
            // meme chose.
            plageDebutMinute: minutesDe(debut),
            plageFinMinute: minutesDe(fin),
            auKilo: auKilo.trim() === '' ? null : entier(auKilo),
            poidsInclusGrammes:
              poidsInclusKg.trim() === '' ? null : entier(poidsInclusKg) * 1000,

            // VIDE VAUT GRATUIT, ET C'EST LE SEUL DEFAUT SUR. Une grille saisie
            // sans retenue ne preleve rien : au pire le client annule sans frais.
            // Le defaut inverse prelerait une somme que personne n'a decidee.
            retenueAnnulationPointsDeBase:
              retenue.trim() === '' ? null : Math.round(nombreDecimal(retenue) * 100),
          },

          // UNE CLE PAR COUPLE, ET SURTOUT PAS LA MEME POUR TOUS. Avec une cle
          // unique, la deuxieme grille de la boucle serait un REJEU : Pricing
          // rendrait la premiere sans rien creer, et neuf grilles sur dix
          // manqueraient sans qu'aucune erreur ne le dise.
          { idempotence: `${cle}:${couple.nature}:${couple.vehicule}` },
        );
      } catch (cause: unknown) {
        const quoi = `${couple.nature} / ${couple.vehicule}`;
        refus.push(`${quoi} : ${cause instanceof Error ? cause.message : 'refus inattendu'}`);
      }
    }

    setOccupe(false);

    // CE QUI A ETE CREE RESTE CREE, ET ON LE MONTRE. Une demultiplication qui
    // echoue a moitie — par exemple parce qu'une grille existe deja sur la meme
    // plage — ne doit ni effacer les reussites ni cacher les refus.
    if (refus.length > 0) {
      setEchec(
        refus.length === combinaisons.length
          ? refus.join(' · ')
          : `${combinaisons.length - refus.length} grille(s) créée(s). Refusées — ${refus.join(' · ')}`,
      );

      rafraichir();
      return;
    }

    apres();
  }

  return (
    <Carte>
      <TitreDeCarte
        titre={reprise ? 'Nouvelle version de la grille' : 'Nouvelle grille'}
        sous={
          reprise
            ? 'La version en vigueur sera close à la seconde où celle-ci commence'
            : 'Une grille existante pour le même quadruplet sera close automatiquement'
        }
      />

      <div className="grid gap-4 px-5 pb-5 pt-4 sm:grid-cols-2 lg:grid-cols-4">
        <Champ
          libelle="Version"
          value={version}
          onChange={(e) => setVersion(e.target.value)}
          placeholder="2026-10"
          aide="Une étiquette libre, pour retrouver la décision qui l'a produite."
        />
        {codesDeZone.length > 0 ? (
          <Liste
            libelle="Zone"
            valeur={zone}
            onChange={setZone}
            options={[
              { valeur: '', libelle: 'À choisir' },
              ...codesDeZone.map((code) => ({ valeur: code, libelle: code })),
            ]}
          />
        ) : (
          <Champ
            libelle="Zone"
            value={zone}
            onChange={(e) => setZone(e.target.value)}
            placeholder="cotonou"
            aide="Aucune zone dessinée : le code est libre, et il devra correspondre à une zone pour servir."
          />
        )}
        <Liste
          libelle="Nature"
          valeur={nature}
          onChange={setNature}
          options={[
            { valeur: '', libelle: 'À choisir' },
            { valeur: TOUT, libelle: 'Les deux natures' },
            { valeur: 'colis', libelle: 'Colis' },
            { valeur: 'course', libelle: 'Course de personnes' },
          ]}
        />
        <Liste
          libelle="Véhicule"
          valeur={vehicule}
          onChange={setVehicule}
          options={[
            { valeur: '', libelle: 'À choisir' },
            { valeur: TOUT, libelle: 'Tous les véhicules' },
            ...VEHICULES.map((v) => ({ valeur: v.code, libelle: v.libelle })),
          ]}
        />

        <Champ
          libelle="Prise en charge (F CFA)"
          type="number"
          min={0}
          value={priseEnCharge}
          onChange={(e) => setPriseEnCharge(e.target.value)}
          aide="Le montant dû avant le premier mètre."
        />
        <Champ
          libelle="Par kilomètre (F CFA)"
          type="number"
          min={0}
          value={parKilometre}
          onChange={(e) => setParKilometre(e.target.value)}
        />
        <Champ
          libelle="Par minute (F CFA)"
          type="number"
          min={0}
          value={parMinute}
          onChange={(e) => setParMinute(e.target.value)}
          aide="Zéro si la durée ne se facture pas."
        />
        <Champ
          libelle="Plancher (F CFA)"
          type="number"
          min={0}
          value={plancher}
          onChange={(e) => setPlancher(e.target.value)}
          aide="Jamais moins que cela, quelle que soit la distance."
        />

        <Champ
          libelle="Part du livreur (%)"
          type="number"
          min={0}
          max={100}
          step={0.01}
          value={partDuLivreur}
          onChange={(e) => setPartDuLivreur(e.target.value)}
          aide="Ce qu'il touche sur le prix payé."
        />
        <Champ
          libelle="Majoration (%)"
          type="number"
          min={0}
          step={0.01}
          value={majoration}
          onChange={(e) => setMajoration(e.target.value)}
          aide="La hausse appliquée au sous-total : 0 en temps normal, 20 pour +20 %."
        />
        <Champ
          libelle="Retenue d'annulation (%)"
          type="number"
          min={0}
          max={100}
          step={0.01}
          value={retenue}
          onChange={(e) => setRetenue(e.target.value)}
          aide="Part du devis retenue si le client annule APRÈS l'affectation d'un livreur. Elle lui revient en entier. Vide ou 0 : annulation gratuite."
        />
        <Champ
          libelle="Au kilo (F CFA)"
          type="number"
          min={0}
          value={auKilo}
          onChange={(e) => setAuKilo(e.target.value)}
          aide="Supplément par kilo au-delà du poids inclus. Vide pour une course."
        />
        <Champ
          libelle="Poids inclus (kg)"
          type="number"
          min={0}
          value={poidsInclusKg}
          onChange={(e) => setPoidsInclusKg(e.target.value)}
          aide="En dessous, aucun supplément."
        />

        <Champ
          libelle="Plage — début"
          type="time"
          value={debut}
          onChange={(e) => setDebut(e.target.value)}
          aide="Vide des deux côtés : la grille couvre la journée entière."
        />
        <Champ
          libelle="Plage — fin"
          type="time"
          value={fin}
          onChange={(e) => setFin(e.target.value)}
          aide="22:00 → 06:00 enjambe minuit, et c'est permis."
        />
      </div>

      <div className="flex flex-wrap items-center gap-3 px-5 pb-5">
        <Bouton disabled={occupe || !complet} onClick={creer}>
          {occupe
            ? 'Création…'
            : combinaisons.length > 1
              ? `Créer les ${combinaisons.length} grilles`
              : 'Créer la grille'}
        </Bouton>

        {/* LE NOMBRE EST DIT AVANT L'APPUI, PAS APRES. Dix grilles d'un seul geste
            est exactement ce qu'on veut — et exactement ce qu'on ne veut pas
            découvrir une fois fait. */}
        {combinaisons.length > 1 ? (
          <p className="text-xs text-encre-3">
            Une grille par couple nature × véhicule, chacune modifiable et closable
            séparément.
          </p>
        ) : null}

        {!complet ? (
          <p className="text-xs text-encre-3">
            Version, zone, nature, véhicule, plancher et part du livreur sont
            obligatoires.
          </p>
        ) : null}
      </div>

      {echec ? <Erreur message={echec} /> : null}
    </Carte>
  );
}

function Liste({
  libelle,
  valeur,
  onChange,
  options,
}: {
  libelle: string;
  valeur: string;
  onChange: (valeur: string) => void;
  options: { valeur: string; libelle: string }[];
}) {
  return (
    <label className="block">
      <span className="mb-1.5 block text-sm font-medium text-encre-2">{libelle}</span>
      <select
        value={valeur}
        onChange={(evenement) => onChange(evenement.target.value)}
        className="creux w-full rounded-xl border-0 px-4 py-3 text-sm text-encre outline-none"
      >
        {options.map((option) => (
          <option key={option.valeur} value={option.valeur}>
            {option.libelle}
          </option>
        ))}
      </select>
    </label>
  );
}

function texteDe(valeur: number | undefined): string {
  return valeur ? `${valeur}` : '';
}

function entier(texte: string): number {
  const valeur = Number.parseInt(texte, 10);
  return Number.isFinite(valeur) && valeur > 0 ? valeur : 0;
}

function nombreDecimal(texte: string): number {
  const valeur = Number.parseFloat(texte.replace(',', '.'));
  return Number.isFinite(valeur) && valeur > 0 ? valeur : 0;
}

/** « 22:00 » vers 1320. Vide vers null, que Pricing lit comme « toute la journée ». */
function minutesDe(heure: string): number | null {
  const parties = heure.split(':');
  if (parties.length !== 2) return null;

  const heures = Number.parseInt(parties[0], 10);
  const minutes = Number.parseInt(parties[1], 10);

  if (!Number.isFinite(heures) || !Number.isFinite(minutes)) return null;

  return heures * 60 + minutes;
}

/** 1320 vers « 22:00 ». Zéro et l'absence rendent une chaîne vide. */
function heureDe(minute: number | undefined): string {
  if (!minute) return '';

  const heures = Math.floor(minute / 60) % 24;
  const minutes = minute % 60;

  return `${`${heures}`.padStart(2, '0')}:${`${minutes}`.padStart(2, '0')}`;
}

/** 7000 vers « 70 % ». Zéro et l'absence rendent un tiret. */
function pourCent(pointsDeBase: number | undefined): string {
  if (!pointsDeBase) return '—';

  const valeur = pointsDeBase / 100;
  return `${Number.isInteger(valeur) ? valeur : valeur.toFixed(2)} %`;
}
