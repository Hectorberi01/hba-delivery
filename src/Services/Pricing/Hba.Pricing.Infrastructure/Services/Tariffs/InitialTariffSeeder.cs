using Hba.Pricing.Domain.Tariffs;
using Hba.Pricing.Domain.ValueObjects;
using Hba.Pricing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hba.Pricing.Infrastructure.Services.Tariffs;

/// <summary>
/// Grille initiale, lue dans la configuration.
///
/// C'EST LA SOURCE PREVUE PAR L'AGREGAT : « LES MONTANTS NE SONT PAS DANS LE
/// REFERENTIEL. Ils sont donnes en configuration et administres ensuite par ops
/// et admin ; aucune valeur n'est codee en dur ici. » Rien n'est donc code en
/// dur ici non plus : sans section Tariff:Initial, aucune grille n'est creee et
/// le service repond NO_TARIFF, ce qui est explicite.
/// </summary>
public sealed class InitialTariffOptions
{
    public const string SectionName = "Tariff:Initial";

    public bool Enabled { get; set; }

    public string Version { get; set; } = string.Empty;

    public string ZoneCode { get; set; } = string.Empty;

    public VehicleType VehicleType { get; set; } = VehicleType.Motorcycle;

    /// <summary>
    /// La nature tarifee par la grille initiale.
    /// </summary>
    ///
    /// <remarks>
    /// LE DEFAUT EST LE COLIS, ET C'EST LE SEUL DEFAUT DE NATURE QUE CE DEPOT SE
    /// PERMET AILLEURS QU'A LA FRONTIERE gRPC. La raison est la meme : ce semis
    /// existait avant que la nature entre dans la cle, les fichiers de configuration
    /// deployes ne la nomment pas, et une grille semee sans nature serait refusee par
    /// l'agregat — donc aucune grille, donc NO_TARIFF sur tout.
    ///
    /// IL NE SEME QU'UNE SEULE GRILLE, DONC UNE SEULE NATURE. La grille de course se
    /// cree par la route d'administration : ce semis n'a jamais eu pour role de
    /// garnir un bareme, seulement de rendre la pile utilisable au premier demarrage.
    /// </remarks>
    // LE NOM EST ECRIT EN ENTIER, ET C'EST NECESSAIRE : ce fichier vit dans
    // ...Infrastructure.Services.Tariffs, donc « Tariffs.ServiceType » designerait CE
    // namespace-la, qui ne contient pas l'enum.
    public ServiceType ServiceType { get; set; } = Hba.Pricing.Domain.Tariffs.ServiceType.Delivery;

    /// <summary>
    /// Tarif au kilogramme au-dela du poids inclus. Zero vaut « pas de supplement ».
    /// Refuse par l'agregat sur une grille de course.
    /// </summary>
    public long PerKilogram { get; set; }

    /// <summary>Poids inclus dans le prix de base, en grammes.</summary>
    public int IncludedWeightGrams { get; set; }

    public long BaseFare { get; set; }

    public long PerKilometer { get; set; }

    public long PerMinute { get; set; }

    public long MinimumFare { get; set; }

    /// <summary>10000 signifie « aucune majoration ».</summary>
    public int SurgeBasisPoints { get; set; } = 10_000;

    /// <summary>Part du livreur sur le total, en points de base.</summary>
    public int DriverShareBasisPoints { get; set; }
}

