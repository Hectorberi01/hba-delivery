using Hba.Payment.Domain.Payments;

namespace Hba.Payment.Application.Common.Views;

/// <summary>
/// Intention telle qu'elle sort du service. Le montant est un entier de francs
/// CFA (ADR 0006).
/// </summary>
public sealed record PaymentIntentView(
    Guid Id,
    ServiceType ServiceType,
    Guid ReferenceId,
    string PayerId,
    long Amount,
    PaymentStatus Status,
    PaymentMethod Method,
    string ProviderReference,

    // Vide tant que le fournisseur n'a pas rendu d'adresse.
    string RedirectUrl,

    DateTimeOffset CreatedAt,
    DateTimeOffset? SucceededAt,
    DateTimeOffset ExpiresAt)
{
    public static PaymentIntentView From(PaymentIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);

        return new PaymentIntentView(
            intent.Id,
            intent.ServiceType,
            intent.ReferenceId,
            intent.PayerId,
            intent.Amount.Amount,
            intent.Status,
            intent.Method,
            intent.ProviderReference ?? string.Empty,
            intent.RedirectUrl ?? string.Empty,
            intent.CreatedAt,
            intent.SucceededAt,
            intent.ExpiresAt);
    }
}
