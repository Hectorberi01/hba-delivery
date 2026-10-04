namespace Hba.Routing.Domain.Routes;

/// <summary>
/// La façon de circuler, et non le véhicule.
/// </summary>
///
/// <remarks>
/// CET ENUM NE RECOPIE PAS VehicleType, ET C'EST DÉLIBÉRÉ. Un tricycle et une
/// voiture empruntent les mêmes voies : ils partagent un profil. Le vélo, lui,
/// passe là où aucune voiture ne passe. Ce qui décide d'un itinéraire est la
/// manière de circuler, pas la carte grise.
///
/// TROIS VALEURS PARCE QU'OSRM EN CONNAÎT TROIS. Les aligner sur les cinq types
/// de véhicule aurait promis une finesse que le moteur ne sait pas rendre : la
/// correspondance aurait été inventée de toute façon, mais plus loin, et dans le
/// code de chaque appelant.
/// </remarks>
public enum RouteProfile
{
    Unspecified = 0,
    Car = 1,
    Motorcycle = 2,
    Bicycle = 3,
}
