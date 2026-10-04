using Hba.BuildingBlocks.Grpc.ServiceAuth;
using Hba.Contracts.Driver.V1;
using Hba.Contracts.Location.V1;
using Hba.Dispatch.Application.Common.Interfaces;
using Hba.Dispatch.Domain.Dispatching;
// DEUX « DriverAvailability » SE CROISENT ICI : le verdict du port, cote
// application, et le message du contrat. C'est exactement le role de cet
// adaptateur que de traduire l'un en l'autre, donc la collision est normale —
// elle se leve par un alias, pas en renommant l'un des deux.
using DomainAvailability = Hba.Dispatch.Application.Common.Interfaces.DriverAvailability;
using DomainCapability = Hba.Dispatch.Domain.Dispatching.DriverCapability;
using DomainGeoPoint = Hba.Dispatch.Domain.ValueObjects.GeoPoint;
using DomainVehicle = Hba.Dispatch.Domain.Dispatching.VehicleType;
using ProtoGeoPoint = Hba.Contracts.Common.V1.GeoPoint;

namespace Hba.Dispatch.Infrastructure.Services.Drivers;

/// <summary>
/// Adaptateur vers Location ET Driver.
/// </summary>
///
/// <remarks>
/// C'EST ICI QUE LE CROISEMENT A ATTERRI, ET C'EST LE PRIX DE LA PHASE 9. Jusqu'au
/// 1er octobre 2026, Driver repondait seul : son handler interrogeait Redis GEO pour
/// la proximite, puis sa propre base pour l'eligibilite, et Dispatch ne faisait qu'un
/// appel. Depuis que les positions vivent dans Location, personne ne peut plus repondre
/// seul — « qui est proche » et « qui est eligible » sont deux services. Le croisement
/// est donc chez l'appelant, c'est-a-dire ici.
///
/// DEUX APPELS AU LIEU D'UN, SUR LE CHEMIN DE LA PREMIERE OFFRE. Les deux sont courts
/// et sequentiels : il faut les identifiants avant de pouvoir les filtrer. C'est le
/// cout assume de la frontiere, et il est paye a chaque anneau de chaque vague.
///
/// LA SUR-INTERROGATION A DEMENAGE AVEC LE CROISEMENT. Location rend les plus proches
/// SANS savoir qui est eligible : Driver en ecartera une partie. Demander exactement le
/// vivier voulu rendrait donc presque toujours moins. On en demande davantage, et on
/// tronque apres le filtre — c'est le meme raisonnement qu'avant, deplace d'un service.
///
/// AUCUN DE CES APPELS NE REPORTE LE JETON DE L'APPELANT, et ils n'ont pourtant pas la
/// meme origine : la recherche de proximite part du planificateur, ou il n'y a aucun
/// utilisateur ; la verification d'un livreur part d'une requete de l'exploitation,
/// dont l'autorisation a deja ete verifiee dans le handler. Dans les deux cas Driver et
/// Location sont en [Authorize] et c'est le jeton de service qui ouvre la porte. Voir
/// ADR 0018.
/// </remarks>
internal sealed class DriverFinder(
    LocationService.LocationServiceClient locations,
    DriverService.DriverServiceClient drivers,
    IServiceTokenProvider tokens) : IDriverFinder
{
    /// <summary>
    /// Facteur de sur-interrogation de Location.
    /// </summary>
    ///
    /// <remarks>
    /// LA GEOGRAPHIE NE SAIT PAS QUI EST DISPONIBLE. Location rend les positions les
    /// plus proches ; Driver ecarte ensuite ceux qui sont hors ligne, en mission, non
    /// verifies, sur le mauvais vehicule ou sans l'habilitation voulue. Demander
    /// exactement le nombre voulu rendrait donc presque toujours moins.
    ///
    /// CINQ, ET C'EST LA MEME VALEUR QU'AVANT LE DEPLACEMENT. Elle vivait dans le
    /// handler de Driver, qui faisait ce croisement ; la changer en demenageant aurait
    /// melange deux decisions — ou poser le croisement, et combien sur-interroger.
    /// </remarks>
    private const int SurInterrogation = 5;

    private const int CandidatsMinimum = 50;

    /// <summary>
    /// « Son telephone n'a pas donne signe de vie dans la fenetre de fraicheur. »
    ///
    /// LE MEME CODE QUE CELUI QUE DRIVER PRONONCAIT, et c'est volontaire : la console
    /// d'exploitation l'affiche deja, et le renommer en demenageant aurait casse un
    /// ecran pour une question de vocabulaire.
    /// </summary>
    private const string PositionPerimee = "DRIVER_POSITION_STALE";

    public async Task<IReadOnlyList<CandidateDriver>> FindAvailableNearbyAsync(
        DomainGeoPoint center,
        int radiusMeters,
        int limit,
        DomainCapability requiredCapability,
        DomainVehicle vehicleType,
        IReadOnlyCollection<string> excludeDriverIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(center);
        ArgumentNullException.ThrowIfNull(excludeDriverIds);

        if (limit <= 0 || radiusMeters <= 0)
        {
            return [];
        }

        var entetes = await tokens.AuthorizationAsync(cancellationToken).ConfigureAwait(false);

        // ---- 1. LA GEOGRAPHIE : qui est pres d'ici, et a donne signe de vie ------
        //
        // L'EXCLUSION PART AVEC LA QUESTION, et non apres : la limite s'applique
        // APRES l'exclusion cote Location. Filtrer ici rendrait des vagues plus
        // maigres que demande, et une vague se viderait au fil de ses tours sans que
        // rien ne le dise.
        var proximite = new FindNearbyDriversRequest
        {
            Center = new ProtoGeoPoint { Latitude = center.Latitude, Longitude = center.Longitude },
            RadiusMeters = radiusMeters,
            Limit = Math.Max(limit * SurInterrogation, CandidatsMinimum),
        };

        proximite.ExcludeDriverIds.AddRange(excludeDriverIds);

        var proches = await locations
            .FindNearbyDriversAsync(proximite, headers: entetes, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (proches.Drivers.Count == 0)
        {
            return [];
        }

        // ---- 2. L'ELIGIBILITE : lesquels peuvent recevoir une offre --------------
        //
        // L'HABILITATION ET LE VEHICULE PARTENT A DRIVER, PAS FILTRES ICI. Lui seul
        // connait les dossiers ; filtrer apres coup rendrait des vagues plus maigres
        // que demande, exactement comme filtrer la liste d'exclusion apres coup le
        // faisait.
        var filtre = new FilterAvailableDriversRequest
        {
            VehicleType = Vehicule(vehicleType),
            RequiredCapability = Capacite(requiredCapability),
        };

        filtre.DriverIds.AddRange(proches.Drivers.Select(d => d.DriverId));

        var eligibles = await drivers
            .FilterAvailableDriversAsync(filtre, headers: entetes, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (eligibles.Drivers.Count == 0)
        {
            return [];
        }

        // ---- 3. LE RECOLLEMENT, DANS L'ORDRE DE LOCATION -------------------------
        //
        // L'ORDRE VIENT DE LA PROXIMITE, PAS DE DRIVER. Le contrat de
        // FilterAvailableDrivers dit explicitement que l'ordre d'entree n'est pas
        // conserve en sortie : c'est une lecture par identifiants, pas un classement.
        // On repart donc de la liste de Location, qui est triee du plus proche au plus
        // lointain, et on ne garde que les autorises.
        //
        // LA POSITION EST REPRISE, et elle sert au classement par temps de trajet
        // reel : on ne calcule pas une duree depuis un point qu'on ignore.
        var autorises = eligibles.Drivers.Select(d => d.DriverId).ToHashSet(StringComparer.Ordinal);

        return
        [
            .. proches.Drivers
                .Where(d => autorises.Contains(d.DriverId))
                .Take(limit)
                .Select(d => new CandidateDriver(
                    d.DriverId,
                    d.DistanceMeters,
                    d.Position?.Point?.Latitude ?? 0,
                    d.Position?.Point?.Longitude ?? 0)),
        ];
    }

    /// <summary>
    /// « CE livreur peut-il recevoir une offre, et a quelle distance se trouve-t-il ? »
    /// </summary>
    ///
    /// <remarks>
    /// DEUX APPELS, ET L'ORDRE DES VERDICTS EST CE QU'IL FAUT PRESERVER. L'ancien
    /// handler de Driver lisait l'etat AVANT la position, parce qu'un livreur suspendu
    /// qui laisse tourner son telephone a une position parfaitement fraiche : annoncer
    /// « position perimee » aurait ete exact sur la forme et faux sur le fond. Cette
    /// precedence est tenue ici — le motif de Driver gagne, et DRIVER_POSITION_STALE
    /// n'est prononce que si Driver n'a rien a reprocher.
    ///
    /// DRIVER NE SAIT PLUS DIRE « LE TELEPHONE EST ETEINT », et ce motif-la est
    /// desormais produit par cette methode. C'est le seul endroit du depot ou il nait.
    /// </remarks>
    public async Task<DomainAvailability> CheckAsync(
        string driverId,
        DomainGeoPoint reference,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);

        // LE JETON DE SERVICE ICI AUSSI, ALORS QUE CET APPEL-LA PART D'UNE REQUETE
        // ADMIN. Dispatch n'est pas une passerelle : il ne reporte pas le jeton de
        // l'operateur vers Driver. L'autorisation de l'operateur a ete verifiee dans le
        // handler, ou elle appartient. Voir ADR 0018.
        var entetes = await tokens.AuthorizationAsync(cancellationToken).ConfigureAwait(false);

        var verdict = await drivers
            .CheckDriverAvailabilityAsync(
                new CheckDriverAvailabilityRequest { DriverId = driverId },
                headers: entetes,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (!verdict.Offerable)
        {
            // ON N'INTERROGE MEME PAS LOCATION. Le motif de Driver est deja le bon, et
            // demander ou se trouve un livreur suspendu serait une lecture inutile
            // d'une donnee sensible.
            return new DomainAvailability(
                false,
                string.IsNullOrEmpty(verdict.Reason) ? null : verdict.Reason,
                0);
        }

        var position = await locations
            .GetPositionAsync(
                new GetPositionRequest
                {
                    DriverId = driverId,
                    Reference = new ProtoGeoPoint
                    {
                        Latitude = reference.Latitude,
                        Longitude = reference.Longitude,
                    },
                },
                headers: entetes,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return position.Found
            ? new DomainAvailability(true, null, position.DistanceMeters)

            // UNE DISTANCE DE ZERO AVEC UN REFUS, et jamais avec un accord : zero metre
            // se lirait « sur place », ce qui est le contraire de ce qu'on dit ici.
            : new DomainAvailability(false, PositionPerimee, 0);
    }

    private static Hba.Contracts.Common.V1.VehicleType Vehicule(DomainVehicle type) => type switch
    {
        DomainVehicle.Motorcycle => Hba.Contracts.Common.V1.VehicleType.Motorcycle,
        DomainVehicle.Car => Hba.Contracts.Common.V1.VehicleType.Car,
        DomainVehicle.Van => Hba.Contracts.Common.V1.VehicleType.Van,
        DomainVehicle.Bicycle => Hba.Contracts.Common.V1.VehicleType.Bicycle,
        DomainVehicle.Tricycle => Hba.Contracts.Common.V1.VehicleType.Tricycle,
        _ => Hba.Contracts.Common.V1.VehicleType.Unspecified,
    };

    private static Hba.Contracts.Common.V1.DriverCapability Capacite(DomainCapability capacite) => capacite switch
    {
        DomainCapability.PackageDelivery => Hba.Contracts.Common.V1.DriverCapability.PackageDelivery,
        DomainCapability.PassengerTransport => Hba.Contracts.Common.V1.DriverCapability.PassengerTransport,
        DomainCapability.FoodDelivery => Hba.Contracts.Common.V1.DriverCapability.FoodDelivery,
        _ => Hba.Contracts.Common.V1.DriverCapability.Unspecified,
    };
}
