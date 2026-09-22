namespace Hba.Delivery.Application.Ports;

/// <summary>
/// Référence lisible communiquée aux clients et aux livreurs, ex. HBA-7KQ3M2.
/// Distincte de l'identifiant technique.
/// </summary>
public interface IReferenceGenerator
{
    string NextDeliveryReference();
}
