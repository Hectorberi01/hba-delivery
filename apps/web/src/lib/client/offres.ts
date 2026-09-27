'use client';

import { envoyer } from '@/lib/client/ressource';

/**
 * Motifs de refus rendus par le service, en clair.
 *
 * LES QUATRE PREMIERS APPELLENT QUATRE GESTES DIFFERENTS, et c'est pour cela
 * qu'ils sont distincts plutot que fondus dans un « indisponible » : valider
 * un dossier, attendre la fin d'une course, rappeler quelqu'un, ou ne rien
 * faire parce que le telephone est eteint.
 */
export const REFUS_OFFRE: Record<string, string> = {
  DRIVER_NOT_FOUND: "Ce livreur n'existe pas.",
  DRIVER_NOT_VERIFIED: "Son dossier n'est pas validé : il n'a pas le droit de travailler.",
  DRIVER_NOT_AVAILABLE: "Il n'est plus libre — hors ligne, déjà sollicité, ou en course.",
  DRIVER_POSITION_STALE: "Son téléphone n'a pas donné signe de vie récemment.",
  DISPATCH_CLOSED: 'Cette course ne cherche plus de livreur.',
  ALREADY_OFFERED: 'Il porte déjà une offre en attente sur cette course.',
  MISSING_DRIVER_ID: 'Aucun livreur désigné.',
};

type ReponseOffre = {
  sent?: boolean;
  rejectionCode?: string | null;
};

/**
 * Pose une offre manuelle. Rend TOUJOURS un resultat, jamais une exception
 * pour un refus metier.
 *
 * LA DISTINCTION EST LE POINT DE CETTE FONCTION. « Ce livreur vient de
 * partir en course » n'est pas une panne : c'est la reponse, et elle demande
 * de choisir quelqu'un d'autre. La traiter comme une erreur reseau
 * l'enverrait dans le bandeau rouge des incidents, ou elle se lirait comme un
 * probleme a signaler.
 */
export async function proposerCourse(
  deliveryId: string,
  driverId: string,
  motif: string,
): Promise<{ ok: boolean; message: string }> {
  try {
    const reponse = await envoyer<ReponseOffre>(`admin/v1/deliveries/${deliveryId}/offer`, {
      driverId,
      reason: motif.trim() || null,
    });

    if (reponse.sent) {
      return { ok: true, message: '' };
    }

    const code = reponse.rejectionCode ?? '';

    return {
      ok: false,
      message: REFUS_OFFRE[code] ?? `Refusé par le service (${code || 'sans code'}).`,
    };
  } catch (cause) {
    return {
      ok: false,
      message: cause instanceof Error ? cause.message : "Échec de l'envoi de l'offre.",
    };
  }
}
