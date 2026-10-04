using Hba.BuildingBlocks.Security;
using Hba.Contracts.Common.V1;
using Hba.Contracts.Pricing.V1;

namespace Hba.Gateway.Endpoints.Web;

/// <summary>
/// Les grilles tarifaires, pour le back-office.
/// </summary>
///
/// <remarks>
/// POURQUOI CES ROUTES N'EXISTAIENT PAS, ET CE QUE LEUR ABSENCE COÛTAIT. La
/// seule grille du système était semée au démarrage par
/// <c>InitialTariffSeeder</c>, depuis la configuration, pour UN couple
/// (zone, véhicule). Les autres véhicules — voiture, camionnette, vélo,
/// tricycle — n'avaient aucun prix : toute course les concernant échouait sur
/// <c>NO_TARIFF</c>. Changer un prix demandait d'éditer une variable
/// d'environnement et de RECRÉER le conteneur.
///
/// C'EST <c>HbaPolicies.Finance</c> ET NON <c>BackOffice</c>. La politique
/// « finance » vaut déjà « admin ou finance », qui est exactement la population
/// décidée le 1er octobre 2026. Ops et support sont du back-office mais n'ont
/// rien à faire ici : ils traitent des courses, pas des barèmes.
///
/// LA PASSERELLE NE FAIT QUE LE PREMIER FILTRE. Pricing revérifie lui-même par
/// <c>PricingAccess</c> — le jeton qui lui parvient est celui de l'utilisateur
/// final, reporté, pas celui d'un service de confiance (ADR 0007).
///
/// CES ROUTES SONT LE SEUL ENDROIT OÙ UNE GRILLE DE COURSE PEUT NAÎTRE. Depuis
/// que la nature entre dans la clé de la grille (1er octobre 2026), AUCUNE
/// grille de course n'existe : le semis de démarrage n'en sème qu'une, de colis.
/// Tant que personne n'en a créé une ici, chaque devis de course échoue sur
/// <c>NO_TARIFF</c>. Les prix des colis n'y ont PAS été recopiés : une course de
/// personnes n'a ni le même plancher ni la même part du chauffeur, et les
/// dupliquer aurait donné des chiffres faux qui ont l'air justes.
/// </remarks>
public static class AdminTariffEndpoints
{
    public static IEndpointRouteBuilder MapAdminTariffEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/admin/v1/tariffs")
            .RequireAuthorization(HbaPolicies.Finance);

