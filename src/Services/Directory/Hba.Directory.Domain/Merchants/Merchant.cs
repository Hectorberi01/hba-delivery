using Hba.BuildingBlocks.Domain;
using Hba.Directory.Domain.Merchants.Events;
using Hba.Directory.Domain.ValueObjects;

namespace Hba.Directory.Domain.Merchants;

/// <summary>
/// Une ENTREPRISE — boutique, restaurant, vendeur HBA Express — dont les colis
/// partent d'un ou plusieurs points de collecte.
///
/// À ne pas confondre avec un partenaire B2B, qui est un système et vit dans
/// Identity : un commerçant a des employés, un partenaire a un client OAuth.
/// </summary>
public sealed class Merchant : AggregateRoot
{
    private readonly List<PickupPoint> _pickupPoints = [];

    private Merchant()
    {
    }

    private Merchant(
        Guid id,
        string legalName,
        string contactName,
        string contactPhone,
        string? contactEmail,
        int averagePreparationMinutes,
        DateTimeOffset createdAt) : base(id)
    {
        LegalName = legalName;
        ContactName = contactName;
        ContactPhone = contactPhone;
        ContactEmail = contactEmail;
        AveragePreparationMinutes = averagePreparationMinutes;
        CreatedAt = createdAt;
        IsActive = true;
    }

    public string LegalName { get; private set; } = string.Empty;

    public string ContactName { get; private set; } = string.Empty;

    public string ContactPhone { get; private set; } = string.Empty;

    public string? ContactEmail { get; private set; }

    /// <summary>
    /// Temps de préparation moyen. Dispatch s'en sert pour ne pas envoyer un
    /// livreur attendre vingt minutes devant un restaurant.
    /// </summary>
    public int AveragePreparationMinutes { get; private set; }

    public IReadOnlyList<PickupPoint> PickupPoints => _pickupPoints;

    public bool IsActive { get; private set; }

    public string? DeactivationReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Merchant Create(
        string legalName,
        string contactName,
        string contactPhone,
        string? contactEmail,
        int averagePreparationMinutes,
        Actor actor,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (string.IsNullOrWhiteSpace(legalName))
        {
            throw new DomainException("MISSING_LEGAL_NAME", "La raison sociale est obligatoire.");
        }

        if (!PhoneNumber.IsValid(contactPhone))
        {
            throw new DomainException("INVALID_PHONE", "Le commerçant doit être joignable.");
        }

        if (averagePreparationMinutes is < 0 or > 240)
        {
            throw new DomainException(
                "INVALID_PREPARATION_TIME",
                "Le temps de préparation moyen doit tenir entre 0 et 240 minutes.");
        }

        var merchant = new Merchant(
            Guid.CreateVersion7(),
            legalName.Trim(),
            string.IsNullOrWhiteSpace(contactName) ? legalName.Trim() : contactName.Trim(),
            PhoneNumber.Normalize(contactPhone),
            string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim().ToLowerInvariant(),
            averagePreparationMinutes,
            createdAt);

        merchant.Raise(new MerchantCreated(merchant.Id, merchant.LegalName, actor, createdAt));

        return merchant;
    }

    public void Update(
        string? legalName,
        string? contactName,
        string? contactPhone,
        string? contactEmail,
        int? averagePreparationMinutes,
        Actor actor,
        DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(legalName))
        {
            LegalName = legalName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(contactName))
        {
            ContactName = contactName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(contactPhone))
        {
            if (!PhoneNumber.IsValid(contactPhone))
            {
                throw new DomainException("INVALID_PHONE", "Le commerçant doit rester joignable.");
            }

            ContactPhone = PhoneNumber.Normalize(contactPhone);
        }

        if (contactEmail is not null)
        {
            ContactEmail = string.IsNullOrWhiteSpace(contactEmail) ? null : contactEmail.Trim().ToLowerInvariant();
        }

        if (averagePreparationMinutes is not null)
        {
            if (averagePreparationMinutes is < 0 or > 240)
            {
                throw new DomainException(
                    "INVALID_PREPARATION_TIME",
                    "Le temps de préparation moyen doit tenir entre 0 et 240 minutes.");
            }

            AveragePreparationMinutes = averagePreparationMinutes.Value;
        }

        Raise(new MerchantUpdated(Id, actor, now));
    }

    public PickupPoint AddPickupPoint(
        string name,
        Address address,
        IEnumerable<OpeningHours> openingHours,
        Actor actor,
        DateTimeOffset now)
    {
        EnsureActive();

        var point = PickupPoint.Create(name, address, openingHours, now);
        _pickupPoints.Add(point);

        Raise(new PickupPointChanged(Id, point.Id, point.Address, point.IsActive, actor, now));

        return point;
    }

    public void UpdatePickupPoint(
        Guid pickupPointId,
        string name,
        Address address,
        IEnumerable<OpeningHours> openingHours,
        Actor actor,
        DateTimeOffset now)
    {
        EnsureActive();

        var point = FindPickupPoint(pickupPointId);
        point.Update(name, address, openingHours);

        Raise(new PickupPointChanged(Id, point.Id, point.Address, point.IsActive, actor, now));
    }

    public void SetPickupPointActive(Guid pickupPointId, bool active, Actor actor, DateTimeOffset now)
    {
        var point = FindPickupPoint(pickupPointId);

        if (point.IsActive == active)
        {
            return;
        }

        if (!active && _pickupPoints.Count(p => p.IsActive) == 1)
        {
            throw new DomainException(
                "LAST_PICKUP_POINT",
                "Un commerçant actif doit garder au moins un point de collecte ouvert. "
                + "Désactivez le commerçant plutôt que son dernier point.");
        }

        point.SetActive(active);

        Raise(new PickupPointChanged(Id, point.Id, point.Address, active, actor, now));
    }

    public void Deactivate(string reason, Actor actor, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        DeactivationReason = reason;

        foreach (var point in _pickupPoints)
        {
            point.SetActive(false);
        }

        Raise(new MerchantDeactivated(Id, reason, actor, now));
    }

    public PickupPoint FindPickupPoint(Guid pickupPointId)
        => _pickupPoints.FirstOrDefault(p => p.Id == pickupPointId)
           ?? throw new NotFoundException("Point de collecte", pickupPointId.ToString());

    private void EnsureActive()
    {
        if (!IsActive)
        {
            throw new ForbiddenException("Ce commerçant est désactivé.");
        }
    }
}
