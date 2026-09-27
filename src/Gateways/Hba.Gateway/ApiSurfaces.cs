namespace Hba.Gateway;

/// <summary>
/// Les quatre surfaces publiques, et le prefixe d'URL qui les distingue.
///
/// LE PREFIXE N'EST PAS UNE CONVENTION DE CONFORT : c'est ce qui dit a quelle
/// application une route s'adresse, et donc quelle politique d'autorisation
/// s'applique. Il sert ici a decouper la documentation, mais il decrit la meme
/// frontiere que les groupes de routes.
/// </summary>
public sealed record ApiSurface(
    string Name,
    string Title,
    string Description,
    IReadOnlyList<string> Prefixes);

public static class ApiSurfaces
{
    public static readonly ApiSurface Client =
        new(
            "client",
            "HBA — application client",
            "Routes de l'application d'envoi de colis. Connexion par code SMS ou WhatsApp.",
            ["api/client/"]);

    public static readonly ApiSurface Driver =
        new(
            "driver",
            "HBA — application livreur",
            "Routes de l'application livreur : missions, position, preuve de remise.",
            ["api/driver/"]);

    /// <summary>
    /// Portail commercant ET back-office : deux publics, un meme navigateur,
    /// et des politiques d'autorisation distinctes portees par les groupes de
    /// routes.
    /// </summary>
    public static readonly ApiSurface Web =
        new(
            "web",
            "HBA — portail et back-office",
            "Deux publics, un meme navigateur : le portail commercant et le back-office. "
            + "Connexion par mot de passe, pas par code.",
            ["api/web/", "api/admin/", "api/merchant/"]);

    public static readonly ApiSurface Partner =
        new(
            "partner",
            "HBA — API partenaires",
            "API des systemes tiers. Authentification OAuth2 client credentials "
            + "sur /api/v1/oauth/token, pas de session ni de jeton de rafraichissement.",
            ["api/v1/"]);

    public static readonly IReadOnlyList<ApiSurface> All = [Client, Driver, Web, Partner];

    /// <summary>
    /// Surface d'une route, d'apres son chemin relatif. Une route qui ne
    /// correspond a aucun prefixe connu — /health, par exemple — ne figure dans
    /// aucune des quatre documentations.
    /// </summary>
    public static string? Resolve(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return null;
        }

        foreach (var surface in All)
        {
            foreach (var prefix in surface.Prefixes)
            {
                if (relativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return surface.Name;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Etiquette de regroupement d'une route : la ressource qu'elle touche.
    ///
    /// Le chemin est de la forme api/&lt;surface&gt;/v1/&lt;ressource&gt;/... ou
    /// api/v1/&lt;ressource&gt;/... pour les partenaires. On prend le premier
    /// segment qui suit la version, ce qui regroupe /auth/otp/request et
    /// /auth/otp/verify sous « auth ».
    /// </summary>
    public static string Tag(string? relativePath)
    {
        var segments = (relativePath ?? string.Empty).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var version = Array.FindIndex(segments, s => s.Length > 1 && s[0] == 'v' && char.IsDigit(s[1]));

        return version >= 0 && version + 1 < segments.Length
            ? segments[version + 1]
            : "divers";
    }
}
