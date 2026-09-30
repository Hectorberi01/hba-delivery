using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;

namespace Hba.Media.Application.Tests;

/// <summary>
/// Un contexte d'appel écrit à la main, pour décrire un jeton précis.
/// </summary>
///
/// <remarks>
/// MEME FORME QUE CELUI DE BILLING, A DESSEIN : ce qui se teste ici est une
/// matrice de rôles et de claims, et deux matrices du même genre gagnent à se
/// lire de la même façon. Les cas limites — un rôle sans le claim qui va avec —
/// s'écrivent aussi facilement que les cas normaux.
/// </remarks>
internal sealed class AppelantFactice : ICallerContext
{
    public static AppelantFactice Client(string sujet)
        => new() { SubjectId = sujet, Roles = Ensemble(HbaRoles.Customer) };

    public static AppelantFactice Livreur(string sujet)
        => new() { SubjectId = sujet, Roles = Ensemble(HbaRoles.Driver), DriverId = sujet };

    public static AppelantFactice Commercant(string merchantId, string sujet = "u-m")
        => new() { SubjectId = sujet, Roles = Ensemble(HbaRoles.MerchantOwner), MerchantId = merchantId };

    public static AppelantFactice Employe(string merchantId, string sujet = "u-e")
        => new() { SubjectId = sujet, Roles = Ensemble(HbaRoles.MerchantStaff), MerchantId = merchantId };

    public static AppelantFactice BackOffice(string role)
        => new() { SubjectId = "u-bo", Roles = Ensemble(role) };

    /// <summary>
    /// Un autre service. LE SUJET PORTE LE PREFIXE « service: », comme le pose
    /// IssueServiceTokenHandler.
    /// </summary>
    public static AppelantFactice Systeme(string nom = "directory")
        => new() { SubjectId = $"service:{nom}", Roles = Ensemble(HbaRoles.Service) };

    public static AppelantFactice Anonyme()
        => new() { IsAuthenticated = false, Roles = Ensemble() };

    /// <summary>Un jeton sans aucun rôle : authentifié, et rien de plus.</summary>
    public static AppelantFactice SansRole(string sujet = "u-vide")
        => new() { SubjectId = sujet, Roles = Ensemble() };

    public bool IsAuthenticated { get; private init; } = true;

    public string SubjectId { get; private init; } = string.Empty;

    public IReadOnlySet<string> Roles { get; private init; } = Ensemble();

    public string? MerchantId { get; private init; }

    public string? PartnerId { get; private init; }

    public string? DriverId { get; private init; }

    public string? DisplayName => null;

    public string? Phone => null;

    public string? Email => null;

    public string? TraceId => null;

    public string? CorrelationId => null;

    public bool IsInRole(string role) => Roles.Contains(role);

    public Actor ToActor() => Actor.Admin(SubjectId);

    /// <summary>Le jeton porte le rôle mais pas le claim qui va avec.</summary>
    public AppelantFactice SansIdentifiant()
        => new() { SubjectId = SubjectId, Roles = Roles };

    private static IReadOnlySet<string> Ensemble(params string[] roles)
        => new HashSet<string>(roles, StringComparer.Ordinal);
}