        // LA LECTURE NE REND QUE LES GRILLES EN VIGUEUR PAR DÉFAUT. L'historique
        // ne se supprime jamais — un devis émis hier se réclame de la grille
        // d'hier (ADR 0004) —, mais l'afficher systématiquement noierait la
        // seule ligne qui compte sous toutes celles qui ne comptent plus.
        group.MapGet("/", async (
            string? zone,
            string? nature,
            string? vehicule,
            bool? closes,
            PricingService.PricingServiceClient pricing,
            CancellationToken cancellationToken) =>
        {
            var reponse = await pricing.ListTariffsAsync(
                new ListTariffsRequest
                {
                    ZoneCode = zone ?? string.Empty,

                    // EN LECTURE, UNE NATURE ABSENTE REND LES DEUX. C'est le seul
                    // appel où UNSPECIFIED veut dire « tout » : à la création, il
                    // vaut un refus.
                    ServiceType = Nature(nature),
                    VehicleType = Vehicule(vehicule),
                    IncludeClosed = closes ?? false,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(reponse.Tariffs.Select(Lisible));
        });

        group.MapPost("/", async (
            CreerUneGrille corps,
            HttpContext http,
            PricingService.PricingServiceClient pricing,
            CancellationToken cancellationToken) =>
        {
            // LA CLÉ D'IDEMPOTENCE VIENT DE L'EN-TÊTE, comme partout ailleurs
            // dans cette passerelle. Sans elle, un double appui crée deux
            // versions dont la seconde clôt la première à la même seconde.
            var cle = http.Request.Headers["Idempotency-Key"].ToString();
            if (string.IsNullOrWhiteSpace(cle))
            {
                return Results.BadRequest(new
                {
                    error = "L'en-tete Idempotency-Key est obligatoire pour creer une grille.",
                });
            }

            var grille = await pricing.CreateTariffAsync(
                new CreateTariffRequest
                {
                    TariffVersion = corps.Version,
                    ZoneCode = corps.Zone,

                    // LA NATURE N'A PAS DE DÉFAUT ICI. Un nom mal orthographié
                    // devient UNSPECIFIED et Pricing le refuse, exactement comme
                    // pour le véhicule : écrire un barème de colis sous le nom
                    // d'une course ne se verrait qu'au premier versement.
                    ServiceType = Nature(corps.Nature),
                    VehicleType = Vehicule(corps.Vehicule),
                    BaseFare = Montant(corps.PriseEnCharge),
                    PerKilometer = Montant(corps.ParKilometre),
                    PerMinute = Montant(corps.ParMinute),
                    MinimumFare = Montant(corps.Plancher),
                    SurgeBasisPoints = corps.MajorationPointsDeBase,
                    DriverShareBasisPoints = corps.PartDuLivreurPointsDeBase,
                    IdempotencyKey = cle,

                    // DEUX BORNES ABSENTES VALENT LA JOURNÉE ENTIÈRE, et c'est
                    // Pricing qui le traduit : l'absence et le zéro sont la même
                    // chose en JSON comme en protobuf3, cette passerelle ne peut
                    // pas les distinguer non plus.
                    BandFromMinute = corps.PlageDebutMinute ?? 0,
                    BandToMinute = corps.PlageFinMinute ?? 0,
                    PerKilogram = Montant(corps.AuKilo ?? 0),
                    IncludedWeightGrams = corps.PoidsInclusGrammes ?? 0,

                    // ABSENTE VAUT GRATUIT. Comme pour la plage, l'absence et le
                    // zéro sont indistinguables en JSON ; ici les deux veulent
                    // dire la même chose, donc il n'y a rien à arbitrer.
                    CancellationFeeBasisPoints = corps.RetenueAnnulationPointsDeBase ?? 0,
                },
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(grille));
        });

        // CLORE SANS REMPLACER LAISSE LE COUPLE SANS PRIX, et c'est parfois ce
        // qu'on veut : retirer le tricycle d'une ville, fermer une zone. Le
        // refus NO_TARIFF est alors la bonne réponse — meilleure qu'un prix
        // inventé. Un changement de prix ordinaire passe par POST, qui clôt
        // l'ancienne version en créant la nouvelle, sans instant creux.
        group.MapPost("/{id}/close", async (
            string id,
            PricingService.PricingServiceClient pricing,
            CancellationToken cancellationToken) =>
        {
            var grille = await pricing.CloseTariffAsync(
                new CloseTariffRequest { TariffId = id },
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(grille));
        });

        return app;
    }

    /// <summary>Ce que le back-office envoie pour créer une grille.</summary>
    ///
    /// <remarks>
    /// LES QUATRE DERNIERS CHAMPS SONT FACULTATIFS, LES AUTRES NON — et
    /// <c>Nature</c> fait partie des autres. Elle est arrivée après les premières
    /// grilles, mais la rendre facultative avec un repli sur le colis aurait
    /// permis de créer une grille de course en oubliant un mot.
    ///
    /// LA PLAGE EST EN MINUTES DEPUIS MINUIT, HEURE LOCALE. 1320 et 360 écrivent
    /// 22 h → 6 h, qui enjambe minuit. Absente, la grille couvre la journée.
    ///
    /// LA RETENUE D'ANNULATION EST EN POINTS DE BASE DU DEVIS, pas en francs :
    /// 2000 vaut 20 %. Absente, elle vaut zéro — l'annulation reste gratuite,
    /// ce qui est le comportement d'avant le 2 octobre 2026.
    /// </remarks>
    public sealed record CreerUneGrille(
        string Version,
        string Zone,
        string Nature,
        string Vehicule,
        long PriseEnCharge,
        long ParKilometre,
        long ParMinute,
        long Plancher,
        int MajorationPointsDeBase,
        int PartDuLivreurPointsDeBase,
        int? PlageDebutMinute = null,
        int? PlageFinMinute = null,
        long? AuKilo = null,
        int? PoidsInclusGrammes = null,
        int? RetenueAnnulationPointsDeBase = null);

    /// <summary>
    /// La nature, écrite en toutes lettres par l'appelant.
    ///
    /// UN NOM INCONNU DEVIENT UNSPECIFIED. En lecture, cela veut dire « les deux
    /// natures » ; à la création, Pricing le refuse. Les deux comportements sont
    /// voulus et c'est le service qui les porte.
    /// </summary>
    private static ServiceType Nature(string? nom) => nom?.Trim().ToLowerInvariant() switch
    {
        "colis" or "livraison" or "delivery" => ServiceType.Delivery,
        "course" or "ride" => ServiceType.Ride,
        _ => ServiceType.Unspecified,
    };

    /// <summary>
    /// Le véhicule, écrit en toutes lettres par l'appelant.
    ///
    /// UN NOM INCONNU DEVIENT UNSPECIFIED, ET PRICING LE REFUSERA. Traduire un
    /// nom mal orthographié en moto écrirait un barème moto sous le nom d'un
    /// autre véhicule ; c'est le service qui tranche, pas cette conversion.
    /// </summary>
    private static VehicleType Vehicule(string? nom) => nom?.Trim().ToLowerInvariant() switch
    {
        "moto" or "motorcycle" => VehicleType.Motorcycle,
        "voiture" or "car" => VehicleType.Car,
        "camionnette" or "van" => VehicleType.Van,
        "velo" or "vélo" or "bicycle" => VehicleType.Bicycle,
        "tricycle" => VehicleType.Tricycle,
        _ => VehicleType.Unspecified,
    };

    private static Money Montant(long valeur) => new() { Amount = valeur, Currency = "XOF" };

    private static object Lisible(Tariff t) => new
    {
        id = t.Id,
        version = t.TariffVersion,
        zone = t.ZoneCode,
        nature = t.ServiceType.ToString(),
        vehicule = t.VehicleType.ToString(),
        priseEnCharge = t.BaseFare?.Amount ?? 0,
        parKilometre = t.PerKilometer?.Amount ?? 0,
        parMinute = t.PerMinute?.Amount ?? 0,
        plancher = t.MinimumFare?.Amount ?? 0,
        auKilo = t.PerKilogram?.Amount ?? 0,
        poidsInclusGrammes = t.IncludedWeightGrams,
        majorationPointsDeBase = t.SurgeBasisPoints,
        partDuLivreurPointsDeBase = t.DriverShareBasisPoints,
        retenueAnnulationPointsDeBase = t.CancellationFeeBasisPoints,

        // LA PLAGE SORT EN MINUTES ET EN TEXTE. Les minutes sont la donnée —
        // c'est elle qu'un écran renvoie pour modifier la grille ; le libellé
        // n'est là que pour être lu, et le reconstruire côté navigateur aurait
        // demandé d'y refaire le cas de la plage qui enjambe minuit.
        plageDebutMinute = t.BandFromMinute,
        plageFinMinute = t.BandToMinute,
        plage = Plage(t.BandFromMinute, t.BandToMinute),
        debut = t.ValidFrom?.ToDateTimeOffset(),
        fin = t.ValidUntil?.ToDateTimeOffset(),
        enVigueur = t.ValidUntil is null,
    };

    /// <summary>« 22:00–06:00 », ou « toute la journee » pour 0 → 1440.</summary>
    private static string Plage(int debut, int fin)
        => debut == 0 && fin is 0 or 1440
            ? "toute la journee"
            : $"{Heure(debut)}–{Heure(fin)}";

    private static string Heure(int minute)
        => minute == 1440 ? "24:00" : $"{minute / 60:D2}:{minute % 60:D2}";
}
