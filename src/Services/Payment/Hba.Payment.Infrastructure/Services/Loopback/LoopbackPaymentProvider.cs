using System.Collections.Concurrent;
using System.Threading.Channels;
using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Domain.Payments;
using Hba.Payment.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.Payment.Infrastructure.Services.Loopback;

public sealed class LoopbackOptions
{
    public const string SectionName = "Loopback";

    /// <summary>Delai avant que la transaction se denoue, comme le ferait un payeur.</summary>
    public int DelaySeconds { get; set; } = 3;

    /// <summary>« succeeded », « failed » ou « pending ».</summary>
    public string Outcome { get; set; } = "succeeded";
}

/// <summary>
/// Fournisseur de paiement FACTICE, RESERVE AU DEVELOPPEMENT.
///
/// Il existe parce que le bac a sable FedaPay refuse toute transaction
/// « momo_test » sur un compte non verifie : sans lui, aucune course ne peut
/// atteindre PAID, donc ni le dispatch ni l'affectation d'un livreur ne
/// peuvent etre exerces. Le meme choix est deja fait ailleurs dans ce depot
/// pour le code OTP fixe et l'operateur SMS « log » — et, comme eux, il est
/// refuse hors Development, dans AddPaymentInfrastructure.
///
/// IL NE COURT-CIRCUITE PAS LA MACHINE A ETATS. Ouvrir un paiement met la
/// reference en file ; un service d'arriere-plan la denoue quelques secondes
/// plus tard en passant par ApplyProviderOutcome, exactement comme le ferait
/// une notification du fournisseur. Tout ce qui suit — agregat, Outbox, Kafka,
/// Delivery — est le chemin reel.
/// </summary>
public sealed class LoopbackPaymentProvider(
    IOptions<LoopbackOptions> options,
    ILogger<LoopbackPaymentProvider> logger) : IPaymentProvider
{
    private readonly ConcurrentDictionary<string, byte> _connues = new(StringComparer.Ordinal);
    private readonly Channel<string> _aDenouer = Channel.CreateUnbounded<string>();
    private readonly LoopbackOptions _options = options.Value;

    public string Name => "loopback";

    internal ChannelReader<string> ADenouer => _aDenouer.Reader;

    internal TimeSpan Delai => TimeSpan.FromSeconds(Math.Max(_options.DelaySeconds, 0));

    public Task<ProviderCheckout> OpenCheckoutAsync(
        Guid paymentIntentId,
        Guid deliveryId,
        MoneyXof amount,
        string payerPhone,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        var reference = $"LOOPBACK-{paymentIntentId:N}"[..18].ToUpperInvariant();
        _connues[reference] = 0;

        logger.LogWarning(
            "PAIEMENT FACTICE {Reference} pour la livraison {DeliveryId} : {Amount} XOF, denouement « {Outcome} » "
            + "dans {Delai} s. Aucun argent ne circule.",
            reference,
            deliveryId,
            amount.Amount,
            _options.Outcome,
            Delai.TotalSeconds);

        // La file est non bornee et le lecteur est un service d'arriere-plan :
        // l'ecriture aboutit toujours et ne bloque pas l'appel en cours.
        _aDenouer.Writer.TryWrite(reference);

        // Pas de page a ouvrir : il n'y a pas de fournisseur. On renvoie
        // l'adresse de retour quand l'appelant en fournit une, pour que
        // l'application ait quelque chose de valide a lancer.
        return Task.FromResult(new ProviderCheckout(
            reference,
            returnUrl ?? "https://loopback.hba.invalid/paiement-factice"));
    }

    public Task<ProviderPayment> GetPaymentAsync(string providerReference, CancellationToken cancellationToken)
    {
        if (!_connues.ContainsKey(providerReference))
        {
            // Une reference que ce processus n'a pas emise : le conteneur a
            // redemarre entre l'ouverture et le denouement. On ne se prononce
            // pas plutot que de conclure a tort.
            return Task.FromResult(new ProviderPayment(
                PaymentStatus.Pending,
                "loopback:inconnue",
                null));
        }

        return Task.FromResult(_options.Outcome.ToUpperInvariant() switch
        {
            "FAILED" => new ProviderPayment(PaymentStatus.Failed, "loopback:failed", "Echec simule."),
            "PENDING" => new ProviderPayment(PaymentStatus.Pending, "loopback:pending", null),
            _ => new ProviderPayment(PaymentStatus.Succeeded, "loopback:succeeded", null),
        });
    }
}