/// <summary>
/// Cree la grille initiale au demarrage si la table est vide.
///
/// UNE SEULE FOIS, ET JAMAIS DE MISE A JOUR : une grille n'est pas modifiee,
/// on en cree une nouvelle version et on clot la precedente. Reecrire la grille
/// a chaque demarrage changerait retroactivement le prix de courses deja
/// confirmees, ce que l'ADR 0004 interdit.
/// </summary>
internal sealed class InitialTariffSeeder(
    IServiceScopeFactory scopeFactory,
    IOptions<InitialTariffOptions> options,
    ILogger<InitialTariffSeeder> logger) : BackgroundService
{
    /// <summary>
    /// Nombre d'essais avant d'abandonner le semis pour cette execution.
    /// </summary>
    private const int MaxAttempts = 5;

    /// <summary>
    /// BackgroundService ET NON IHostedService.StartAsync, ET AUCUNE EXCEPTION
    /// NE SORT D'ICI.
    ///
    /// Une exception levee dans StartAsync empeche l'hote de demarrer : le
    /// conteneur meurt. Or ce semis touche la base, et une base qui met dix
    /// secondes a accepter les connexions au demarrage de la pile est un
    /// incident ordinaire, pas une erreur de configuration. Faire mourir
    /// Pricing pour ca transforme un hoquet en panne, et la politique de
    /// redemarrage de Docker en boucle.
    ///
    /// La distinction avec les garde-fous poses ailleurs — cle de signature
    /// d'Identity, operateur SMS de Notification — tient a la nature du defaut :
    /// une configuration manquante est permanente et doit arreter le service ;
    /// une dependance momentanement injoignable se reessaie. L'absence de
    /// grille, elle, est deja geree : Pricing repond NO_TARIFF, explicitement.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        if (!settings.Enabled)
        {
            logger.LogInformation("Aucune grille initiale configuree : Pricing repondra NO_TARIFF.");
            return;
        }

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await SeedAsync(settings, stoppingToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
#pragma warning disable CA1031 // Le semis ne doit jamais empecher le service de servir.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                if (attempt == MaxAttempts)
                {
                    logger.LogError(
                        ex,
                        "Grille initiale non semee apres {Attempts} essais. Pricing demarre quand meme et "
                        + "repondra NO_TARIFF jusqu'a ce qu'une grille existe.",
                        MaxAttempts);
                    return;
                }

                logger.LogWarning(
                    "Semis de la grille impossible (essai {Attempt}/{Max}) : {Raison}. Nouvelle tentative.",
                    attempt,
                    MaxAttempts,
                    ex.Message);

                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task SeedAsync(InitialTariffOptions settings, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<PricingDbContext>();

        // LA CONDITION PORTE SUR LA CLE DE CETTE GRILLE, PLUS SUR LA TABLE ENTIERE.
        // Depuis que la nature entre dans la cle, une base qui contient la grille des
        // colis n'a pas pour autant celle des courses : « la table n'est pas vide »
        // aurait empeche a jamais le semis de la seconde. La condition reste
        // volontairement etroite — meme zone, meme nature, meme vehicule — et le
        // semis ne touche jamais a une grille existante.
        // LA ZONE EST NORMALISEE AVANT LA COMPARAISON, parce que l'agregat la stocke
        // en minuscules : « Cotonou » dans un fichier de configuration et « cotonou »
        // en base sont la meme zone, et comparer les deux telles quelles aurait seme
        // une seconde grille identique a chaque demarrage.
        var zone = settings.ZoneCode.Trim().ToLowerInvariant();

        var dejaSemee = await context.Tariffs
            .AnyAsync(
                t => t.ZoneCode == zone
                    && t.ServiceType == settings.ServiceType
                    && t.VehicleType == settings.VehicleType,
                cancellationToken)
            .ConfigureAwait(false);

        if (dejaSemee)
        {
            return;
        }

        // LA PLAGE EST LA JOURNEE ENTIERE, ET CE N'EST PAS CONFIGURABLE. Un semis de
        // demarrage qui poserait une grille de nuit laisserait la journee sans prix,
        // et le premier devis a midi echouerait sur NO_TARIFF sans que la
        // configuration ait l'air fausse. Les plages se saisissent par la route
        // d'administration, ou l'on voit celles qui existent deja.
        var tariff = Tariff.Create(
            Guid.CreateVersion7(),
            settings.Version,
            settings.ZoneCode,
            settings.ServiceType,
            settings.VehicleType,
            TimeBand.Journee,
            MoneyXof.FromNonNegative(settings.BaseFare),
            MoneyXof.FromNonNegative(settings.PerKilometer),
            MoneyXof.FromNonNegative(settings.PerMinute),
            MoneyXof.FromNonNegative(settings.MinimumFare),
            settings.SurgeBasisPoints,
            settings.DriverShareBasisPoints,
            DateTimeOffset.UtcNow,
            MoneyXof.FromNonNegative(settings.PerKilogram),
            settings.IncludedWeightGrams);

        context.Tariffs.Add(tariff);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogWarning(
            "Grille initiale {Version} creee pour la zone {Zone}, nature {Nature}, vehicule {Vehicule} : "
            + "{PerKm} XOF/km, plancher {Minimum} XOF, part livreur {Share} points de base, journee entiere. "
            + "Les autres natures, vehicules et plages horaires se creent par la route d'administration.",
            settings.Version,
            settings.ZoneCode,
            settings.ServiceType,
            settings.VehicleType,
            settings.PerKilometer,
            settings.MinimumFare,
            settings.DriverShareBasisPoints);
    }
}
