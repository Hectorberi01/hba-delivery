import type { TonStatut } from '@/lib/statuts';

/**
 * Les quatre etats d'une demande de versement, en clair.
 *
 * « APPROUVEE » N'EST PAS UNE FIN, et le libelle le dit : l'argent n'est pas
 * parti. C'est la confusion que cet ecran doit empecher — une demande
 * approuvee et oubliee laisse un livreur a attendre un virement que personne
 * n'a fait, sans que rien a l'ecran ne clignote.
 */
export type DefinitionVersement = { libelle: string; ton: TonStatut; explication: string };

export const VERSEMENTS: Record<string, DefinitionVersement> = {
  PAYOUT_STATUS_REQUESTED: {
    libelle: 'Demandée',
    ton: 'attention',
    explication: 'Le livreur attend une réponse. Rien n’est engagé.',
  },
  PAYOUT_STATUS_APPROVED: {
    libelle: 'Approuvée',
    ton: 'encours',
    explication: "Accord donné, virement pas encore consigné : l'argent n'est pas parti.",
  },
  PAYOUT_STATUS_PAID: {
    libelle: 'Versée',
    ton: 'bon',
    explication: 'Virement consigné, compte du livreur débité.',
  },
  PAYOUT_STATUS_REJECTED: {
    libelle: 'Refusée',
    ton: 'critique',
    explication: 'Refusée avec motif. Le livreur peut en ouvrir une autre.',
  },
};

export const VERSEMENT_INCONNU: DefinitionVersement = {
  libelle: 'État inconnu',
  ton: 'neutre',
  explication: "Le service a rendu un état que cet écran ne connaît pas.",
};

export function lireVersement(brut: unknown): DefinitionVersement {
  return (typeof brut === 'string' ? VERSEMENTS[brut] : undefined) ?? VERSEMENT_INCONNU;
}

/** Les mouvements du grand livre, nommes pour un humain. */
export function libelleMouvement(kind: unknown): string {
  if (kind === 'LEDGER_ENTRY_KIND_DELIVERY_EARNING') return 'Course livrée';
  if (kind === 'LEDGER_ENTRY_KIND_PAYOUT') return 'Versement';
  return 'Mouvement';
}

export function entre(direction: unknown): boolean {
  return direction === 'LEDGER_DIRECTION_CREDIT';
}
