using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Application.Time;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Grpc.Time;
using Hba.Contracts.Payment.V1;
using Hba.Payment.Application.Common.Views;
using Hba.Payment.Application.Features.Payments.Commands;
using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Application.Features.Earnings.Queries;
using Hba.Payment.Application.Features.Payouts.Commands;
using Hba.Payment.Application.Features.Payouts.Queries;
using Hba.Payment.Application.Features.Payments.Queries;
using Hba.Payment.Application.Features.Refunds;
using Hba.Payment.Application.Features.Refunds.Commands;
using Hba.Payment.Application.Features.Refunds.Queries;
using Hba.Payment.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using CommonMoney = Hba.Contracts.Common.V1.Money;
using DomainLedgerDirection = Hba.Payment.Domain.Earnings.LedgerDirection;
using DomainLedgerKind = Hba.Payment.Domain.Earnings.LedgerEntryKind;
using DomainMethod = Hba.Payment.Domain.Payments.PaymentMethod;
using DomainPayoutStatus = Hba.Payment.Domain.Payouts.PayoutStatus;
using DomainStatus = Hba.Payment.Domain.Payments.PaymentStatus;
using DomainRefundStatus = Hba.Payment.Domain.Refunds.RefundDueStatus;
using DomainServiceType = Hba.Payment.Domain.Payments.ServiceType;
using ProtoMethod = Hba.Contracts.Payment.V1.PaymentMethod;
using ProtoPayoutStatus = Hba.Contracts.Payment.V1.PayoutStatus;
using ProtoServiceType = Hba.Contracts.Common.V1.ServiceType;
using ProtoRefundDue = Hba.Contracts.Payment.V1.RefundDue;
using ProtoRefundStatus = Hba.Contracts.Payment.V1.RefundDueStatus;
using ProtoStatus = Hba.Contracts.Payment.V1.PaymentStatus;

namespace Hba.Payment.Api.Grpc;

