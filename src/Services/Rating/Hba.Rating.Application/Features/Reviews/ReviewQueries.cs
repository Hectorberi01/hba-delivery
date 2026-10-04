using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Rating.Application.Common;

namespace Hba.Rating.Application.Features.Reviews;

/// <summary>Ce que ce client peut encore noter.</summary>
public sealed record ListPendingReviewsQuery(string? CustomerId, int Limit)
    : IQuery<IReadOnlyList<ReviewView>>;

public sealed class ListPendingReviewsHandler(IReviewRepository reviews, ICallerContext caller)
    : IQueryHandler<ListPendingReviewsQuery, IReadOnlyList<ReviewView>>
{
    /// <summary>Plafond de sortie, pour que « limit=0 » ne rende pas tout.</summary>
    private const int ParDefaut = 10;

    private const int Maximum = 50;

    public async Task<IReadOnlyList<ReviewView>> HandleAsync(
        ListPendingReviewsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // UN CLIENT NE LIT QUE LES SIENNES, ET LE SERVICE LE FORCE. Un
        // identifiant dans la requete ne dit pas qui commande : sans cette
        // ligne, n'importe quel porteur de jeton listerait les missions d'un
        // autre, avec le livreur qui les a portees.
        var customerId = caller.IsInRole(HbaRoles.Customer)
            ? caller.SubjectId
            : query.CustomerId;

        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new ForbiddenException("Aucun client designe.");
        }

        var limite = query.Limit <= 0 ? ParDefaut : Math.Min(query.Limit, Maximum);

        var trouvees = await reviews
            .FindPendingForCustomerAsync(customerId, limite, cancellationToken)
            .ConfigureAwait(false);

        return [.. trouvees.Select(ReviewView.From)];
    }
}

/// <summary>La note moyenne d'un livreur.</summary>
///
/// <remarks>
/// L'IDENTIFIANT EST FACULTATIF PARCE QU'UN LIVREUR N'A PAS A LE DONNER. Quand
/// c'est lui qui demande, le service prend celui du jeton et ignore la requete :
/// l'autorisation se verifie ici, pas a la passerelle (referentiel des acteurs).
/// </remarks>
public sealed record GetDriverRatingQuery(string? DriverId) : IQuery<DriverRatingView>;

public sealed class GetDriverRatingHandler(IReviewRepository reviews, ICallerContext caller)
    : IQueryHandler<GetDriverRatingQuery, DriverRatingView>
{
    public async Task<DriverRatingView> HandleAsync(
        GetDriverRatingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // MEME REGLE QUE POUR LA LISTE, ET POUR LA MEME RAISON. Un livreur lit
        // SA moyenne ; le reste du systeme — back-office, et demain peut-etre
        // l'application cliente si le point 41 le tranche — nomme qui il
        // regarde.
        var brut = caller.IsInRole(HbaRoles.Driver) ? caller.DriverId : query.DriverId;

        if (!Guid.TryParse(brut, out var driverId))
        {
            throw new ForbiddenException("Aucun livreur designe.");
        }

        var (moyenne, nombre) = await reviews
            .ResumerPourDriverAsync(driverId, cancellationToken)
            .ConfigureAwait(false);

        return DriverRatingView.From(driverId, moyenne, nombre);
    }
}

/// <summary>Les avis recus par un livreur, un par un.</summary>
///
/// <remarks>
/// UNE MOYENNE SANS DETAIL EST UNE SANCTION SANS MOTIF. Le point 41 pose que le
/// livreur doit pouvoir voir ce qu'on lui reproche ; cette lecture est ce qui le
/// rend possible.
/// </remarks>
public sealed record ListDriverReviewsQuery(string? DriverId, int Limit)
    : IQuery<IReadOnlyList<ReviewView>>;

public sealed class ListDriverReviewsHandler(IReviewRepository reviews, ICallerContext caller)
    : IQueryHandler<ListDriverReviewsQuery, IReadOnlyList<ReviewView>>
{
    private const int ParDefaut = 20;

    private const int Maximum = 100;

    public async Task<IReadOnlyList<ReviewView>> HandleAsync(
        ListDriverReviewsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // UN LIVREUR NE LIT QUE SES PROPRES AVIS. Le back-office, lui, peut
        // nommer quelqu'un : c'est lui qui traite les reclamations.
        var brut = caller.IsInRole(HbaRoles.Driver) ? caller.DriverId : query.DriverId;

        if (!Guid.TryParse(brut, out var driverId))
        {
            throw new ForbiddenException("Aucun livreur designe.");
        }

        var limite = query.Limit <= 0 ? ParDefaut : Math.Min(query.Limit, Maximum);

        var trouves = await reviews
            .FindSubmittedForDriverAsync(driverId, limite, cancellationToken)
            .ConfigureAwait(false);

        return [.. trouves.Select(ReviewView.From)];
    }
}
