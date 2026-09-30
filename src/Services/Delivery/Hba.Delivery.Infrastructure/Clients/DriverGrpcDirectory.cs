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

    /// <summary>
    /// CE QUI ARRIVE ICI EST ECRIT SUR LA COURSE, ET NE SE CORRIGE PLUS.
    /// « AssignedDriver » est une copie figee : un velo avale par le repli
    /// resterait une moto dans l'historique de la course, longtemps apres que
    /// Driver a raison. D'ou un bras par type connu.
    ///
    /// LE REPLI RESTE « MOTO » POUR L'INDETERMINE, et c'est un defaut ANTERIEUR
    /// qu'il faut connaitre : l'enumeration de ce service n'a pas de valeur
    /// « non precise », donc un livreur dont Driver ignore le vehicule est
    /// inscrit comme motard. Le corriger demande d'ajouter Unspecified a
    /// l'enumeration et de le traiter partout ou la course l'affiche.
    /// </summary>
    private static DomainVehicleType MapVehicle(Contracts.Common.V1.VehicleType type) => type switch
    {
        Contracts.Common.V1.VehicleType.Car => DomainVehicleType.Car,
        Contracts.Common.V1.VehicleType.Van => DomainVehicleType.Van,
        Contracts.Common.V1.VehicleType.Bicycle => DomainVehicleType.Bicycle,
        Contracts.Common.V1.VehicleType.Tricycle => DomainVehicleType.Tricycle,
        _ => DomainVehicleType.Motorcycle,
    };
}
