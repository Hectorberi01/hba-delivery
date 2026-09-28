using Microsoft.Extensions.Configuration;

namespace Hba.BuildingBlocks.Persistence;

/// <summary>
/// Lecture d'une chaîne de connexion qui REFUSE DE DÉMARRER si elle est
/// incomplète.
/// </summary>
///
/// <remarks>
/// LE 28 SEPTEMBRE 2026, LE SERVICE PAYMENT A TOURNÉ DES HEURES AVEC UNE
/// CHAÎNE AMPUTÉE. Il répondait à /health, il acceptait les requêtes, et il
/// écrivait toutes les secondes « No password has been provided » dans un
/// journal que personne ne regardait. Le .env, lui, était correct : c'est la
/// variable qui n'était pas arrivée dans le conteneur.
///
/// CE QUI A RENDU LE DIAGNOSTIC LONG, ce n'est pas la panne — c'est qu'elle
/// s'exprimait LOIN de sa cause. L'erreur parlait d'authentification Postgres ;
/// la cause était une variable d'environnement. Entre les deux, une heure de
/// recherche dans le mauvais service.
///
/// UN SERVICE QUI NE PEUT PAS JOINDRE SA BASE N'A RIEN A FAIRE DEBOUT. Le dépôt
/// applique déjà ce principe ailleurs : la forme « :? » de Compose refuse de
/// démarrer sans variable, et JwtSigning refuse une clé éphémère hors
/// développement. Ici, la vérification a lieu à la construction du conteneur
/// d'injection — donc au démarrage, avant la première requête.
///
/// LE MESSAGE NE CONTIENT JAMAIS LA CHAINE, qui porte le mot de passe. Il nomme
/// les champs manquants et la variable à corriger : de quoi réparer sans rien
/// divulguer dans un journal.
///
/// PAS DE DEPENDANCE A NPGSQL ICI. Ce bloc partagé ne connaît qu'EF Core ; y
/// faire entrer le pilote PostgreSQL pour lire quatre clés coûterait plus que
/// la dizaine de lignes d'analyse ci-dessous.
/// </remarks>
public static class ChaineDeConnexion
{
    /// <summary>Champs sans lesquels aucune connexion ne peut aboutir.</summary>
    private static readonly string[] Requis = ["host", "database", "username", "password"];

    /// <summary>« User ID » et « Username » désignent la même chose chez Npgsql.</summary>
    private static readonly Dictionary<string, string> Synonymes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["user id"] = "username",
            ["userid"] = "username",
            ["user"] = "username",
            ["server"] = "host",
            ["pwd"] = "password",
            ["db"] = "database",
        };

    public static string Obligatoire(this IConfiguration configuration, string nom)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(nom);

        var valeur = configuration.GetConnectionString(nom);

        if (string.IsNullOrWhiteSpace(valeur))
        {
            throw new InvalidOperationException(
                $"La chaîne de connexion « {nom} » est absente ou vide. "
                + "Vérifiez le .env du service et que le conteneur a bien été recréé "
                + "(le .env n'est lu qu'à la CRÉATION du conteneur : "
                + "docker compose up -d --force-recreate).");
        }

        var champs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var morceau in valeur.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var coupure = morceau.IndexOf('=', StringComparison.Ordinal);

            if (coupure <= 0 || coupure == morceau.Length - 1)
            {
                continue;
            }

            var cle = morceau[..coupure].Trim();
            var contenu = morceau[(coupure + 1)..].Trim();

            if (contenu.Length == 0)
            {
                continue;
            }

            champs.Add(Synonymes.TryGetValue(cle, out var canonique) ? canonique : cle);
        }

        var manquants = Requis.Where(r => !champs.Contains(r)).ToList();

        if (manquants.Count == 0)
        {
            return valeur;
        }

        throw new InvalidOperationException(
            $"La chaîne de connexion « {nom} » est incomplète : "
            + $"il manque {string.Join(", ", manquants)}. "
            + "Le service refuse de démarrer plutôt que d'échouer à chaque requête. "
            + "Si le .env du service est correct, le conteneur tourne sur un "
            + "environnement périmé : docker compose up -d --force-recreate.");
    }
}
