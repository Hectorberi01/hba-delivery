using Hba.Gateway.Endpoints.Relais;

namespace Hba.Gateway.Endpoints.Driver;

/// <summary>
/// Relaie un envoi de fichier vers le service Driver.
///
/// LA MECANIQUE EST DANS RelaisDeDepot, partagée avec le relais vers Media.
/// Ce qui reste ici, c'est la seule chose qui soit propre à Driver : le chemin
/// de sa route interne, et le fait que l'identifiant du livreur s'y lise dans
/// le jeton.
/// </summary>
public interface IDriverUploadRelay
{
    Task<IResult> RelayerAsync(
        HttpRequest requete,
        HttpContext contexte,
        string cheminRelatif,
        CancellationToken cancellationToken);
}

public sealed class DriverUploadRelay(HttpClient client, ILogger<DriverUploadRelay> journal)
    : IDriverUploadRelay
{
    public async Task<IResult> RelayerAsync(
        HttpRequest requete,
        HttpContext contexte,
        string cheminRelatif,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requete);
        ArgumentNullException.ThrowIfNull(contexte);

        var (fichier, refus) = await RelaisDeDepot.LireAsync(requete, cancellationToken).ConfigureAwait(false);
        if (refus is not null)
        {
            return refus;
        }

        var driverId = DriverEndpoints.DriverIdOf(contexte);

        var reponse = await RelaisDeDepot.EnvoyerAsync(
            client,
            contexte,
            $"/internal/v1/drivers/{driverId}/{cheminRelatif}",
            fichier!,
            cancellationToken).ConfigureAwait(false);

        if (!reponse.EstUnSucces)
        {
            // LE CODE ET LE CORPS DU SERVICE SONT RENDUS TELS QUELS. Les
            // remplacer par un 500 générique effacerait « votre pièce dépasse
            // 5 Mo », qui est exactement ce que le livreur doit lire.
            journal.LogWarning(
                "Depot de piece refuse par Driver : {Statut} sur {Chemin}.",
                reponse.Statut,
                cheminRelatif);

            return Results.Content(reponse.Corps, "application/json", statusCode: reponse.Statut);
        }

        return Results.Content(reponse.Corps, "application/json");
    }
}
