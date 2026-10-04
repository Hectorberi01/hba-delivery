namespace Hba.Ride.Domain.Rides;

/// <summary>
/// Les états d'une course de personnes.
/// </summary>
///
/// <remarks>
/// QUATRE ÉTATS TERMINAUX, ET IL FAUT LES CONNAÎTRE : <see cref="Completed"/>,
/// <see cref="Cancelled"/>, <see cref="NoDriverAvailable"/> et
/// <see cref="PaymentFailed"/>. Aucune transition n'en sort. Le troisième a été oublié une fois dans Delivery, et l'écran du
/// client tournait indéfiniment sur une recherche déjà abandonnée — d'où sa
/// présence explicite ici.
///
/// LES NUMÉROS NE SE RÉUTILISENT NI NE SE RÉORDONNENT : ils sont persistés et
/// portés par le contrat.
/// </remarks>
public enum RideStatus
{
    Unspecified = 0,

    /// <summary>
    /// Le passager a demandé, et le paiement n'est pas encore confirmé.
    /// </summary>
    ///
    /// <remarks>
    /// IL N'Y A PAS D'ÉTAT « PendingPayment » DISTINCT, ET C'EST DÉLIBÉRÉ. Entre la
    /// demande et l'encaissement, une course ne fait rien d'autre qu'attendre son
    /// paiement : un second état porterait exactement le même comportement, et
    /// l'application aurait deux libellés pour un seul écran. Delivery, lui, part
    /// directement de PENDING_PAYMENT — c'est le même état sous un autre nom.
    /// </remarks>
    Requested = 1,

    /// <summary>Le dispatch cherche.</summary>
    SearchingDriver = 2,

    /// <summary>Un chauffeur a accepté.</summary>
    DriverAssigned = 3,

    /// <summary>Il roule vers le point de prise en charge.</summary>
    DriverArriving = 4,

    /// <summary>Il est sur place, il attend le passager.</summary>
    DriverArrived = 5,

    /// <summary>Le passager est à bord.</summary>
    InProgress = 6,

    Completed = 7,
    Cancelled = 8,

    /// <summary>Toutes les vagues ont échoué.</summary>
    NoDriverAvailable = 9,

    /// <summary>
    /// Le paiement a échoué ou expiré. Terminal.
    /// </summary>
    ///
    /// <remarks>
    /// SON ABSENCE A DÉJÀ COÛTÉ UN ÉCRAN BLOQUÉ dans l'application client, du côté
    /// des colis : sans cet état, une course dont le paiement échoue reste
    /// « demandée » pour toujours et le passager regarde un sablier qui ne tournera
    /// jamais. C'est aussi pourquoi il est TERMINAL : on ne réessaie pas sur la même
    /// course, on en redemande une — le devis, lui, est déjà consommé.
    /// </remarks>
    PaymentFailed = 10,
}
