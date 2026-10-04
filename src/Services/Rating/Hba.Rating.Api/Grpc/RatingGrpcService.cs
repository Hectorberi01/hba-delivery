using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Contracts.Rating.V1;
using Hba.Rating.Application.Common;
using Hba.Rating.Application.Features.Reviews;
using Microsoft.AspNetCore.Authorization;
using DomainMissionType = Hba.Rating.Domain.Reviews.MissionType;
using ProtoReview = Hba.Contracts.Rating.V1.Review;
using ProtoServiceType = Hba.Contracts.Common.V1.ServiceType;

namespace Hba.Rating.Api.Grpc;

/// <summary>
/// Entree synchrone du service Rating.
/// </summary>
///
/// <remarks>
/// ELLE NE DECIDE RIEN. Aucune regle d'autorisation, aucune moyenne, aucun
/// seuil : elle traduit un contrat en commande et une vue en contrat. Qui a le
/// droit de noter, et de lire quoi, est verifie dans les handlers — testables
/// sans serveur gRPC (ADR 0007).
/// </remarks>
[Authorize]
public sealed class RatingGrpcService(IDispatcher dispatcher) : Hba.Contracts.Rating.V1.RatingService.RatingServiceBase
{
    public override async Task<ListPendingReviewsResponse> ListPendingReviews(
        ListPendingReviewsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vues = await dispatcher.QueryAsync(
            new ListPendingReviewsQuery(Nullify(request.CustomerId), request.Limit),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new ListPendingReviewsResponse();
        reponse.Reviews.AddRange(vues.Select(ToProto));

        return reponse;
    }

    public override async Task<ProtoReview> SubmitReview(
        SubmitReviewRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.SendAsync(
            new SubmitReviewCommand(
                Nature(request.ServiceType),
                Identifiant(request.ReferenceId, "reference_id"),
                request.Stars,
                Nullify(request.Comment)),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(vue);
    }

    public override async Task<Hba.Contracts.Rating.V1.DriverRating> GetDriverRating(
        GetDriverRatingRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.QueryAsync(
            new GetDriverRatingQuery(Nullify(request.DriverId)),
            context.CancellationToken).ConfigureAwait(false);

        return new Hba.Contracts.Rating.V1.DriverRating
        {
            DriverId = vue.DriverId.ToString(),
            Average = vue.Average,
            Count = vue.Count,
            Enough = vue.Enough,
        };
    }

    public override async Task<ListDriverReviewsResponse> ListDriverReviews(
        ListDriverReviewsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vues = await dispatcher.QueryAsync(
            new ListDriverReviewsQuery(Nullify(request.DriverId), request.Limit),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new ListDriverReviewsResponse();
        reponse.Reviews.AddRange(vues.Select(ToProto));

        return reponse;
    }

    public override async Task<ProtoReview> OpenReview(
        OpenReviewRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.SendAsync(
            new OpenReviewCommand(
                Nature(request.ServiceType),
                Identifiant(request.ReferenceId, "reference_id"),
                request.CustomerId,
                Identifiant(request.DriverId, "driver_id"),
                request.CompletedAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(vue);
    }

    /// <summary>
    /// La nature de la mission, dans le mot du domaine.
    /// </summary>
    ///
    /// <remarks>
    /// NON PRECISEE EST UNE ERREUR, PAS UN DEFAUT. Supposer la livraison ferait
    /// noter un colis qui porte l'identifiant d'une course : les deux services
    /// tirent leurs identifiants separement, et rien ne les distingue sans ce
    /// champ.
    /// </remarks>
    private static DomainMissionType Nature(ProtoServiceType nature) => nature switch
    {
        ProtoServiceType.Delivery => DomainMissionType.Delivery,
        ProtoServiceType.Ride => DomainMissionType.Ride,
        _ => throw new DomainException(
            "MISSING_SERVICE_TYPE",
            "Precisez la nature de la mission : livraison ou course."),
    };

    private static ProtoServiceType Nature(DomainMissionType nature) => nature switch
    {
        DomainMissionType.Delivery => ProtoServiceType.Delivery,
        DomainMissionType.Ride => ProtoServiceType.Ride,
        _ => ProtoServiceType.Unspecified,
    };

    private static Guid Identifiant(string brut, string champ) =>
        Guid.TryParse(brut, out var id)
            ? id
            : throw new DomainException("INVALID_ID", $"{champ} n'est pas un identifiant valide.");

    private static string? Nullify(string? valeur) =>
        string.IsNullOrWhiteSpace(valeur) ? null : valeur;

    /// <summary>
    /// ZERO ETOILE VEUT DIRE « PAS ENCORE NOTEE », et c'est ecrit au contrat.
    /// Protobuf n'a pas d'entier nul ; le bareme commençant a une etoile, zero
    /// est libre et dit l'absence sans ambiguite.
    /// </summary>
    private static ProtoReview ToProto(ReviewView vue) => new()
    {
        Id = vue.Id.ToString(),
        ServiceType = Nature(vue.MissionType),
        ReferenceId = vue.ReferenceId.ToString(),
        CustomerId = vue.CustomerId,
        DriverId = vue.DriverId.ToString(),
        Stars = vue.Stars ?? 0,
        Comment = vue.Comment ?? string.Empty,
        MissionCompletedAt = Timestamp.FromDateTimeOffset(vue.MissionCompletedAt),
        SubmittedAt = vue.SubmittedAt is null ? null : Timestamp.FromDateTimeOffset(vue.SubmittedAt.Value),
    };
}
