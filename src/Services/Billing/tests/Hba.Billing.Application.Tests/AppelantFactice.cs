using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;

namespace Hba.Billing.Application.Tests;

/// <summary>
/// Un contexte d'appel écrit à la main, pour décrire un jeton précis.
/// </summary>
///
/// <remarks>
/// PAS DE SIMULACRE DE BIBLIOTHÈQUE ICI. Ce qu'on teste est une matrice de
/// rôles et de claims : une classe de dix lignes la dit plus clairement que
/// trois appels de configuration, et elle rend les cas limites — un rôle sans
/// son claim — aussi faciles à écrire que les cas normaux.
/// </remarks>
internal sealed class AppelantFactice : ICallerContext
{
    public static AppelantFactice Commercant(string merchantId, string sujet = "u-1")
        => new() { SubjectId = sujet, Roles = Ensemble(HbaRoles.MerchantOwner), MerchantId = merchantId };

    public static AppelantFactice Employe(string merchantId, string sujet = "u-2")
        => new() { SubjectId = sujet, Roles = Ensemble(HbaRoles.MerchantStaff), MerchantId = merchantId };

    public static AppelantFactice Partenaire(string partnerId, string sujet = "u-3")
        => new() { SubjectId = sujet, Roles = Ensemble(HbaRoles.Partner), PartnerId = partnerId };

    public static AppelantFactice Client(string sujet = "u-4")
        => new() { SubjectId = sujet, Roles = Ensemble(HbaRoles.Customer) };

    public static AppelantFactice Livreur(string driverId = "d-1", string sujet = "u-5")
        => new() { SubjectId = sujet, Roles = Ensemble(HbaRoles.Driver), DriverId = driverId };

    public static AppelantFactice BackOffice(string role, string sujet = "u-6")
        => new() { SubjectId = sujet, Roles = Ensemble(role) };

    /// <summary>
    /// Un autre service du systeme. LE SUJET PORTE LE PREFIXE « service: »,
    /// comme le pose IssueServiceTokenHandler : le reecrire ici sans prefixe
    /// ferait passer un test que la production ne passerait pas.
    /// </summary>
    public static AppelantFactice Systeme(string nom = "delivery")
        => new() { SubjectId = $"service:{nom}", Roles = Ensemble(HbaRoles.Service) };

    public static AppelantFactice Anonyme()
        => new() { IsAuthenticated = false, Roles = Ensemble() };

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
