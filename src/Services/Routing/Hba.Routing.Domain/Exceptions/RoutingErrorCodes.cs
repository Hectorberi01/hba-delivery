namespace Hba.Routing.Domain.Exceptions;

/// <summary>Codes métier stables du service Routing.</summary>
public static class RoutingErrorCodes
{
    public const string InvalidCoordinates = "INVALID_COORDINATES";

    public const string InvalidRoute = "INVALID_ROUTE";

    /// <summary>Une matrice sans origine ou sans destination ne veut rien dire.</summary>
    public const string EmptyMatrix = "EMPTY_MATRIX";

    /// <summary>
    /// La matrice demandée dépasse ce qu'un appel peut traiter.
    ///
    /// CE N'EST PAS UNE LIMITE ARBITRAIRE : le temps de calcul croît avec le
    /// PRODUIT des deux dimensions, et le client qui attend un chauffeur, lui,
    /// attend en secondes.
    /// </summary>
    public const string MatrixTooLarge = "MATRIX_TOO_LARGE";
}
