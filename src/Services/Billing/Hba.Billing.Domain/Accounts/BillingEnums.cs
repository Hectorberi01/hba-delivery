namespace Hba.Billing.Domain.Accounts;

/// <summary>
/// Le mode de règlement d'un compte. C'EST LE SEUL PARAMÈTRE qui distingue les
/// deux façons de payer : la règle de débit, elle, ne change pas.
/// </summary>
public enum SettlementMode
{
    /// <summary>Le titulaire recharge à l'avance ; le plafond vaut zéro.</summary>
    Prepaid = 1,

    /// <summary>
    /// Le solde descend sous zéro dans la limite d'un plafond accordé par
    /// `finance`, et ce solde négatif EST l'encours de la période.
    /// </summary>
    Postpaid = 2,
}

public enum AccountStatus
{
    Active = 1,

    /// <summary>
    /// Plus aucun débit n'est accepté. LES CRÉDITS RESTENT POSSIBLES : un
    /// compte suspendu pour impayé doit pouvoir être réglé, sans quoi la
    /// suspension serait définitive.
    /// </summary>
    Suspended = 2,
}

/// <summary>
/// Nature d'un mouvement. Le signe du montant en découle, et le domaine le
/// vérifie : un `topup` négatif ou un `debit` positif est un bug, pas une
/// écriture exotique.
/// </summary>
public enum MovementKind
{
    /// <summary>Recharge du titulaire, en prépayé. Crédit.</summary>
    Topup = 1,

    /// <summary>Une course débitée. Débit.</summary>
    Debit = 2,

    /// <summary>Course annulée ou débit orphelin rendu. Crédit.</summary>
    Refund = 3,

    /// <summary>Règlement d'une facture, en postpayé. Crédit.</summary>
    InvoicePayment = 4,

    /// <summary>
    /// Correction manuelle, dans un sens ou dans l'autre.
    /// </summary>
    ///
    /// <remarks>
    /// UN MOUVEMENT NE SE MODIFIE NI NE S'EFFACE. Une erreur se corrige par un
    /// ajustement de sens inverse, qui laisse les deux traces. C'est ce qui
    /// rend un compte vérifiable : la somme des mouvements doit refaire le
    /// solde, et une ligne retouchée romprait cette vérification en silence.
    /// </remarks>
    Adjustment = 5,
}
