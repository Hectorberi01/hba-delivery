namespace Hba.Ride.Domain.Results;

/// <summary>Un refus métier : un code stable, un message lisible.</summary>
///
/// <remarks>
/// LE CODE EST LE CONTRAT, LE MESSAGE EST POUR L'HUMAIN. L'application cliente
/// branche sur le code — elle doit distinguer « trop tard » de « pas votre
/// course » — et n'affiche jamais le message tel quel : il est écrit pour celui
/// qui lit un journal, pas pour un passager.
/// </remarks>
public sealed record Error(string Code, string Message)
{
    public static Error None { get; } = new(string.Empty, string.Empty);
}

/// <summary>
/// Le résultat d'une opération métier : ce qu'elle a produit, ou pourquoi elle
/// a refusé.
/// </summary>
///
/// <remarks>
/// POURQUOI CE TYPE EXISTE ICI, ET NULLE PART AILLEURS DANS LE DÉPÔT. Les dix
/// autres services lèvent une <c>DomainException</c> portant un code, traduite
/// en frontière par <c>ExceptionInterceptor</c> et <c>TraduireLesRefus</c>. Ride
/// utilise <c>Result</c> : c'est une décision explicite du 1er octobre 2026,
/// inscrite au point 35.
///
/// CETTE COHABITATION NE TIENT QU'À UNE CONDITION, ET ELLE EST STRICTE : LE
/// DEHORS NE DOIT RIEN VOIR DE LA DIFFÉRENCE. Un refus de Ride produit
/// exactement le même code, le même statut HTTP et le même statut gRPC qu'un
/// refus de Delivery. La frontière de Ride traduit donc chaque <c>Error</c> en
/// refus, avec les mêmes règles que le reste du dépôt.
///
/// LA LIGNE DE PARTAGE EST LE PROJET, PAS LE GOÛT DU MOMENT. Dans
/// <c>Hba.Ride.Domain</c> et <c>Hba.Ride.Application</c>, le flux métier normal
/// rend un <c>Result</c> et ne lève pas. Partout ailleurs, l'exception reste la
/// convention. On sait où l'on est en lisant le nom du projet.
///
/// CE QUI RESTE UNE EXCEPTION, MÊME ICI : ce qui n'est pas du flux métier — un
/// argument nul, un invariant violé, un état que le code n'aurait pas dû
/// atteindre. <c>Result</c> sert à dire « non, et voici pourquoi » à un
/// utilisateur ; il ne sert pas à avaler un bug.
/// </remarks>
public class Result
{
    protected Result(bool succeeded, Error error)
    {
        if (succeeded && error != Error.None)
        {
            throw new InvalidOperationException("Un succès ne porte pas d'erreur.");
        }

        if (!succeeded && error == Error.None)
        {
            throw new InvalidOperationException("Un refus doit dire pourquoi.");
        }

        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }

    public bool Failed => !Succeeded;

    public Error Error { get; }

    public static Result Ok() => new(true, Error.None);

    public static Result Refus(string code, string message) => new(false, new Error(code, message));

    public static Result<TValue> Ok<TValue>(TValue value) => new(value, true, Error.None);

    public static Result<TValue> Refus<TValue>(string code, string message)
        => new(default, false, new Error(code, message));
}

/// <summary>Un résultat qui porte une valeur quand il réussit.</summary>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool succeeded, Error error)
        : base(succeeded, error) => _value = value;

    /// <summary>
    /// La valeur produite.
    ///
    /// LIRE LA VALEUR D'UN REFUS EST UN BUG, PAS UN CAS MÉTIER : d'où
    /// l'exception. Un appelant qui n'a pas regardé <c>Succeeded</c> a sauté une
    /// étape, et lui rendre un défaut silencieux propagerait la faute plus loin.
    /// </summary>
    public TValue Value => Succeeded
        ? _value!
        : throw new InvalidOperationException("Un refus ne porte pas de valeur.");
}
