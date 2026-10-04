using Grpc.Core;
using Hba.BuildingBlocks.Grpc.ServiceAuth;
using Hba.Contracts.Driver.V1;
using Hba.Ride.Application.Ports;
using Microsoft.Extensions.Logging;
using DomainDriver = Hba.Ride.Domain.Rides.AssignedDriver;
using DomainVehicle = Hba.Ride.Domain.Rides.VehicleType;

namespace Hba.Ride.Infrastructure.Clients;

/// <summary>
/// Adaptateur vers le service Driver, pour le profil public d'un chauffeur.
/// </summary>
///
/// <remarks>
/// LE JETON DE SERVICE, PAS CELUI D'UN UTILISATEUR. Cet appel descend d'un
/// consommateur Kafka : aucune personne ne l'a declenche, il n'y a donc aucun jeton
/// a reporter. Driver est en [Authorize], et sans ce jeton l'appel part nu —
/// exactement la panne qui a fait que les recus de Notification ne partaient pas
/// pendant des jours, avec un motif faux dans les journaux. Voir ADR 0018.
///
/// TOUTE PANNE REND NULL PLUTOT QUE DE LEVER. Quand cet appel a lieu, l'acceptation
/// est deja actee chez Dispatch et chez Driver : lever laisserait un chauffeur
/// reserve sur une course qui ne l'a pas enregistre. Le passager lit « un chauffeur
/// arrive » au lieu de son nom, ce qui est un degrade acceptable.
/// </remarks>
internal sealed class DriverGrpcDirectory(
    DriverService.DriverServiceClient client,
    IServiceTokenProvider jetons,
    ILogger<DriverGrpcDirectory> journal) : IRideDriverDirectory
{
    public async Task<DomainDriver?> GetPublicProfileAsync(string driverId, CancellationToken cancellationToken)
    {
        try
        {
            var entetes = await jetons.AuthorizationAsync(cancellationToken).ConfigureAwait(false);

            var profil = await client.GetDriverPublicProfileAsync(
                new GetDriverRequest { DriverId = driverId },
                headers: entetes,
                cancellationToken: cancellationToken);

            return DomainDriver.Create(
                profil.DriverId,
                profil.DisplayName,
                profil.Phone,
                Vehicule(profil.VehicleType),
                profil.VehiclePlate,
                profil.VehicleBrand,
                profil.VehicleModel,
                profil.VehicleColor);
        }
        catch (RpcException erreur)
        {
            journal.LogWarning(
                erreur,
                "Driver n'a pas rendu le profil du chauffeur {DriverId} ({Statut}) : la course sera "
                + "affectee sans son nom, et le SMS d'affectation ne partira pas.",
                driverId,
                erreur.StatusCode);

            return null;
        }
        catch (ArgumentException erreur)
        {
            // UN PROFIL SANS NOM N'EST PAS EXPLOITABLE, et AssignedDriver.Create le
            // refuse. Ce n'est pas une panne reseau : c'est un dossier incomplet cote
            // Driver, et il faut le voir comme tel dans les journaux.
            journal.LogWarning(
                erreur,
                "Profil du chauffeur {DriverId} inexploitable : la course sera affectee sans son nom.",
                driverId);

            return null;
        }
    }

    /// <summary>
    /// LE FOURGON ET LE VELO N'ONT PAS D'EQUIVALENT ICI, et c'est normal : le
    /// contrat de Ride ne connait que la moto, la voiture et le tricycle — eux seuls
    /// transportent des passagers. Un chauffeur affecte a une course dans un fourgon
    /// signalerait un filtre d'habilitation defaillant cote Driver, et « non
    /// precise » est alors la reponse honnete.
    /// </summary>
    private static DomainVehicle Vehicule(Hba.Contracts.Common.V1.VehicleType type) => type switch
    {
        Hba.Contracts.Common.V1.VehicleType.Motorcycle => DomainVehicle.Motorcycle,
        Hba.Contracts.Common.V1.VehicleType.Car => DomainVehicle.Car,
        Hba.Contracts.Common.V1.VehicleType.Tricycle => DomainVehicle.Tricycle,
        _ => DomainVehicle.Unspecified,
    };
}
