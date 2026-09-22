namespace Hba.BuildingBlocks.Application.Abstractions;

/// <summary>
/// Horloge injectable. Le domaine ne lit jamais DateTimeOffset.UtcNow
/// directement : les tests de machine à états doivent pouvoir figer le temps.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
