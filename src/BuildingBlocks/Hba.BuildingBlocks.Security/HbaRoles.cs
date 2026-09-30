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

    /// <summary>
    /// UN SERVICE, PAS UNE PERSONNE. Ajouté au référentiel le 30 septembre 2026.
    /// </summary>
    ///
    /// <remarks>
    /// POURQUOI IL A FALLU L'AJOUTER. Deux appels du système partent d'un
    /// consommateur Kafka, donc sans personne derrière : Notification demande à
    /// Directory l'adresse d'un client pour lui envoyer son reçu, et Directory
    /// demande à Media d'effacer la photo d'un compte supprimé. Les deux
    /// services appelés sont en [Authorize] ; le jeton de service, lui, ne
    /// portait AUCUN rôle — ce qui était présenté comme la garantie principale.
    /// Résultat : les deux appels revenaient « non authentifié », l'erreur était
    /// avalée en avertissement, et le reçu était consigné « le compte n'a pas de
    /// courriel » — un motif faux. Aucun reçu ne partait, aucune photo n'était
    /// effacée.
    ///
    /// CE RÔLE N'EST PAS UN PASSE-PARTOUT, ET IL NE DOIT JAMAIS LE DEVENIR. Il
    /// n'est PAS dans BackOffice, il n'ouvre aucune écriture, et chaque service
    /// appelé décide explicitement des lectures qu'il lui accorde — aujourd'hui
    /// deux : lire une fiche client par son identifiant, et effacer les médias
    /// d'un propriétaire. Toute autre règle métier continue de le refuser, parce
    /// qu'elle demande QUI est l'appelant, et un service n'est personne.
    ///
    /// SA PORTÉE EST BORNÉE PAR QUI DÉTIENT UN SECRET. Les identifiants de
    /// service viennent de la configuration de l'hôte, pas d'une table : on ne
    /// devient pas un service en s'inscrivant.
    /// </remarks>
    public const string Service = "service";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Customer, Driver, MerchantOwner, MerchantStaff, Partner, Admin, Ops, Support, Finance, Service,
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

    // CES TROIS-LA SONT DANS LE JETON DEPUIS LE DEBUT, mais rien ne les lisait.
    // JwtTokenIssuer les pose (« name », « phone », « email ») ; les nommer ici
    // evite qu'un service les cherche sous un autre nom et trouve vide.
    public const string Name = "name";
    public const string Phone = "phone";
    public const string Email = "email";
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

    /// <summary>
    /// Annuaire des clients : admin, ops et support — PAS finance.
    ///
    /// UNE POLITIQUE A PART, ET NON BackOffice. Rattacher un paiement a un
    /// client se fait par identifiant ; parcourir un annuaire de personnes et
    /// lire des adresses de domicile n'entre pas dans le metier de finance.
    /// Reutiliser BackOffice aurait ouvert la porte sans que personne ne
    /// l'ait decide. Le service reverifie de toute facon : cette politique
    /// evite seulement qu'un appel manifestement hors perimetre traverse la
    /// passerelle.
    /// </summary>
    public const string AnnuaireClients = "policy:annuaire-clients";
}
