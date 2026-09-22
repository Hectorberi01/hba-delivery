using Hba.Delivery.Domain.Deliveries;

namespace Hba.Delivery.Application.Ports;

/// <summary>
/// Profil réduit du livreur, tel que le client et le commerçant ont le droit de
/// le voir : nom, véhicule, téléphone. Rien de plus ne doit transiter.
/// </summary>
public interface IDriverDirectory
{
    Task<AssignedDriver?> GetPublicProfileAsync(string driverId, CancellationToken cancellationToken);
}
