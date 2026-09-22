namespace Hba.Identity.Domain;

/// <summary>
/// Rôles du référentiel acteurs. Ils sont redéclarés ici plutôt qu'importés de
/// Hba.BuildingBlocks.Security : ce paquet dépend d'ASP.NET Core, et le domaine
/// ne dépend d'aucune infrastructure. Un test vérifie que les deux listes
/// restent identiques.
/// </summary>
public static class Roles
{
    public const string Customer = "customer";
    public const string Driver = "driver";
    public const string MerchantOwner = "merchant_owner";
    public const string MerchantStaff = "merchant_staff";
    public const string Partner = "partner";
    public const string Admin = "admin";
    public const string Ops = "ops";
    public const string Support = "support";
    public const string Finance = "finance";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Customer,
        Driver,
        MerchantOwner,
        MerchantStaff,
        Partner,
        Admin,
        Ops,
        Support,
        Finance,
    };

    /// <summary>Rôles qu'une simple vérification de numéro peut créer.</summary>
    public static readonly IReadOnlySet<string> SelfServiceByOtp = new HashSet<string>(StringComparer.Ordinal)
    {
        Customer,
        Driver,
    };

    public static readonly IReadOnlySet<string> BackOffice = new HashSet<string>(StringComparer.Ordinal)
    {
        Admin,
        Ops,
        Support,
        Finance,
    };

    public static readonly IReadOnlySet<string> Merchant = new HashSet<string>(StringComparer.Ordinal)
    {
        MerchantOwner,
        MerchantStaff,
    };

    /// <summary>Rôles qui se connectent par mot de passe et non par OTP.</summary>
    public static readonly IReadOnlySet<string> PasswordBased = new HashSet<string>(StringComparer.Ordinal)
    {
        MerchantOwner,
        MerchantStaff,
        Admin,
        Ops,
        Support,
        Finance,
    };
}
