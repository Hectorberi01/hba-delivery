namespace Hba.Directory.Application.Views;

/// <summary>
/// Une ligne de l'annuaire.
///
/// LE TELEPHONE Y EST TOUJOURS MASQUE, quel que soit le role — c'est la seule
/// difference entre cette vue et la fiche. Parcourir une liste ne doit pas
/// permettre de MOISSONNER des numeros ; ouvrir une fiche, une par une, si.
///
/// CE N'EST PAS UNE PROTECTION CONTRE LA RECHERCHE : qui connait deja un
/// numero le retrouve, puisque la recherche porte dessus. Ce que le masque
/// empeche, c'est de repartir de la liste avec l'annuaire entier.
/// </summary>
public sealed record CustomerDirectoryRowView(
    Guid Id,
    string DisplayName,
    string PhoneMasked,
    DateTimeOffset CreatedAt);

public sealed record CustomerDirectoryPage(
    IReadOnlyList<CustomerDirectoryRowView> Customers,
    int Total);

/// <summary>
/// La fiche, telle que l'appelant a le droit de la voir.
///
/// LES DRAPEAUX « MASQUE » NE SONT PAS DU CONFORT. Sans eux, une fiche sans
/// adresse se lit comme un client qui n'en a enregistre aucune, et l'operateur
/// en tire une conclusion fausse.
/// </summary>
public sealed record CustomerFileView(
    Guid Id,
    string DisplayName,
    string Phone,
    string? Email,
    bool EmailMasque,
    IReadOnlyList<FavoriteAddressView> FavoriteAddresses,
    bool AdressesMasquees,
    DateTimeOffset CreatedAt);

public static class MasqueTelephone
{
    /// <summary>
    /// Ne garde que les quatre derniers chiffres.
    ///
    /// QUATRE, PARCE QU'ILS SUFFISENT A RECONNAITRE et pas a composer. C'est
    /// ce qu'un operateur compare au numero qu'un client vient de lui donner
    /// au telephone.
    /// </summary>
    public static string Appliquer(string telephone)
    {
        if (string.IsNullOrWhiteSpace(telephone))
        {
            return string.Empty;
        }

        var net = telephone.Trim();

        return net.Length <= 4 ? new string('•', net.Length) : $"••••{net[^4..]}";
    }
}
