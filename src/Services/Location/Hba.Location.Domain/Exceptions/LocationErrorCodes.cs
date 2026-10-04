namespace Hba.Location.Domain.Exceptions;

/// <summary>
/// Les refus de ce service.
/// </summary>
///
/// <remarks>
/// IL Y EN A PEU, ET C'EST LE SIGNE QUE LA FRONTIERE EST BIEN PLACEE. Location ne
/// porte aucune regle metier : il ne decide pas qui peut prendre une course, ni ce
/// qu'une position autorise. Les seuls refus possibles sont des refus de FORME —
/// un point hors du globe, un cap impossible, un identifiant mal ecrit.
///
/// « PAS DE POSITION RECENTE » N'EST PAS UN REFUS, et c'est volontaire : c'est une
/// REPONSE. Un telephone eteint est un fait ordinaire du metier, pas une erreur a
/// lever ; <c>PositionLookup.found</c> le dit sans exception.
/// </remarks>
public static class LocationErrorCodes
{
    public const string InvalidCoordinates = "INVALID_COORDINATES";

    public const string InvalidHeading = "INVALID_HEADING";

    public const string InvalidSpeed = "INVALID_SPEED";

    public const string InvalidAccuracy = "INVALID_ACCURACY";

    public const string InvalidId = "INVALID_ID";
}
