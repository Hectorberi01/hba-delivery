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
using Hba.Payment.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using CommonMoney = Hba.Contracts.Common.V1.Money;
using DomainLedgerDirection = Hba.Payment.Domain.Earnings.LedgerDirection;
using DomainLedgerKind = Hba.Payment.Domain.Earnings.LedgerEntryKind;
using DomainMethod = Hba.Payment.Domain.Payments.PaymentMethod;
using DomainPayoutStatus = Hba.Payment.Domain.Payouts.PayoutStatus;
using DomainStatus = Hba.Payment.Domain.Payments.PaymentStatus;
using ProtoMethod = Hba.Contracts.Payment.V1.PaymentMethod;
using ProtoPayoutStatus = Hba.Contracts.Payment.V1.PayoutStatus;
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
                ParseId(request.DeliveryId, "delivery_id"),
                request.PayerId,
                request.PayerPhone,
                request.Amount.Amount,
                ToDomain(request.Method),
                string.IsNullOrWhiteSpace(request.ReturnUrl) ? null : request.ReturnUrl),
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

            // LA COURSE EST VIDE POUR UN VERSEMENT, et c'est la chaine vide
            // qui le dit : un Guid.Empty rendu « 00000000-… » se lirait comme
            // une course.
            DeliveryId = e.DeliveryId?.ToString() ?? string.Empty,
            DeliveryReference = e.DeliveryReference,
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
        _ => LedgerEntryKind.Unspecified,
    };

    private static LedgerDirection ToProto(DomainLedgerDirection direction) => direction switch
    {
        DomainLedgerDirection.Credit => LedgerDirection.Credit,
        DomainLedgerDirection.Debit => LedgerDirection.Debit,
        _ => LedgerDirection.Unspecified,
    };

    private static PaymentIntent ToProto(PaymentIntentView view) => new()
    {
        Id = view.Id.ToString(),
        DeliveryId = view.DeliveryId.ToString(),
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
