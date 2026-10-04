using Hba.BuildingBlocks.Domain;

namespace Hba.Ride.Domain.ValueObjects;

/// <summary>
/// Ce qui a été annoncé au passager avant qu'il accepte : distance, durée, prix.
/// </summary>
///
/// <remarks>
/// LES TROIS VONT ENSEMBLE, ET C'EST POURQUOI ILS NE SONT PAS TROIS CHAMPS
/// SÉPARÉS SUR LA COURSE. Un prix sans la distance qui l'a produit est
/// invérifiable : le jour où un passager conteste, il faut pouvoir montrer sur
/// quoi le calcul reposait.
///
/// C'EST UN INSTANTANÉ, PAS UNE RÉFÉRENCE. L'ADR 0004 l'impose déjà pour les
/// colis : une grille tarifaire modifiée demain ne doit pas changer le prix
/// d'une course d'hier. La course garde donc la valeur, pas un lien vers la
/// grille.
/// </remarks>
public sealed class TripEstimate : ValueObject
{
    private TripEstimate()
    {
    }

    private TripEstimate(int distanceMeters, int durationSeconds, MoneyXof price)
    {
        DistanceMeters = distanceMeters;
        DurationSeconds = durationSeconds;
        Price = price;
    }

    public int DistanceMeters { get; private set; }

    public int DurationSeconds { get; private set; }

    public MoneyXof Price { get; private set; } = null!;

    public static bool EstValide(int distanceMeters, int durationSeconds, long price)
        => distanceMeters >= 0 && durationSeconds >= 0 && MoneyXof.EstValide(price);

    public static TripEstimate Create(int distanceMeters, int durationSeconds, MoneyXof price)
    {
        ArgumentNullException.ThrowIfNull(price);

        return EstValide(distanceMeters, durationSeconds, price.Amount)
            ? new TripEstimate(distanceMeters, durationSeconds, price)
            : throw new ArgumentOutOfRangeException(
                nameof(distanceMeters),
                "Distance, durée et prix d'une estimation sont positifs.");
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return DistanceMeters;
        yield return DurationSeconds;
        yield return Price;
    }
}
