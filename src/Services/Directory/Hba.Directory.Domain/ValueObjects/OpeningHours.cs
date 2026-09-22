using System.Globalization;
using Hba.BuildingBlocks.Domain;

namespace Hba.Directory.Domain.ValueObjects;

/// <summary>
/// Plage d'ouverture d'un jour, en minutes depuis minuit, heure locale de
/// Cotonou. Un jour sans plage est un jour fermé ; deux plages le même jour
/// décrivent une coupure de midi.
/// </summary>
public sealed class OpeningHours : ValueObject
{
    private const int MinutesPerDay = 24 * 60;

    private OpeningHours(DayOfWeek day, int opensAtMinutes, int closesAtMinutes)
    {
        Day = day;
        OpensAtMinutes = opensAtMinutes;
        ClosesAtMinutes = closesAtMinutes;
    }

    public DayOfWeek Day { get; }

    public int OpensAtMinutes { get; }

    public int ClosesAtMinutes { get; }

    /// <summary>1 = lundi … 7 = dimanche, comme dans le contrat.</summary>
    public int IsoDay => Day == DayOfWeek.Sunday ? 7 : (int)Day;

    public static OpeningHours Create(int isoDay, int opensAtMinutes, int closesAtMinutes)
    {
        if (isoDay is < 1 or > 7)
        {
            throw new DomainException("INVALID_DAY", $"Jour hors bornes : {isoDay}. Attendu 1 (lundi) à 7 (dimanche).");
        }

        if (opensAtMinutes is < 0 or > MinutesPerDay || closesAtMinutes is < 0 or > MinutesPerDay)
        {
            throw new DomainException("INVALID_HOURS", "Les horaires doivent tenir dans la journée.");
        }

        if (closesAtMinutes <= opensAtMinutes)
        {
            throw new DomainException(
                "INVALID_HOURS",
                "L'heure de fermeture doit être postérieure à l'heure d'ouverture.");
        }

        var day = isoDay == 7 ? DayOfWeek.Sunday : (DayOfWeek)isoDay;

        return new OpeningHours(day, opensAtMinutes, closesAtMinutes);
    }

    public bool Contains(int minutesOfDay) => minutesOfDay >= OpensAtMinutes && minutesOfDay < ClosesAtMinutes;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Day;
        yield return OpensAtMinutes;
        yield return ClosesAtMinutes;
    }

    public override string ToString()
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{IsoDay} {OpensAtMinutes / 60:D2}:{OpensAtMinutes % 60:D2}-{ClosesAtMinutes / 60:D2}:{ClosesAtMinutes % 60:D2}");
}
