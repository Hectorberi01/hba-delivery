namespace Hba.BuildingBlocks.Security;

/// <summary>
/// Rôles du référentiel acteurs. Ce sont exactement les valeurs portées par le
/// claim "roles" du JWT. Aucun autre nom de rôle ne doit apparaître ailleurs.
/// </summary>
public static class HbaRoles
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
        Customer, Driver, MerchantOwner, MerchantStaff, Partner, Admin, Ops, Support, Finance,
    };

    /// <summary>Rôles du back-office interne.</summary>
    public static readonly IReadOnlySet<string> BackOffice = new HashSet<string>(StringComparer.Ordinal)
    {
        Admin, Ops, Support, Finance,
    };
}

/// <summary>Claims propres à HBA, en plus des claims standard.</summary>
public static class HbaClaims
{
    public const string Roles = "roles";
    public const string MerchantId = "merchant_id";
    public const string PartnerId = "partner_id";
    public const string DriverId = "driver_id";
}

/// <summary>Noms des politiques d'autorisation ASP.NET Core.</summary>
public static class HbaPolicies
{
    public const string Customer = "policy:customer";
    public const string Driver = "policy:driver";
    public const string Merchant = "policy:merchant";
    public const string Partner = "policy:partner";
    public const string BackOffice = "policy:back-office";
    public const string Admin = "policy:admin";
    public const string Finance = "policy:finance";
}
