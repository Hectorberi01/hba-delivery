using Hba.BuildingBlocks.Domain;
using Hba.Routing.Domain.Exceptions;

namespace Hba.Routing.Domain.Routes;

/// <summary>Une case de la matrice : une origine vers une destination.</summary>
///
/// <remarks>
/// « JOIGNABLE » N'EST PAS UN DÉTAIL, C'EST LA MOITIÉ DE L'INFORMATION. Quand
/// aucun chemin ne relie deux points — une erreur de saisie, un point tombé dans
/// la lagune —, OSRM ne rend pas une grande distance : il ne rend RIEN. Traduire
/// ce silence par un zéro ferait du point injoignable le MEILLEUR candidat de
/// la vague, et le dispatch enverrait la course à un chauffeur qui ne peut pas
/// venir.
/// </remarks>
public sealed record MatrixCell(int DistanceMeters, int DurationSeconds, bool Reachable)
{
    public static MatrixCell Unreachable { get; } = new(0, 0, false);

    public static MatrixCell Reached(int distanceMeters, int durationSeconds)
        => distanceMeters < 0 || durationSeconds < 0
            ? throw new DomainException(
                RoutingErrorCodes.InvalidRoute,
                "Distance et durée d'un trajet ne peuvent pas être négatives.")
            : new MatrixCell(distanceMeters, durationSeconds, true);
}

/// <summary>
/// Les trajets de chaque origine vers chaque destination.
/// </summary>
///
/// <remarks>
/// POURQUOI UNE MATRICE PLUTÔT QUE N APPELS. Le dispatch compare une vague de
/// chauffeurs à UN point d'enlèvement. Vingt appels séquentiels, c'est vingt
/// allers-retours pendant qu'un client regarde un écran d'attente ; un appel de
/// matrice, c'est une seule requête au moteur, qui la traite en une passe.
/// </remarks>
public sealed class RouteMatrix
{
    private RouteMatrix(
        IReadOnlyList<IReadOnlyList<MatrixCell>> rows,
        string provider,
        bool fromFallback,
        DateTimeOffset calculatedAt)
    {
        Rows = rows;
        Provider = provider;
        FromFallback = fromFallback;
        CalculatedAt = calculatedAt;
    }

    /// <summary>Une ligne par origine, dans l'ordre de la demande.</summary>
    public IReadOnlyList<IReadOnlyList<MatrixCell>> Rows { get; }

    public string Provider { get; }

    public bool FromFallback { get; }

    public DateTimeOffset CalculatedAt { get; }

    public static RouteMatrix Create(
        IReadOnlyList<IReadOnlyList<MatrixCell>> rows,
        string provider,
        DateTimeOffset calculatedAt,
        bool fromFallback = false)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (string.IsNullOrWhiteSpace(provider))
        {
            throw new DomainException(
                RoutingErrorCodes.InvalidRoute,
                "Une mesure dit toujours d'où elle vient.");
        }

        return new RouteMatrix(rows, provider.Trim(), fromFallback, calculatedAt);
    }
}
