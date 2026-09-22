using Grpc.Core;
using Hba.Contracts.Driver.V1;
using Hba.Delivery.Application.Ports;
using Hba.Delivery.Domain.Deliveries;
using DomainVehicleType = Hba.Delivery.Domain.Deliveries.VehicleType;

namespace Hba.Delivery.Infrastructure.Clients;

/// <summary>
/// Ne demande QUE le profil public : nom, véhicule, téléphone. Le service
/// Delivery n'a aucune raison de connaître le KYC ni la position d'un livreur.
/// </summary>
internal sealed class DriverGrpcDirectory(DriverService.DriverServiceClient client) : IDriverDirectory
{
    public async Task<AssignedDriver?> GetPublicProfileAsync(string driverId, CancellationToken cancellationToken)
    {
        try
        {
            var profile = await client.GetDriverPublicProfileAsync(
                new GetDriverRequest { DriverId = driverId },
                cancellationToken: cancellationToken);

            return AssignedDriver.Create(
                profile.DriverId,
                profile.DisplayName,
                profile.Phone,
                MapVehicle(profile.VehicleType),
                profile.VehiclePlate);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    private static DomainVehicleType MapVehicle(Contracts.Common.V1.VehicleType type) => type switch
    {
        Contracts.Common.V1.VehicleType.Car => DomainVehicleType.Car,
        Contracts.Common.V1.VehicleType.Van => DomainVehicleType.Van,
        _ => DomainVehicleType.Motorcycle,
    };
}
