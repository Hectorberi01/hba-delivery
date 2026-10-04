using Hba.BuildingBlocks.Security;
using Hba.Contracts.Payment.V1;

namespace Hba.Gateway.Endpoints.Web;

/// <summary>
/// La file des remboursements à faire, pour le back-office.
/// </summary>
///
/// <remarks>
/// POURQUOI CET ÉCRAN EXISTE. Le prix est encaissé AVANT la recherche d'un chauffeur
/// (point 35), et FedaPay n'a pas d'API de remboursement — tableau de bord marchand
/// uniquement, MTN Mobile Money seulement, vérifié le 30 septembre 2026. Quand
/// personne ne répond, le client a payé pour rien et quelqu'un doit lui rendre son
/// argent à la main.
///
/// RIEN ICI NE DÉPLACE D'ARGENT. L'argent part du tableau de bord du fournisseur ;
/// ces routes tiennent le cahier qui dit qu'il doit partir, puis qu'il est parti.
/// Confondre les deux ferait croire qu'un clic rembourse.
///
/// LECTURE OUVERTE AU BACK-OFFICE, DÉCISION RÉSERVÉE À admin ET finance. Le support
/// doit pouvoir dire à un client où en est son remboursement sans pouvoir le
/// trancher : c'est le service qui vérifie la seconde règle, pas cette passerelle.
/// </remarks>
public static class AdminRefundEndpoints
{
    public static IEndpointRouteBuilder MapAdminRefundEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/admin/v1/refunds-due")
            .RequireAuthorization(HbaPolicies.BackOffice);

        group.MapGet("/", async (
            PaymentService.PaymentServiceClient payment,
            CancellationToken cancellationToken,
            string? status = null,
            string? serviceType = null,
            int pageSize = 50,
            int offset = 0) =>
        {
            var reponse = await payment.ListRefundsDueAsync(
                new ListRefundsDueRequest
                {
                    // ABSENT VAUT « À TRAITER », et c'est le service qui le décide :
                    // le défaut protobuf d'un enum est zéro, qu'il traduit en PENDING.
                    Status = Etat(status),
                    ServiceType = Nature(serviceType),
                    PageSize = pageSize,
                    Offset = offset,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                items = reponse.Items.Select(Lisible),
                total = reponse.Total,
            });
        });

        // MARQUER RENDU. Le montant est SAISI, pas déduit : il peut différer du
        // recommandé — remboursement partiel négocié, arrondi du fournisseur — et
        // c'est ce qui est réellement parti qui doit figurer au dossier.
        group.MapPost("/{id}/settle", async (
            string id,
            SettleRefundDto body,
            PaymentService.PaymentServiceClient payment,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(body);

            var ligne = await payment.SettleRefundDueAsync(
                new SettleRefundDueRequest
                {
                    RefundDueId = id,
                    SettledAmount = body.SettledAmountXof,
                    Note = body.Note ?? string.Empty,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(ligne));
        });

        // CLASSER SANS SUITE. Le motif est obligatoire côté service : décider de
        // garder l'argent d'un client est une décision qui doit porter une raison.
        group.MapPost("/{id}/dismiss", async (
            string id,
            DismissRefundDto body,
            PaymentService.PaymentServiceClient payment,
            CancellationToken cancellationToken) =>
        {
            ArgumentNullException.ThrowIfNull(body);

            var ligne = await payment.DismissRefundDueAsync(
                new DismissRefundDueRequest { RefundDueId = id, Note = body.Note },
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(ligne));
        });

        return app;
    }

    private static object Lisible(RefundDue ligne) => new
    {
        id = ligne.Id,
        serviceType = Nommer(ligne.ServiceType),
        referenceId = ligne.ReferenceId,
        paymentIntentId = ligne.PaymentIntentId,
        payerId = ligne.PayerId,
        amountCollectedXof = ligne.AmountCollected,

        // NUL VEUT DIRE « À DÉTERMINER », PAS « RIEN DÛ », et la console doit
        // afficher les deux différemment. Un zéro se lirait « aucun remboursement
        // dû », ce qui est une décision que personne n'a prise : la politique
        // d'annulation n'est pas tranchée (point 3).
        recommendedRefundXof = ligne.HasRecommendedRefund ? ligne.RecommendedRefund : (long?)null,

        reason = ligne.Reason,
        status = Nommer(ligne.Status),
        settledAmountXof = ligne.HasSettledAmount ? ligne.SettledAmount : (long?)null,
        createdAt = ligne.CreatedAt?.ToDateTimeOffset(),
        resolvedAt = ligne.ResolvedAt?.ToDateTimeOffset(),
        resolvedBy = string.IsNullOrEmpty(ligne.ResolvedBy) ? null : ligne.ResolvedBy,
        note = string.IsNullOrEmpty(ligne.Note) ? null : ligne.Note,
    };

    private static string Nommer(RefundDueStatus status) => status switch
    {
        RefundDueStatus.Pending => "PENDING",
        RefundDueStatus.Settled => "SETTLED",
        RefundDueStatus.Dismissed => "DISMISSED",
        _ => "UNSPECIFIED",
    };

    private static string? Nommer(Hba.Contracts.Common.V1.ServiceType nature) => nature switch
    {
        Hba.Contracts.Common.V1.ServiceType.Delivery => "DELIVERY",
        Hba.Contracts.Common.V1.ServiceType.Ride => "RIDE",
        _ => null,
    };

    private static RefundDueStatus Etat(string? nom) => nom?.Trim().ToUpperInvariant() switch
    {
        "SETTLED" => RefundDueStatus.Settled,
        "DISMISSED" => RefundDueStatus.Dismissed,
        "PENDING" => RefundDueStatus.Pending,
        _ => RefundDueStatus.Unspecified,
    };

    private static Hba.Contracts.Common.V1.ServiceType Nature(string? nom) => nom?.Trim().ToUpperInvariant() switch
    {
        "DELIVERY" => Hba.Contracts.Common.V1.ServiceType.Delivery,
        "RIDE" => Hba.Contracts.Common.V1.ServiceType.Ride,
        _ => Hba.Contracts.Common.V1.ServiceType.Unspecified,
    };
}

public sealed record SettleRefundDto(long SettledAmountXof, string? Note = null);

public sealed record DismissRefundDto(string Note);
