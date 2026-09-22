using Hba.BuildingBlocks.Domain;
using Hba.Directory.Domain.ValueObjects;

namespace Hba.Directory.Domain.Merchants;

/// <summary>
/// Point de collecte d'un commerçant : l'endroit physique d'où part un colis.
/// Un commerçant peut en avoir plusieurs — une boutique et un entrepôt, deux
/// restaurants.
/// </summary>
public sealed class PickupPoint : Entity
{
    private readonly List<OpeningHours> _openingHours = [];

    private PickupPoint()
    {
    }

    private PickupPoint(Guid id, string name, Address address, DateTimeOffset createdAt) : base(id)
    {
        Name = name;
        Address = address;
        CreatedAt = createdAt;
        IsActive = true;
    }

    public string Name { get; private set; } = string.Empty;

    public Address Address { get; private set; } = null!;

    public IReadOnlyList<OpeningHours> OpeningHours => _openingHours;

    /// <summary>
    /// Forme persistée des horaires : « 1:480-1200;2:480-1200 », un jour ISO et
    /// deux minutes depuis minuit. Une table de plus pour au plus quatorze
    /// lignes par point de collecte ne se justifie pas, et ce format se lit
    /// directement en SQL. Propriété privée : EF Core seul s'en sert.
    /// </summary>
    private string OpeningHoursRaw
    {
        get => string.Join(
            ';',
            _openingHours.Select(h => string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{h.IsoDay}:{h.OpensAtMinutes}-{h.ClosesAtMinutes}")));
        set
        {
            _openingHours.Clear();

            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            foreach (var slot in value.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var dayAndRange = slot.Split(':', 2);
                var range = dayAndRange[1].Split('-', 2);

                _openingHours.Add(ValueObjects.OpeningHours.Create(
                    int.Parse(dayAndRange[0], System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(range[0], System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(range[1], System.Globalization.CultureInfo.InvariantCulture)));
            }
        }
    }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal static PickupPoint Create(
        string name,
        Address address,
        IEnumerable<OpeningHours> openingHours,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("MISSING_NAME", "Un point de collecte doit porter un nom.");
        }

        var point = new PickupPoint(Guid.CreateVersion7(), name.Trim(), address, createdAt);
        point.ReplaceOpeningHours(openingHours);

        return point;
    }

    internal void Update(string name, Address address, IEnumerable<OpeningHours> openingHours)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("MISSING_NAME", "Un point de collecte doit porter un nom.");
        }

        Name = name.Trim();
        Address = address;
        ReplaceOpeningHours(openingHours);
    }

    internal void SetActive(bool active) => IsActive = active;

    /// <summary>
    /// Ouvert à un instant donné, heure locale. Aucun horaire déclaré vaut
    /// « ouvert » : mieux vaut laisser passer une course que bloquer un
    /// commerçant qui n'a pas rempli ses horaires.
    /// </summary>
    public bool IsOpenAt(DayOfWeek day, int minutesOfDay)
    {
        if (_openingHours.Count == 0)
        {
            return true;
        }

        return _openingHours.Any(h => h.Day == day && h.Contains(minutesOfDay));
    }

    private void ReplaceOpeningHours(IEnumerable<OpeningHours> openingHours)
    {
        _openingHours.Clear();

        if (openingHours is null)
        {
            return;
        }

        foreach (var slot in openingHours.OrderBy(h => h.IsoDay).ThenBy(h => h.OpensAtMinutes))
        {
            if (_openingHours.Any(existing => existing.Day == slot.Day && Overlaps(existing, slot)))
            {
                throw new DomainException(
                    "OVERLAPPING_HOURS",
                    "Deux plages d'ouverture du même jour se chevauchent.");
            }

            _openingHours.Add(slot);
        }
    }

    private static bool Overlaps(OpeningHours left, OpeningHours right)
        => left.OpensAtMinutes < right.ClosesAtMinutes && right.OpensAtMinutes < left.ClosesAtMinutes;
}
