using Hba.Ride.Domain.Rides;

namespace Hba.Ride.Application.Ports;

/// <summary>
/// Ce que Pricing rend quand on consomme un devis.
/// </summary>
///
/// <remarks>
/// RIDE NE CALCULE JAMAIS UN PRIX, ET N'EN ACCEPTE JAMAIS UN DE L'APPELANT. Le
/// client envoie un identifiant de devis, pas un montant : c'est la seule façon
/// d'empêcher qu'un téléphone modifié commande une course à dix francs. Le même
/// raisonnement vaut pour la distance — Pricing seul sait quel trajet il a
/// chiffré.
/// </remarks>
public sealed record QuoteSnapshot(
    string QuoteId,
    string TariffVersion,
    long Total,

    /// <summary>
    /// La part du chauffeur, telle que Pricing l'a calculée.
    ///
    /// ELLE VIENT DU DEVIS ET DE NULLE PART AILLEURS. Le chauffeur accepte sur ce
    /// montant ; le recalculer ici — même avec la bonne formule — donnerait deux
    /// chiffres pour une même course le jour où la grille change entre le devis et
    /// la commande.
    /// </summary>
    long DriverEarning,

    /// <summary>
    /// La retenue due au chauffeur si le passager annule APRÈS son affectation,
    /// figée par le devis au taux de la grille du jour (point 3, tranché le
    /// 2 octobre 2026). Zéro quand la grille n'en prévoit pas.
    ///
    /// MÊME RAISON QUE LA PART DU CHAUFFEUR CI-DESSUS : elle vient du devis et de
    /// nulle part ailleurs. Le passager la voit avant de confirmer son annulation,
    /// et la relire dans la grille à ce moment-là lui prélèverait autre chose que
    /// ce que l'écran lui a annoncé.
    /// </summary>
    long CancellationFee,

    int DistanceMeters,
    int DurationSeconds);

/// <summary>Accès au service Pricing.</summary>
public interface IPricingClient
{
    /// <summary>
    /// Consomme le devis et rend ce qu'il faut figer. Échoue s'il est expiré,
    /// déjà consommé, ou S'IL A ÉTÉ ÉTABLI POUR UN AUTRE TRAJET.
    /// </summary>
    ///
    /// <remarks>
    /// LE TRAJET EST PASSÉ À PRICING, COMME DANS DELIVERY, et pour la même raison
    /// apprise à ses dépens : tant qu'on ne lui donnait pas les deux points, un
    /// devis de trois cents mètres payait une course de vingt kilomètres. Le
    /// contrôle appartient à celui qui a chiffré, pas à celui qui facture.
    /// </remarks>
    Task<QuoteSnapshot> ConsumeQuoteAsync(
        string quoteId,
        Guid rideId,
        double pickupLatitude,
        double pickupLongitude,
        double destinationLatitude,
        double destinationLongitude,
        CancellationToken cancellationToken);
}

