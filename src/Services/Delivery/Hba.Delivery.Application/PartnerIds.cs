namespace Hba.Delivery.Application;

/// <summary>
/// Toute livraison porte un PartnerId, y compris celles créées depuis l'app
/// client ou le portail commerçant : le cloisonnement doit être uniforme, sinon
/// il faut un cas particulier dans chaque requête.
/// </summary>
public static class PartnerIds
{
    /// <summary>Livraisons créées par HBA pour son propre compte.</summary>
    public const string Internal = "hba-internal";
}
