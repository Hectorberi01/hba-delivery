namespace Hba.Payment.Infrastructure.Services.FedaPay;

/// <summary>
/// Reglages de l'agregateur FedaPay.
///
/// AUCUNE VALEUR PAR DEFAUT UTILISABLE N'EST FOURNIE POUR LES SECRETS. Une cle
/// vide doit faire echouer le demarrage, pas produire un service qui accepte
/// des commandes et ne sait pas les encaisser : c'est la panne invisible, celle
/// qu'on decouvre le jour ou un client a paye.
/// </summary>
public sealed class FedaPayOptions
{
    public const string SectionName = "FedaPay";

    /// <summary>
    /// « sandbox » ou « live ». Le mot choisit l'adresse de l'API : les deux
    /// environnements ont des cles distinctes et des soldes distincts.
    /// </summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>Cle secrete du compte (sk_sandbox_… ou sk_live_…).</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Secret de l'endpoint webhook. DISTINCT DE LA CLE SECRETE : il est
    /// genere avec le webhook dans le tableau de bord, et c'est lui seul qui
    /// signe les notifications.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Adresse ou le payeur est renvoye apres la page de paiement. Elle ne sert
    /// qu'au confort : RIEN N'EST ENCAISSE SUR CE RETOUR, seul le webhook fait
    /// foi.
    /// </summary>
    public string? CallbackUrl { get; set; }

    /// <summary>
    /// Pays ou ce compte encaisse, en ISO 3166-1 alpha-2 minuscule. « bj » pour
    /// le Benin.
    /// </summary>
    ///
    /// <remarks>
    /// CE N'EST PLUS « LE PAYS QU'ON COLLE AU NUMERO », ET LA NUANCE VAUT UNE
    /// PANNE. Ce reglage etiquetait autrefois le numero du payeur quel qu'il
    /// soit : un client inscrit avec un +33 partait chez le fournisseur comme
    /// beninois, et son paiement echouait au debit sans motif lisible. Il sert
    /// desormais de FILTRE — le numero n'est transmis que si son indicatif
    /// correspond a ce pays — et non d'affirmation.
    /// </remarks>
    public string CustomerCountry { get; set; } = "bj";

    /// <summary>
    /// Tolerance d'horodatage des webhooks, en secondes. 300 est la valeur des
    /// bibliotheques officielles ; la reduire rejette les notifications
    /// retardees par une file d'attente, l'augmenter allonge la fenetre de
    /// rejeu d'une notification interceptee.
    /// </summary>
    public int WebhookToleranceSeconds { get; set; } = 300;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);

    public bool IsLive => string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Adresse de base de l'API, version comprise. Les deux hotes sont ceux des
    /// bibliotheques officielles du fournisseur.
    /// </summary>
    public Uri BaseAddress => new(IsLive
        ? "https://api.fedapay.com/v1/"
        : "https://sandbox-api.fedapay.com/v1/");
}
