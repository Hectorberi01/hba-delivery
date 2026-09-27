using Grpc.Core;
using Hba.BuildingBlocks.Grpc.ServiceAuth;
using Hba.Contracts.Driver.V1;
using Hba.Delivery.Application.Ports;
using Hba.Delivery.Domain.Deliveries;
using DomainVehicleType = Hba.Delivery.Domain.Deliveries.VehicleType;

namespace Hba.Delivery.Infrastructure.Clients;

/// <summary>
/// Ne demande QUE le profil public : nom, véhicule, téléphone. Le service
/// Delivery n'a aucune raison de connaître le KYC ni la position d'un livreur.
///
/// CET APPEL PART D'UN CONSOMMATEUR KAFKA, donc sans aucun utilisateur : il
/// naît de « OfferAccepted », pas d'une requête. Le service Driver est en
/// [Authorize] et refuserait un appel nu ; la livraison resterait alors en
/// SEARCHING_DRIVER alors que le livreur voit la course acceptée dans son
/// application. D'où le jeton de service — voir ADR 0018.
/// </summary>
internal sealed class DriverGrpcDirectory(
    DriverService.DriverServiceClient client,
    IServiceTokenProvider tokens) : IDriverDirectory
{
    public async Task<AssignedDriver?> GetPublicProfileAsync(string driverId, CancellationToken cancellationToken)
    {
        var headers = await tokens.AuthorizationAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var profile = await client.GetDriverPublicProfileAsync(
                new GetDriverRequest { DriverId = driverId },
                headers: headers,
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
