namespace Hba.BuildingBlocks.Domain;

/// <summary>
/// Violation d'une règle métier. Porte un code stable, exploité par les BFF et
/// les applications pour afficher un message : jamais un texte libre.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string code, string message) : base(message) => Code = code;

    public DomainException(string code, string message, Exception innerException)
        : base(message, innerException) => Code = code;

    public DomainException() : base() => Code = "DOMAIN_ERROR";

    public DomainException(string message) : base(message) => Code = "DOMAIN_ERROR";

    public DomainException(string message, Exception innerException)
        : base(message, innerException) => Code = "DOMAIN_ERROR";

    public string Code { get; }
}

/// <summary>Transition d'état interdite par la table des transitions.</summary>
public sealed class InvalidStateTransitionException : DomainException
{
    public InvalidStateTransitionException(string aggregate, string from, string to, ActorKind actor)
        : base(
            "INVALID_STATE_TRANSITION",
            $"Transition interdite sur {aggregate} : {from} -> {to} par {actor}.")
    {
        Aggregate = aggregate;
        From = from;
        To = to;
        ActorKind = actor;
    }

    public InvalidStateTransitionException() : base("INVALID_STATE_TRANSITION", "Transition interdite.")
    {
        Aggregate = string.Empty;
        From = string.Empty;
        To = string.Empty;
    }

    public InvalidStateTransitionException(string message) : base("INVALID_STATE_TRANSITION", message)
    {
        Aggregate = string.Empty;
        From = string.Empty;
        To = string.Empty;
    }

    public InvalidStateTransitionException(string message, Exception innerException)
        : base("INVALID_STATE_TRANSITION", message, innerException)
    {
        Aggregate = string.Empty;
        From = string.Empty;
        To = string.Empty;
    }

    public string Aggregate { get; }

    public string From { get; }

    public string To { get; }

    public ActorKind ActorKind { get; }
}

/// <summary>L'appelant n'a pas le droit d'agir sur cette ressource.</summary>
public sealed class ForbiddenException : DomainException
{
    public ForbiddenException(string message) : base("FORBIDDEN", message)
    {
    }

    public ForbiddenException() : base("FORBIDDEN", "Action interdite pour cet acteur.")
    {
    }

    public ForbiddenException(string message, Exception innerException)
        : base("FORBIDDEN", message, innerException)
    {
    }
}

/// <summary>Ressource inexistante, ou hors du périmètre de l'appelant.</summary>
public sealed class NotFoundException : DomainException
{
    public NotFoundException(string resource, string id)
        : base("NOT_FOUND", $"{resource} introuvable : {id}.")
    {
    }

    public NotFoundException() : base("NOT_FOUND", "Ressource introuvable.")
    {
    }

    public NotFoundException(string message) : base("NOT_FOUND", message)
    {
    }

    public NotFoundException(string message, Exception innerException)
        : base("NOT_FOUND", message, innerException)
    {
    }
}
