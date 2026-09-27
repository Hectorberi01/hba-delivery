namespace Hba.Dispatch.Domain.Dispatching;

/// <summary>Etat d'une offre. Les valeurs suivent le contrat gRPC.</summary>
public enum OfferStatus
{
    Unspecified = 0,
    Pending = 1,
    Accepted = 2,
    Declined = 3,
    Expired = 4,

    /// <summary>Une autre offre de la meme vague a ete acceptee.</summary>
    Superseded = 5,
}

/// <summary>
/// Etat de la recherche pour une livraison. Il n'est pas dans le contrat : la
/// machine a etats publique est celle de la livraison, et Dispatch n'en est
/// qu'un moteur.
/// </summary>
public enum DispatchStatus
{
    Unspecified = 0,

    /// <summary>Des vagues sont en cours ou a venir.</summary>
    Searching = 1,

    Assigned = 2,

    /// <summary>Toutes les vagues ont echoue.</summary>
    Exhausted = 3,

    /// <summary>La course a ete annulee ailleurs : plus aucune offre ne part.</summary>
    Cancelled = 4,
}
