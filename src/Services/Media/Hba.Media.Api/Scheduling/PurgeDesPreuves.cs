using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Storage;
using Hba.Media.Application.Assets;
using Hba.Media.Domain.Assets;
using Microsoft.Extensions.Options;

namespace Hba.Media.Api.Scheduling;

/// <summary>
/// Réglages de la rétention des preuves de livraison.
/// </summary>
public sealed class PreuveRetentionOptions
{
    public const string Section = "ProofRetention";

    /// <summary>
    /// Durée de conservation, en jours. Trente par décision du 30 septembre 2026.
    /// </summary>
    public int Jours { get; set; } = 30;

    /// <summary>Période de balayage, en heures.</summary>
    public int BalayageHeures { get; set; } = 6;

    /// <summary>Nombre de médias traités par passage.</summary>
    public int Lot { get; set; } = 200;
}

/// <summary>
/// Efface les preuves de livraison passé leur durée de conservation.
/// </summary>
///
/// <remarks>
/// ELLE EXISTE AVANT LA FONCTIONNALITÉ QU'ELLE NETTOIE, ET C'EST VOULU. La
/// décision du 30 septembre 2026 — point 7 — a fixé ensemble la photo de preuve
/// et sa rétention : un mois. Écrire d'abord le dépôt aurait créé des données
/// personnelles sans savoir qui les efface, ce que ce point existe précisément
/// pour éviter. Tant qu'aucune preuve n'est déposée, ce balayage ne trouve rien
/// et ne coûte rien — c'est le bon ordre, pas du zèle.
///
/// UNE PHOTO DE REMISE N'EST PAS UNE PIÈCE COMPTABLE. Elle cadre une porte, une
/// cour, parfois une personne qui n'est même pas cliente et n'a rien demandé.
/// Sa valeur tombe avec le litige qu'elle sert à trancher ; sa charge, elle,
/// reste entière. Trente jours, et elle disparaît.
///
/// L'OCTET PART AVANT LA LIGNE, et c'est l'inverse du dépôt. À l'écriture on
/// range l'objet puis la ligne, pour ne jamais promettre un fichier qui n'existe
/// pas. À l'effacement on supprime l'objet PUIS la ligne : si l'on retirait la
/// ligne d'abord et que le stockage refusait, l'objet resterait sans inventaire
/// — invisible, et donc impossible à effacer une seconde fois. On préfère un
/// inventaire qui pointe brièvement dans le vide à un fichier que plus personne
/// ne sait nommer.
///
/// AUCUNE EXCEPTION NE SORT D'ICI. Un BackgroundService qui en laisse échapper
/// une arrête l'hôte : un stockage objet injoignable trois secondes ferait
/// tomber Media, donc tout dépôt de pièce sur la plateforme. Même règle que
/// l'abandon des impayées.
/// </remarks>
public sealed class PurgeDesPreuves(
    IServiceScopeFactory scopeFactory,
    IOptions<PreuveRetentionOptions> options,
    ILogger<PurgeDesPreuves> journal) : BackgroundService
{
    private readonly PreuveRetentionOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var periode = TimeSpan.FromHours(Math.Max(1, _options.BalayageHeures));

        journal.LogInformation(
            "Purge des preuves : conservation de {Jours} jours, balayage toutes les {Heures} h.",
            _options.Jours,
            periode.TotalHours);

        using var timer = new PeriodicTimer(periode);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await BalayerAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                journal.LogError(exception, "La purge des preuves a echoue. Elle reprendra au prochain passage.");
            }
        }
    }

    private async Task BalayerAsync(CancellationToken cancellationToken)
    {
        using var portee = scopeFactory.CreateScope();

        var medias = portee.ServiceProvider.GetRequiredService<IMediaRepository>();
        var stockage = portee.ServiceProvider.GetRequiredService<IObjectStore>();
        var unitOfWork = portee.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var horloge = portee.ServiceProvider.GetRequiredService<IClock>();

        var limite = horloge.UtcNow.AddDays(-Math.Max(1, _options.Jours));

        var expirees = await medias
            .ListerAvantAsync(MediaKind.DeliveryProof, limite, Math.Max(1, _options.Lot), cancellationToken)
            .ConfigureAwait(false);

        if (expirees.Count == 0)
        {
            return;
        }

        var effacees = 0;

        foreach (var preuve in expirees)
        {
            try
            {
                await stockage.DeleteAsync(preuve.StorageKey, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // UNE PREUVE QUI RESISTE NE BLOQUE PAS LES AUTRES. On la laisse
                // dans l'inventaire : le prochain passage la reprendra, et elle
                // reste nommable entre-temps.
                journal.LogWarning(
                    exception,
                    "Preuve {MediaId} : le stockage a refuse la suppression, elle sera reprise.",
                    preuve.Id);

                continue;
            }

            medias.Remove(preuve);
            effacees++;
        }

        if (effacees == 0)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // EN AVERTISSEMENT, PARCE QUE C'EST IRREVERSIBLE, et pour la meme raison
        // que la suppression par proprietaire : un effacement de donnees
        // personnelles merite une ligne qu'on retrouve.
        journal.LogWarning(
            "Purge des preuves : {Nombre} media(s) efface(s), deposes avant {Limite:u}.",
            effacees,
            limite);
    }
}