/// <summary>
/// Entree synchrone du service Payment.
///
/// CE SERVICE N'EST PAS EXPOSE AUX APPLICATIONS. Il est appele par Delivery,
/// avec le jeton du client reporte : c'est pour cela que l'autorisation fine
/// est verifiee dans les handlers et non ici.
/// </summary>
[Authorize]
public sealed class PaymentGrpcService(IDispatcher dispatcher, ITimeCalendar calendrier)
    : PaymentService.PaymentServiceBase
{
    public override async Task<PaymentIntent> CreatePaymentIntent(
        CreatePaymentIntentRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (request.Amount is null || request.Amount.Amount <= 0)
        {
            throw new DomainException(
                PaymentErrorCodes.InvalidAmount,
                "Un paiement porte sur un montant strictement positif.");
        }

        EnsureXof(request.Amount.Currency);

        var view = await dispatcher.SendAsync(
            new CreatePaymentIntentCommand(
                request.IdempotencyKey,
                Nature(request.ServiceType),
                ParseId(request.ReferenceId, "reference_id"),
                request.PayerId,
                request.PayerPhone,
                request.Amount.Amount,
                ToDomain(request.Method)),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    /// <summary>
    /// Ouvre le paiement d'un pourboire (point 42).
    /// </summary>
    ///
    /// <remarks>
    /// LE BENEFICIAIRE N'EST PAS DANS LA REQUETE, et c'est tout le point : le
    /// handler le lit dans le grand livre, ou la remuneration de la mission a
    /// inscrit le chauffeur qui l'a faite.
    /// </remarks>
    public override async Task<PaymentIntent> CreateTipIntent(
        CreateTipIntentRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.SendAsync(
            new CreateTipIntentCommand(
                request.IdempotencyKey,
                Nature(request.ServiceType),
                ParseId(request.ReferenceId, "reference_id"),
                request.PayerPhone,
                request.AmountXof),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    public override async Task<PaymentIntent> GetPaymentIntent(
        GetPaymentIntentRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var view = await dispatcher.QueryAsync(
            new GetPaymentIntentQuery(ParseId(request.PaymentIntentId, "payment_intent_id")),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(view);
    }

    // RefundPayment n'est volontairement pas implemente : il renvoie
    // UNIMPLEMENTED, ce qui est explicite pour l'appelant. Rembourser engage
    // des regles qui ne sont pas tranchees — qui peut declencher, sous quel
    // delai, avec ou sans frais, et comment la part du livreur est reprise
    // quand la course a deja ete faite. Voir l'ADR 0017.

    /// <summary>
    /// Le mode non precise devient le mobile money : c'est le moyen de paiement
    /// courant a Cotonou, et la page hebergee du fournisseur propose de toute
    /// facon la carte a qui la veut.
    /// </summary>
    public override async Task<PaymentStats> GetPaymentStats(
        GetPaymentStatsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.QueryAsync(
            new GetPaymentStatsQuery(
                TimeWindowMapper.ToDomain(request.Window, calendrier),
                TimeWindowMapper.ToDomain(request.Granularity)),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new PaymentStats
        {
            Window = TimeWindowMapper.ToProto(vue.Window),
            CreatedCount = vue.CreatedByStatus.Sum(t => t.Count),
            CreatedAmountXof = vue.CreatedByStatus.Sum(t => t.AmountXof),
            CollectedCount = vue.CollectedCount,
            CollectedXof = vue.CollectedXof,
            AvgSecondsToPayment = vue.AverageSecondsToPayment,
            PaymentSamples = vue.PaymentSamples,
        };

        reponse.CreatedByStatus.AddRange(vue.CreatedByStatus.Select(t => new PaymentStatusCount
        {
            Status = ToProto(t.Status),
            Count = t.Count,
            AmountXof = t.AmountXof,
        }));

        reponse.CreatedByMethod.AddRange(vue.CreatedByMethod.Select(t => new PaymentMethodCount
        {
            Method = t.Method == DomainMethod.Card ? ProtoMethod.Card : ProtoMethod.MobileMoney,
            Count = t.Count,
            AmountXof = t.AmountXof,
        }));

        // La serie porte le MONTANT encaisse, pas le nombre de paiements :
        // c'est une courbe de recette, et deux petits paiements ne valent pas
        // un gros.
        reponse.CollectedSeries.AddRange(vue.CollectedSeries.Select(p => new Contracts.Common.V1.SeriesPoint
        {
            Key = p.Key,
            Value = p.AmountXof,
        }));

        return reponse;
    }

    private static DomainMethod ToDomain(ProtoMethod method) => method switch
    {
        ProtoMethod.Card => DomainMethod.Card,
        _ => DomainMethod.MobileMoney,
    };

    private static void EnsureXof(string? currency)
    {
        if (!string.IsNullOrEmpty(currency)
            && !string.Equals(currency, Hba.Payment.Domain.ValueObjects.MoneyXof.CurrencyCode, StringComparison.Ordinal))
        {
            throw new DomainException(
                "UNSUPPORTED_CURRENCY",
                $"Devise non prise en charge : {currency}. Seul le XOF est accepte.");
        }
    }

    private static Guid ParseId(string value, string field)
        => Guid.TryParse(value, out var id) && id != Guid.Empty
            ? id
            : throw new DomainException("INVALID_ID", $"Le champ {field} n'est pas un identifiant valide.");

    public override async Task<DriverStatement> GetDriverStatement(
        GetDriverStatementRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.QueryAsync(
            new GetDriverStatementQuery(
                string.IsNullOrWhiteSpace(request.DriverId) ? null : request.DriverId,
                request.Limit),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new DriverStatement
        {
            DriverId = vue.DriverId,
            EarnedXof = vue.EarnedXof,
            PaidOutXof = vue.PaidOutXof,
            DueXof = vue.DueXof,
            TotalEntries = vue.TotalEntries,
        };

        reponse.Entries.AddRange(vue.Entries.Select(e => new LedgerEntry
        {
            Id = e.Id.ToString(),
            Kind = ToProto(e.Kind),
            Direction = ToProto(e.Direction),
            AmountXof = e.AmountXof,

            // LA MISSION EST VIDE POUR UN VERSEMENT, et c'est la chaine vide
            // qui le dit : un Guid.Empty rendu « 00000000-… » se lirait comme
            // une mission.
            ReferenceId = e.ReferenceId?.ToString() ?? string.Empty,
            ReferenceLabel = e.ReferenceLabel,
            ServiceType = Nature(e.ServiceType),
            PayoutId = e.PayoutId?.ToString() ?? string.Empty,
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAt),
        }));

        return reponse;
    }

    public override async Task<PayoutRequest> RequestPayout(
        RequestPayoutRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.SendAsync(
            new RequestPayoutCommand(request.AmountXof),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(vue);
    }

    public override async Task<ListDriverPayoutsResponse> ListDriverPayouts(
        ListDriverPayoutsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vues = await dispatcher.QueryAsync(
            new ListDriverPayoutsQuery(
                string.IsNullOrWhiteSpace(request.DriverId) ? null : request.DriverId,
                request.Limit),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new ListDriverPayoutsResponse();
        reponse.Payouts.AddRange(vues.Select(ToProto));

        return reponse;
    }

    public override async Task<ListPayoutRequestsResponse> ListPayoutRequests(
        ListPayoutRequestsRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vues = await dispatcher.QueryAsync(
            new ListPayoutRequestsQuery(DuProto(request.Status), request.Limit),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new ListPayoutRequestsResponse();
        reponse.Payouts.AddRange(vues.Select(ToProto));

        return reponse;
    }

    public override async Task<PayoutRequest> ApprovePayout(
        ApprovePayoutRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.SendAsync(
            new ApprovePayoutCommand(ParseId(request.PayoutId, nameof(request.PayoutId))),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(vue);
    }

    public override async Task<PayoutRequest> RejectPayout(
        RejectPayoutRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.SendAsync(
            new RejectPayoutCommand(ParseId(request.PayoutId, nameof(request.PayoutId)), request.Reason),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(vue);
    }

    public override async Task<PayoutRequest> MarkPayoutPaid(
        MarkPayoutPaidRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.SendAsync(
            new MarkPayoutPaidCommand(
                ParseId(request.PayoutId, nameof(request.PayoutId)),
                request.PaymentReference),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(vue);
    }

    /// <summary>
    /// UNE DATE ABSENTE RESTE ABSENTE. Un Timestamp a l'epoque Unix se lirait
    /// comme une decision prise le 1er janvier 1970 ; le champ vide se lit
    /// comme « pas encore decide ».
    /// </summary>
    private static PayoutRequest ToProto(PayoutRequestView view)
    {
        var proto = new PayoutRequest
        {
            Id = view.Id.ToString(),
            DriverId = view.DriverId,
            AmountXof = view.AmountXof,
            Status = ToProto(view.Status),
            RequestedAt = Timestamp.FromDateTimeOffset(view.RequestedAt),
            RejectionReason = view.RejectionReason ?? string.Empty,
            PaymentReference = view.PaymentReference ?? string.Empty,
            DecidedBy = view.DecidedBy ?? string.Empty,
        };

        if (view.DecidedAt is { } decidee)
        {
            proto.DecidedAt = Timestamp.FromDateTimeOffset(decidee);
        }

        if (view.PaidAt is { } versee)
        {
            proto.PaidAt = Timestamp.FromDateTimeOffset(versee);
        }

        return proto;
    }

    /// <summary>
    /// UNSPECIFIED N'EST PAS UN ETAT, C'EST UNE ABSENCE DE FILTRE. Le proto3
    /// n'a pas de champ enum facultatif : zero est ce que porte un message ou
    /// personne n'a rien mis. Le traduire par « null » est donc la seule
    /// lecture honnete — et c'est exactement ce que la file de la finance
    /// attend par defaut.
    /// </summary>
    private static DomainPayoutStatus? DuProto(ProtoPayoutStatus status) => status switch
    {
        ProtoPayoutStatus.Requested => DomainPayoutStatus.Requested,
        ProtoPayoutStatus.Approved => DomainPayoutStatus.Approved,
        ProtoPayoutStatus.Paid => DomainPayoutStatus.Paid,
        ProtoPayoutStatus.Rejected => DomainPayoutStatus.Rejected,
        _ => null,
    };

    private static ProtoPayoutStatus ToProto(DomainPayoutStatus status) => status switch
    {
        DomainPayoutStatus.Requested => ProtoPayoutStatus.Requested,
        DomainPayoutStatus.Approved => ProtoPayoutStatus.Approved,
        DomainPayoutStatus.Paid => ProtoPayoutStatus.Paid,
        DomainPayoutStatus.Rejected => ProtoPayoutStatus.Rejected,
        _ => ProtoPayoutStatus.Unspecified,
    };

    private static LedgerEntryKind ToProto(DomainLedgerKind kind) => kind switch
    {
        DomainLedgerKind.DeliveryEarning => LedgerEntryKind.DeliveryEarning,
        DomainLedgerKind.Payout => LedgerEntryKind.Payout,
        DomainLedgerKind.Compensation => LedgerEntryKind.Compensation,
        DomainLedgerKind.Tip => LedgerEntryKind.Tip,
        _ => LedgerEntryKind.Unspecified,
    };

    private static LedgerDirection ToProto(DomainLedgerDirection direction) => direction switch
    {
        DomainLedgerDirection.Credit => LedgerDirection.Credit,
        DomainLedgerDirection.Debit => LedgerDirection.Debit,
        _ => LedgerDirection.Unspecified,
    };

    // ---- Remboursements a faire ---------------------------------------------
    //
    // CES TROIS APPELS NE DEPLACENT PAS D'ARGENT, et c'est a garder en tete en
    // lisant ce qui suit : FedaPay n'expose pas de remboursement par API. L'argent
    // part du tableau de bord marchand, par un geste humain ; ici on tient le
    // cahier qui dit qu'il doit partir, puis qu'il est parti.

    public override async Task<ListRefundsDueResponse> ListRefundsDue(
        ListRefundsDueRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var page = await dispatcher.QueryAsync(
            new ListRefundsDueQuery(
                // UN STATUT NON PRECISE VAUT « A TRAITER », pas « tous ». C'est la
                // question de la finance en ouvrant l'ecran, et le defaut protobuf
                // d'un enum est justement zero.
                EtatDemande(request.Status),
                NatureDemandee(request.ServiceType),
                request.PageSize,
                request.Offset),
            context.CancellationToken).ConfigureAwait(false);

        var reponse = new ListRefundsDueResponse { Total = page.Total };
        reponse.Items.AddRange(page.Items.Select(ToProto));

        return reponse;
    }

    public override async Task<ProtoRefundDue> SettleRefundDue(
        SettleRefundDueRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.SendAsync(
            new SettleRefundDueCommand(
                ParseId(request.RefundDueId, "refund_due_id"),
                request.SettledAmount,
                string.IsNullOrWhiteSpace(request.Note) ? null : request.Note),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(vue);
    }

    public override async Task<ProtoRefundDue> DismissRefundDue(
        DismissRefundDueRequest request,
        ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var vue = await dispatcher.SendAsync(
            new DismissRefundDueCommand(
                ParseId(request.RefundDueId, "refund_due_id"),
                request.Note),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(vue);
    }

    /// <summary>
    /// L'etat demande. NON PRECISE VAUT « A TRAITER » : voir ListRefundsDue.
    /// </summary>
    private static DomainRefundStatus? EtatDemande(ProtoRefundStatus status) => status switch
    {
        ProtoRefundStatus.Pending => DomainRefundStatus.Pending,
        ProtoRefundStatus.Settled => DomainRefundStatus.Settled,
        ProtoRefundStatus.Dismissed => DomainRefundStatus.Dismissed,
        _ => DomainRefundStatus.Pending,
    };

    /// <summary>
    /// La nature demandee. NON PRECISEE VAUT « LES DEUX », et c'est l'inverse de
    /// l'exigence qui vaut a l'ecriture : un filtre absent ne filtre pas, alors
    /// qu'une mission sans nature serait ambigue.
    /// </summary>
    private static DomainServiceType? NatureDemandee(ProtoServiceType nature) => nature switch
    {
        ProtoServiceType.Delivery => DomainServiceType.Delivery,
        ProtoServiceType.Ride => DomainServiceType.Ride,
        _ => null,
    };

    private static ProtoRefundDue ToProto(RefundDueView vue)
    {
        var ligne = new ProtoRefundDue
        {
            Id = vue.Id.ToString(),
            ServiceType = Nature(vue.ServiceType),
            ReferenceId = vue.ReferenceId.ToString(),
            PaymentIntentId = vue.PaymentIntentId.ToString(),
            PayerId = vue.PayerId,
            AmountCollected = vue.AmountCollected,
            Reason = vue.Reason,
            Status = Etat(vue.Status),
            CreatedAt = Timestamp.FromDateTimeOffset(vue.CreatedAt),
            ResolvedBy = vue.ResolvedBy ?? string.Empty,
            Note = vue.Note ?? string.Empty,
        };
        if (vue.RecommendedRefund is not null)
        {
            ligne.RecommendedRefund = vue.RecommendedRefund.Value;
        }

        if (vue.SettledAmount is not null)
        {
            ligne.SettledAmount = vue.SettledAmount.Value;
        }

        if (vue.ResolvedAt is not null)
        {
            ligne.ResolvedAt = Timestamp.FromDateTimeOffset(vue.ResolvedAt.Value);
        }

        return ligne;
    }

    private static ProtoRefundStatus Etat(DomainRefundStatus status) => status switch
    {
        DomainRefundStatus.Pending => ProtoRefundStatus.Pending,
        DomainRefundStatus.Settled => ProtoRefundStatus.Settled,
        DomainRefundStatus.Dismissed => ProtoRefundStatus.Dismissed,
        _ => ProtoRefundStatus.Unspecified,
    };

    /// <summary>
    /// La nature de la mission, exigee et non devinee.
    /// </summary>
    ///
    /// <remarks>
    /// UN APPEL SANS NATURE EST REFUSE, PAS INTERPRETE COMME UNE LIVRAISON. Le
    /// defaut protobuf d'un enum est zero : traiter zero comme « livraison » ferait
    /// qu'un appelant distrait paie la livraison qui porte l'identifiant d'une
    /// course. Mieux vaut un argument invalide tout de suite.
    /// </remarks>
    private static DomainServiceType Nature(ProtoServiceType nature) => nature switch
    {
        ProtoServiceType.Delivery => DomainServiceType.Delivery,
        ProtoServiceType.Ride => DomainServiceType.Ride,
        _ => throw new DomainException(
            "VALIDATION_FAILED",
            "Le champ service_type est obligatoire : livraison ou course."),
    };

    private static ProtoServiceType Nature(DomainServiceType nature) => nature switch
    {
        DomainServiceType.Delivery => ProtoServiceType.Delivery,
        DomainServiceType.Ride => ProtoServiceType.Ride,
        _ => ProtoServiceType.Unspecified,
    };

    private static PaymentIntent ToProto(PaymentIntentView view) => new()
    {
        Id = view.Id.ToString(),
        ReferenceId = view.ReferenceId.ToString(),
        ServiceType = Nature(view.ServiceType),
        PayerId = view.PayerId,
        Amount = new CommonMoney
        {
            Amount = view.Amount,
            Currency = Hba.Payment.Domain.ValueObjects.MoneyXof.CurrencyCode,
        },
        Status = ToProto(view.Status),
        Method = view.Method == DomainMethod.Card ? ProtoMethod.Card : ProtoMethod.MobileMoney,
        ProviderReference = view.ProviderReference,
        RedirectUrl = view.RedirectUrl,
        CreatedAt = Timestamp.FromDateTimeOffset(view.CreatedAt),
        SucceededAt = view.SucceededAt is null ? null : Timestamp.FromDateTimeOffset(view.SucceededAt.Value),
        ExpiresAt = Timestamp.FromDateTimeOffset(view.ExpiresAt),
    };

    private static ProtoStatus ToProto(DomainStatus status) => status switch
    {
        DomainStatus.Pending => ProtoStatus.Pending,
        DomainStatus.Succeeded => ProtoStatus.Succeeded,
        DomainStatus.Failed => ProtoStatus.Failed,
        DomainStatus.Expired => ProtoStatus.Expired,
        DomainStatus.Refunded => ProtoStatus.Refunded,
        DomainStatus.PartiallyRefunded => ProtoStatus.PartiallyRefunded,
        _ => ProtoStatus.Unspecified,
    };
}
