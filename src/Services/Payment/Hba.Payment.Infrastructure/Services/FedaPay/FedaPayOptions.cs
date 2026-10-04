namespace Hba.Payment.Infrastructure.Services.FedaPay;

/// <summary>
/// Reglages de l'agregateur FedaPay.
/// </summary>
public sealed class FedaPayOptions
{
    public const string SectionName = "FedaPay";
    public string Environment { get; set; } = "sandbox";
    public string SecretKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string? CallbackUrl { get; set; }
    public string CustomerCountry { get; set; } = "bj";
    public int WebhookToleranceSeconds { get; set; } = 300;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);
    public bool IsLive => string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase);
    public Uri BaseAddress => new(IsLive
        ? "https://api.fedapay.com/v1/"
        : "https://sandbox-api.fedapay.com/v1/");
}
