namespace Hba.BuildingBlocks.Domain;

/// <summary>
/// Nature de l'auteur d'une action. Les acteurs système en font partie : ils
/// déclenchent des transitions et doivent apparaître dans l'audit.
/// </summary>
public enum ActorKind
{
    Unknown = 0,
    Customer = 1,
    Driver = 2,
    Merchant = 3,
    Partner = 4,
    Admin = 5,

    /// <summary>Moteur de dispatch : vagues d'offres, expirations, NO_DRIVER_FOUND.</summary>
    Dispatch = 6,

    /// <summary>FedaPay, via webhook vérifié.</summary>
    PaymentProvider = 7,

    /// <summary>Planificateur : expirations de devis, timeouts, rapprochements.</summary>
    Scheduler = 8,
}

/// <summary>
/// Auteur d'une action, tel qu'il est enregistré dans l'audit.
/// </summary>
public sealed class Actor : ValueObject
{
    private Actor(ActorKind kind, string id, string? displayName)
    {
        Kind = kind;
        Id = id;
        DisplayName = displayName;
    }

    public ActorKind Kind { get; }

    /// <summary>Identifiant de l'acteur, ou nom du composant pour un acteur système.</summary>
    public string Id { get; }

    public string? DisplayName { get; }

    public bool IsHuman => Kind is ActorKind.Customer or ActorKind.Driver or ActorKind.Merchant or ActorKind.Admin;

    public static Actor Human(ActorKind kind, string id, string? displayName = null)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Un acteur doit porter un identifiant.", nameof(id));
        }

        return new Actor(kind, id, displayName);
    }

    public static Actor Customer(string id) => Human(ActorKind.Customer, id);

    public static Actor Driver(string id) => Human(ActorKind.Driver, id);

    public static Actor Merchant(string id) => Human(ActorKind.Merchant, id);

    public static Actor Partner(string partnerId) => Human(ActorKind.Partner, partnerId);

    public static Actor Admin(string id) => Human(ActorKind.Admin, id);

    public static readonly Actor DispatchEngine = new(ActorKind.Dispatch, "dispatch", "Moteur de dispatch");

    public static readonly Actor FedaPay = new(ActorKind.PaymentProvider, "fedapay", "FedaPay");

    public static readonly Actor Scheduler = new(ActorKind.Scheduler, "scheduler", "Planificateur");

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Kind;
        yield return Id;
    }

    public override string ToString() => $"{Kind}:{Id}";
}
