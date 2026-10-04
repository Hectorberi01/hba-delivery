namespace Hba.Ride.Domain.Rides;

/// <summary>
/// Reprend les valeurs de <c>hba.common.v1.VehicleType</c>. Le domaine ne dépend
/// pas des contrats générés ; la correspondance est faite dans la couche Api.
/// </summary>
///
/// <remarks>
/// LE VÉLO ET LA CAMIONNETTE SONT ABSENTS, ET C'EST VOULU. Ils existent dans
/// l'énuméré commun, mais aucun des deux ne transporte de personnes — décidé le
/// 1er octobre 2026, et déjà appliqué par la table de <c>Vehicle</c> dans
/// Driver. Les déclarer ici laisserait croire qu'une course à vélo se commande,
/// et le refus n'arriverait qu'au dispatch, après paiement.
///
/// LES NUMÉROS SUIVENT LE CONTRAT COMMUN, trous compris : ce sont les mêmes
/// valeurs, et les réordonner pour « combler » casserait la correspondance.
/// </remarks>
public enum VehicleType
{
    Unspecified = 0,
    Motorcycle = 1,
    Car = 2,
    Tricycle = 5,
}