public interface IRideRepository
{
    Task<RideAggregate?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Les courses en cours d'un client. Vide s'il n'en a aucune.
    /// </summary>
    ///
    /// <remarks>
    /// UNE LISTE, PAS UNE COURSE : LE CLIENT PEUT EN AVOIR PLUSIEURS OUVERTES.
    /// Décidé par Hector le 1er octobre 2026. Le cas n'est pas théorique — on
    /// commande une course pour un proche en même temps que la sienne, ou deux
    /// véhicules pour un groupe trop grand pour un seul.
    ///
    /// CE QUE CELA ENLÈVE, ET QU'IL FAUT SAVOIR : plus rien ne distingue un
    /// double appui sur « commander » d'une seconde course voulue. La clé
    /// d'idempotence devient la seule protection, et elle doit donc être générée
    /// par écran de commande — pas une fois pour la session.
    ///
    /// L'ORDRE EST CELUI DE LA DEMANDE, LA PLUS RÉCENTE D'ABORD : c'est celle
    /// que l'application doit montrer en premier.
    /// </remarks>
    Task<IReadOnlyList<RideAggregate>> ListActiveForCustomerAsync(
        string customerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// La course en cours d'un chauffeur, s'il en a une.
    /// </summary>
    ///
    /// <remarks>
    /// ICI LE SINGULIER RESTE, ET CE N'EST PAS UN OUBLI : un chauffeur ne conduit
    /// qu'un véhicule. Ce que le client peut multiplier, le chauffeur ne le peut
    /// pas.
    /// </remarks>
    Task<RideAggregate?> FindActiveForDriverAsync(string driverId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<RideAggregate> Rides, int Total)> ListForCustomerAsync(
        string customerId,
        int pageSize,
        int offset,
        CancellationToken cancellationToken);

    /// <summary>
    /// Les courses qu'aucun paiement n'est venu confirmer avant
    /// <paramref name="limite"/>, les plus anciennes d'abord.
    /// </summary>
    ///
    /// <remarks>
    /// RIEN NE FERMAIT CES COURSES-LA JUSQU'AU 2 OCTOBRE 2026. Delivery balayait
    /// ses impayées depuis le 29 septembre (point 30) ; Ride, né deux jours plus
    /// tard, n'a jamais eu l'équivalent. Une course abandonnée au paiement restait
    /// en <c>Requested</c> indéfiniment, avec son devis consommé et son intention
    /// de paiement ouverte.
    ///
    /// « UNE INTENTION EST RATTACHÉE » EST LA GARDE, comme du côté des colis. Une
    /// course sans intention est une course dont le paiement n'a même pas pu
    /// s'ouvrir : l'abandonner sur le seul statut effacerait aussi celles que le
    /// fournisseur a refusé d'ouvrir dans la même seconde, avant que le
    /// rattachement n'ait eu lieu.
    ///
    /// LES PLUS ANCIENNES D'ABORD : après une panne, c'est le retard le plus vieux
    /// qu'il faut solder, et c'est ce qui rend le balayage progressif au lieu de
    /// tourner en rond sur les mêmes lignes.
    /// </remarks>
    Task<IReadOnlyList<RideAggregate>> ListUnpaidBeforeAsync(
        DateTimeOffset limite,
        int batchSize,
        CancellationToken cancellationToken);

    void Add(RideAggregate ride);
}

/// <summary>
/// Ce que Payment rend quand on ouvre une intention.
/// </summary>
///
/// <remarks>
/// L'ADRESSE EST REVENUE LE 3 OCTOBRE 2026, le jour même où elle avait disparu.
/// Entre les deux, la demande de paiement devait être poussée directement sur
/// le téléphone du passager ; le fournisseur refuse cette poussée sur notre
/// compte, et la page hébergée a repris sa place.
/// </remarks>
public sealed record PaymentIntentOpened(string PaymentIntentId, string RedirectUrl);

/// <summary>Accès au service Payment.</summary>
public interface IRidePaymentClient
{
    /// <summary>
    /// Ouvre le paiement d'une course et rend la page où envoyer le passager.
    /// </summary>
    ///
    /// <remarks>
    /// LE MONTANT EST CELUI DU DEVIS CONSOMMÉ, jamais un montant venu de
    /// l'appelant : Ride le tient de Pricing et le transmet tel quel. Payment ne
    /// recalcule aucun prix — il n'a ni la grille, ni la distance, ni le droit
    /// (ADR 0004).
    ///
    /// LA CLÉ D'IDEMPOTENCE EST CELLE DE LA COURSE, pas une nouvelle : un rejeu
    /// de la demande doit rendre la MÊME intention, sans quoi le passager se
    /// retrouverait avec deux transactions ouvertes pour une course.
    ///
    /// LE NUMÉRO PAYEUR PEUT ÊTRE VIDE, et c'est alors celui du jeton qui sert.
    /// Il ne fait que préremplir la page : le passager peut en saisir un autre,
    /// et c'est celui-là qui sera débité.
    /// </remarks>
    Task<PaymentIntentOpened> OpenAsync(
        string idempotencyKey,
        Guid rideId,
        string customerId,
        string payerPhone,
        long amount,
        CancellationToken cancellationToken);
}

/// <summary>
/// Accès au service Driver, pour le profil public d'un chauffeur affecté.
/// </summary>
///
/// <remarks>
/// JE DÉROGE ICI À MON PROPRE DOCUMENT D'ANALYSE, et autant le dire : §D de
/// mobilite-analyse-prealable.md écrit « Ride ne parle jamais à Driver ni à
/// Location : c'est Dispatch ». La règle visait à éviter un cycle, et il n'y en a
/// aucun — Driver n'appelle pas Ride. Delivery fait exactement cet appel, au même
/// moment et pour la même raison. La phrase était trop stricte ; le document est
/// corrigé.
///
/// L'ALTERNATIVE ÉTAIT PIRE : faire voyager le profil dans l'événement de Dispatch
/// obligerait Dispatch à lire une donnée dont il ne fait rien, et à la porter dans
/// un message que Driver consomme aussi.
///
/// TOUTE PANNE REND NULL PLUTÔT QUE DE LEVER. Une acceptation est déjà actée chez
/// Dispatch et chez Driver quand cet appel a lieu : échouer ici laisserait un
/// chauffeur réservé sur une course qui ne l'a pas enregistré.
/// </remarks>
public interface IRideDriverDirectory
{
    Task<AssignedDriver?> GetPublicProfileAsync(string driverId, CancellationToken cancellationToken);
}
