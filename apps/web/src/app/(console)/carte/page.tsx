'use client';

import { CarteDesLivreurs } from '@/composants/carte-livreurs';

/**
 * La carte des livreurs, en direct et en plein ecran.
 *
 * ELLE ETAIT EN BAS DE L'ANNUAIRE JUSQU'AU 2 OCTOBRE 2026, sous un bloc
 * expliquant deux notions absentes du domaine. Elle n'y avait rien a faire :
 * c'est le seul ecran de la console qu'on laisse OUVERT — on regarde une carte
 * de supervision, on ne la consulte pas — et elle vivait derriere un
 * defilement, a cote d'un texte qui parle d'autre chose.
 *
 * ELLE REMPLIT LA HAUTEUR, ELLE NE LA SUBIT PAS. Le composant portait un
 * « h-[420px] » fixe : sur un ecran de portable c'etait trop, sur un 27 pouces
 * il restait six cents pixels de gris sous la carte. La page lui donne
 * desormais tout ce qui reste sous l'en-tete, et le composant retombe sur ses
 * 420px quand personne ne lui donne de hauteur.
 *
 * ELLE N'EST PAS DANS UNE CARTE BLANCHE A MARGES. Une carte posee sur un fond
 * gris cadre un contenu qu'on lit ; celle-ci se regarde, et chaque pixel qu'un
 * cadre lui prend est une rue de moins. Elle garde sa bordure et son rayon pour
 * rester une surface de la console, rien de plus.
 *
 * LE COMPOSANT N'A PAS CHANGE POUR LE RESTE : il porte deja sa propre legende,
 * son compteur de livreurs localises, son sondage toutes les quinze secondes et
 * l'explication de pourquoi cette console reste au sondage quand les
 * applications mobiles sont passees au temps reel.
 */
export default function PageCarte() {
  return (
    // LA PAGE OCCUPE LA HAUTEUR DU CADRE, ET « min-h-0 » EST CE QUI LE PERMET :
    // sans lui, un enfant en defilement impose sa hauteur naturelle a la grille
    // et la colonne deborde au lieu de se partager.
    <div className="flex min-h-0 flex-1 flex-col gap-4">
      <div>
        <h1 className="text-lg font-semibold text-encre">Carte des livreurs</h1>
        <p className="text-sm text-encre-3">
          Qui est où, en ce moment. Seules les positions reçues récemment s&apos;affichent.
        </p>
      </div>

      <div className="relief min-h-0 flex-1 rounded-[var(--radius-carte)] p-4">
        <CarteDesLivreurs />
      </div>
    </div>
  );
}
