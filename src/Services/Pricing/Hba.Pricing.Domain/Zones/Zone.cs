using Hba.BuildingBlocks.Domain;

namespace Hba.Pricing.Domain.Zones;

/// <summary>
/// Zone tarifaire. Le référentiel confie à ops et admin la gestion des zones
/// (polygones PostGIS) et des grilles.
///
/// LE POLYGONE N'EST PAS ICI. Il vit en base, dans une colonne geography, et
/// seule la requête de recherche le manipule (ST_Contains). Le domaine n'a
/// aucune raison de faire de la géométrie, et cela lui évite une dépendance
/// externe que les tests d'architecture interdisent.
/// </summary>
public sealed class Zone : AggregateRoot
{
    private Zone()
    {
    }

    private Zone(Guid id, string code, string name, int priority, DateTimeOffset createdAt)
        : base(id)
    {
        Code = code;
        Name = name;
        Priority = priority;
        IsActive = true;
        CreatedAt = createdAt;
    }

    /// <summary>Identifiant lisible, repris tel quel dans le devis : « cotonou-centre ».</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Départage les zones qui se chevauchent : la plus grande priorité gagne.
    /// Sans cela, un point situé dans deux polygones donnerait un prix qui
    /// dépend de l'ordre de lecture en base.
    /// </summary>
    public int Priority { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Zone Create(Guid id, string code, string name, int priority, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("MISSING_ZONE_CODE", "Une zone doit porter un code.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("MISSING_ZONE_NAME", "Une zone doit porter un nom.");
        }

        return new Zone(id, code.Trim().ToLowerInvariant(), name.Trim(), priority, createdAt);
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("MISSING_ZONE_NAME", "Une zone doit porter un nom.");
        }

        Name = name.Trim();
    }

    /// <summary>
    /// Change la priorité qui départage les chevauchements.
    /// </summary>
    ///
    /// <remarks>
    /// ELLE MANQUAIT, ET SON ABSENCE SE SERAIT VUE AU PREMIER REDESSIN. Une zone
    /// pouvait naître avec sa priorité et ne plus jamais en changer : découper une
    /// ville se fait par essais — on dessine un centre, puis on s'aperçoit qu'il
    /// doit passer devant la périphérie qui l'englobe.
    ///
    /// AUCUNE VALEUR N'EST INTERDITE, pas même négative : « passe après tout le
    /// reste » est une intention légitime, et le départage ne demande qu'un ordre.
    /// </remarks>
    public void Reprioritise(int priority) => Priority = priority;

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
