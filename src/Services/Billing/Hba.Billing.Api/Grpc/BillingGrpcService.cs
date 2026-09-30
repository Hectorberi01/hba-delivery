using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Billing.Application.Common.Views;
using Hba.Billing.Application.Features.Accounts.Commands;
using Hba.Billing.Application.Features.Accounts.Queries;
using Hba.Billing.Domain.Exceptions;
using Hba.Contracts.Billing.V1;
using Microsoft.AspNetCore.Authorization;
using CommonMoney = Hba.Contracts.Common.V1.Money;
using DomainKind = Hba.Billing.Domain.Accounts.MovementKind;
using DomainMode = Hba.Billing.Domain.Accounts.SettlementMode;
using DomainStatus = Hba.Billing.Domain.Accounts.AccountStatus;
using ProtoKind = Hba.Contracts.Billing.V1.MovementKind;
using ProtoMode = Hba.Contracts.Billing.V1.SettlementMode;
using ProtoStatus = Hba.Contracts.Billing.V1.AccountStatus;

namespace Hba.Billing.Api.Grpc;

/// <summary>
/// Entrée synchrone du service Billing.
/// </summary>
///
/// <remarks>
/// CE SERVICE N'EST PAS EXPOSÉ AUX APPLICATIONS, ET CELA NE LE PROTÈGE PAS.
/// Il est appelé par Delivery, qui REPORTE LE JETON DE L'UTILISATEUR FINAL —
/// pas un jeton de service : le commentaire qui disait l'inverse ici était faux
/// et laissait croire que [Authorize] suffisait. C'est un porteur de jeton
/// commerçant ou partenaire qui arrive à cette porte. L'autorisation fine se
/// vérifie donc dans les gestionnaires, par BillingAccess, et jamais ici.
///
/// LE DÉBIT EST SYNCHRONE, ET C'EST TOUT L'INTÉRÊT DE CETTE PORTE. Un donneur
/// d'ordre au plafond doit l'apprendre AVANT que la course n'existe.
/// </remarks>
[Authorize]
public sealed class BillingGrpcService(IDispatcher dispatcher) : BillingService.BillingServiceBase
{
    public override async Task<Movement> Debit(DebitRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        EnsureXof(request.Amount?.Currency);

        var vue = await dispatcher.SendAsync(
            new DebitAccountCommand(
                Proprietaire(request.Owner).OwnerType,
                Proprietaire(request.Owner).OwnerId,
                request.Amount?.Amount ?? 0,
                request.Reference,
                request.IdempotencyKey),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(vue);
    }

    public override async Task<Movement> Credit(CreditRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        EnsureXof(request.Amount?.Currency);

        var vue = await dispatcher.SendAsync(
            new CreditAccountCommand(
                Proprietaire(request.Owner).OwnerType,
                Proprietaire(request.Owner).OwnerId,
                ToDomain(request.Kind),
                request.Amount?.Amount ?? 0,
                request.Reference,
                request.IdempotencyKey),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(vue);
    }

    public override async Task<Movement> ReverseDebit(ReverseDebitRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var proprietaire = Proprietaire(request.Owner);

        var vue = await dispatcher.SendAsync(
            new ReverseDebitCommand(
                proprietaire.OwnerType,
                proprietaire.OwnerId,
                request.DebitIdempotencyKey),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(vue);
    }

    public override async Task<Account> GetAccount(GetAccountRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var proprietaire = Proprietaire(request.Owner);

        // « QueryAsync » ET NON « SendAsync » : le repartiteur separe les deux.
        // une lecture cherche un IQueryHandler. Le compilateur l'a dit avant
        // moi — CS0411 sur une inference impossible.
        var vue = await dispatcher.QueryAsync(
            new GetAccountQuery(proprietaire.OwnerType, proprietaire.OwnerId),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(vue);
    }

    public override async Task<Account> OpenAccount(OpenAccountRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var proprietaire = Proprietaire(request.Owner);

        var vue = await dispatcher.SendAsync(
            new OpenAccountCommand(
                proprietaire.OwnerType,
                proprietaire.OwnerId,
                request.LowBalanceThreshold?.Amount ?? 0),
            context.CancellationToken).ConfigureAwait(false);

        return ToProto(vue);
    }

    /// <summary>
    /// LE COUPLE EST OBLIGATOIRE, ET SON ABSENCE EST UNE ERREUR D'APPELANT.
    /// Un « owner » vide ferait chercher un compte « : » — qui n'existe pas —
    /// et le service repondrait « compte introuvable », ce qui enverrait celui
    /// qui debogue vers la base plutot que vers son propre code.
    /// </summary>
    private static AccountOwner Proprietaire(AccountOwner? owner)
    {
        if (owner is null
            || string.IsNullOrWhiteSpace(owner.OwnerType)
            || string.IsNullOrWhiteSpace(owner.OwnerId))
        {
            throw new DomainException(
                "MISSING_ACCOUNT_OWNER",
                "Un appel a Billing nomme toujours le titulaire du compte.");
        }

        return owner;
    }

    private static void EnsureXof(string? currency)
    {
        if (!string.IsNullOrEmpty(currency)
            && !string.Equals(currency, Hba.Billing.Domain.ValueObjects.MoneyXof.CurrencyCode, StringComparison.Ordinal))
        {
            throw new DomainException(
                "UNSUPPORTED_CURRENCY",
                "Devise non prise en charge : seul le XOF est accepte.");
        }
    }

    private static CommonMoney Montant(long francs)
        => new() { Amount = francs, Currency = Hba.Billing.Domain.ValueObjects.MoneyXof.CurrencyCode };

    private static Account ToProto(BillingAccountView vue) => new()
    {
        Id = vue.Id.ToString(),
        Owner = new AccountOwner { OwnerType = vue.OwnerType, OwnerId = vue.OwnerId },
        Mode = vue.Mode switch
        {
            DomainMode.Prepaid => ProtoMode.Prepaid,
            DomainMode.Postpaid => ProtoMode.Postpaid,
            _ => ProtoMode.Unspecified,
        },
        Balance = Montant(vue.BalanceXof),
        CreditLimit = Montant(vue.CreditLimitXof),
        Available = Montant(vue.AvailableXof),
        LowBalanceThreshold = Montant(vue.LowBalanceThresholdXof),
        Status = vue.Status switch
        {
            DomainStatus.Active => ProtoStatus.Active,
            DomainStatus.Suspended => ProtoStatus.Suspended,
            _ => ProtoStatus.Unspecified,
        },
    };

    private static Movement ToProto(MovementView vue) => new()
    {
        Id = vue.Id.ToString(),
        AccountId = vue.AccountId.ToString(),
        Kind = ToProto(vue.Kind),
        Amount = Montant(vue.AmountXof),
        BalanceAfter = Montant(vue.BalanceAfterXof),
        Reference = vue.Reference,
        CreatedAt = Timestamp.FromDateTimeOffset(vue.CreatedAt),
    };

    private static ProtoKind ToProto(DomainKind kind) => kind switch
    {
        DomainKind.Topup => ProtoKind.Topup,
        DomainKind.Debit => ProtoKind.Debit,
        DomainKind.Refund => ProtoKind.Refund,
        DomainKind.InvoicePayment => ProtoKind.InvoicePayment,
        DomainKind.Adjustment => ProtoKind.Adjustment,
        _ => ProtoKind.Unspecified,
    };

    private static DomainKind ToDomain(ProtoKind kind) => kind switch
    {
        ProtoKind.Topup => DomainKind.Topup,
        ProtoKind.Refund => DomainKind.Refund,
        ProtoKind.InvoicePayment => DomainKind.InvoicePayment,
        ProtoKind.Adjustment => DomainKind.Adjustment,

        // « DEBIT » N'EST PAS ACCEPTE ICI, ET CE N'EST PAS UN OUBLI. Un debit
        // passe par Debit(), qui verifie le plafond ; le laisser entrer par
        // Credit() contournerait la seule regle de ce service.
        _ => throw new DomainException(
            BillingErrorCodes.InvalidAmount,
            "Ce type de mouvement ne peut pas etre credite. Un debit passe par Debit."),
    };
}
