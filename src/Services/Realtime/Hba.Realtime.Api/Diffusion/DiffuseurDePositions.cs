using System.Text.Json;
using Hba.Realtime.Api.Hubs;
using Hba.Realtime.Api.Options;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Hba.Realtime.Api.Diffusion;

/// <summary>
/// Le point qui bouge : les battements de Location vers les ecrans.
/// </summary>
///
/// <remarks>
/// POURQUOI REDIS PUB/SUB ET NON KAFKA, ALORS QUE TOUT LE RESTE DU SERVICE LIT KAFKA.
///
/// Un battement de position est un fait PERISSABLE : il vaut vingt secondes, personne
/// ne le relira jamais, et le suivant le remplace entierement. Kafka est fait pour le
/// contraire — des faits durables, ordonnes, rejouables, conserves. Y faire passer
/// cinq messages par seconde dont aucun ne merite d'etre relu encombrerait un bus
/// prevu pour l'argent et les commandes, et obligerait a choisir une politique de
/// retention pour des donnees qu'on ne veut surtout pas garder.
///
/// LE PUB/SUB DE REDIS N'OFFRE AUCUNE GARANTIE DE LIVRAISON, et c'est exactement ce
/// qu'il faut : un battement perdu est remplace vingt secondes plus tard. Le seul cas
/// ou cela se voit est une coupure longue, et l'ecran affiche alors un point qui
/// vieillit — ce que l'horodatage permet de dire a l'utilisateur.
///
/// --------------------------------------------------------------------------------
///
/// POURQUOI ON ENVOIE AUX CONNEXIONS ET NON AU GROUPE, CONTRAIREMENT AUX DIFFUSEURS
/// KAFKA. C'est la subtilite la plus facile a casser de tout ce service.
///
/// Les diffuseurs Kafka envoient a un GROUPE : le message n'arrive que sur une
/// replique — celle qui detient la partition — et le backplane Redis le fait traverser
/// vers les autres. Une replique, un envoi, tout le monde recoit une fois.
///
/// Le pub/sub, lui, livre a TOUTES les repliques. Si chacune envoyait au groupe, le
/// backplane rediffuserait chaque battement a tous les membres autant de fois qu'il y
/// a de repliques : avec trois repliques, trois fois le meme point. On envoie donc aux
/// CONNEXIONS que cette replique detient — <c>Clients.Clients(...)</c> ne traverse pas
/// le backplane — et chaque replique sert ses propres abonnes. Zero doublon, et aucune
/// dependance au backplane pour ce flux.
///
/// CE QUI OBLIGE A TENIR LA LISTE DES CONNEXIONS LOCALES, puisque SignalR ne permet
/// pas d'enumerer les membres d'un groupe. C'est le role de
/// <see cref="Presences"/>.
/// </remarks>
internal sealed class DiffuseurDePositions(
    IConnectionMultiplexer redis,
    IHubContext<CustomerHub> clients,
    IHubContext<OpsHub> exploitation,
    AnnuaireDesMissions annuaire,
    Presences presences,
    IOptions<RealtimeOptions> options,
    ILogger<DiffuseurDePositions> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Format = new(JsonSerializerDefaults.Web);

    private readonly RealtimeOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var abonne = redis.GetSubscriber();
        var canal = RedisChannel.Literal(_options.PositionChannel);

        await abonne.SubscribeAsync(canal, (_, valeur) => Recevoir(valeur, stoppingToken)).ConfigureAwait(false);

        logger.LogInformation("Diffusion des positions : abonnement au canal {Canal}.", _options.PositionChannel);

        // ON NE REND PAS LA MAIN : un BackgroundService dont ExecuteAsync se termine est
        // considere comme fini par l'hote, et l'abonnement serait libere avec lui.
        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
    }

    /// <summary>
    /// LE RAPPEL DE REDIS EST SYNCHRONE, ET ON NE L'ATTEND PAS.
    ///
    /// StackExchange.Redis appelle ce delegue sur son propre fil de lecture : y attendre
    /// une tache — une lecture de l'annuaire, un envoi SignalR — bloquerait la reception
    /// de TOUS les battements suivants derriere le plus lent. On lance donc le travail et
    /// on rend la main immediatement.
    ///
    /// L'ORDRE DES BATTEMENTS N'EST PAS GARANTI PAR CE CHOIX, et ce n'est pas grave : un
    /// point plus ancien qui arriverait apres un plus recent ferait reculer la pastille
    /// d'un battement. Le suivant corrige. Garantir l'ordre demanderait une file par
    /// chauffeur, pour un benefice invisible a l'oeil.
    /// </summary>
    private void Recevoir(RedisValue valeur, CancellationToken cancellationToken)
        => _ = Task.Run(() => AiguillerAsync(valeur, cancellationToken), cancellationToken);

    private async Task AiguillerAsync(RedisValue valeur, CancellationToken cancellationToken)
    {
        BattementDePosition? battement;

        try
        {
            battement = JsonSerializer.Deserialize<BattementDePosition>(valeur.ToString(), Format);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Battement illisible sur le canal des positions.");
            return;
        }

        // UN BATTEMENT INCOMPLET EST IGNORE, PAS AFFICHE. C'est la protection contre la
        // divergence de forme entre Location et ce service : sans elle, un champ renomme
        // d'un seul cote peindrait des points a la latitude zero, au large de l'Afrique.
        if (battement is null
            || string.IsNullOrWhiteSpace(battement.DriverId)
            || (battement.Latitude == 0d && battement.Longitude == 0d))
        {
            return;
        }

        var vuA = DateTimeOffset.FromUnixTimeSeconds(battement.VuAUnix);

        try
        {
            await VersLExploitationAsync(battement, vuA, cancellationToken).ConfigureAwait(false);
            await VersLesClientsAsync(battement, vuA, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Arret normal.
        }
#pragma warning disable CA1031 // UN BATTEMENT RATE NE DOIT PAS TUER L'ABONNEMENT.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Diffusion d'un battement en echec pour {Chauffeur}.", battement.DriverId);
        }
    }

    /// <summary>
    /// Vers la carte de l'exploitation : TOUT, cap, vitesse et precision compris.
    /// </summary>
    ///
    /// <remarks>
    /// ON ENVOIE MEME QUAND PERSONNE N'ECOUTE, et c'est sans consequence : SignalR ne
    /// fait rien pour un groupe vide. Verifier d'abord couterait un aller-retour par
    /// battement pour economiser un appel qui ne fait rien.
    /// </remarks>
    private Task VersLExploitationAsync(
        BattementDePosition battement,
        DateTimeOffset vuA,
        CancellationToken cancellationToken)
        => exploitation.Clients.Group(Groupes.Exploitation).SendAsync(
            Trames.Methodes.Position,
            new PositionDExploitation(
                battement.DriverId,
                battement.Latitude,
                battement.Longitude,
                battement.CapDegres,
                battement.VitesseMps,
                battement.PrecisionMetres,
                vuA),
            cancellationToken);

    /// <summary>
    /// Vers les clients des missions de ce chauffeur : LA POSITION SEULE.
    /// </summary>
    ///
    /// <remarks>
    /// NI CAP, NI VITESSE, NI PRECISION — point 37. La trame du client est volontairement
    /// plus pauvre que celle de l'exploitation, et c'est ici, sur ces trois lignes
    /// absentes, que la decision est appliquee. Les unifier pour « simplifier »
    /// rouvrirait le point.
    ///
    /// AUCUNE MISSION, AUCUN ENVOI. Un chauffeur hors mission emet ses battements comme
    /// les autres — il est en ligne, il attend une course — et l'annuaire ne rend rien
    /// pour lui. C'est la borne du point 37 dans son etat normal : pas de mission, pas de
    /// client, pas de position diffusee.
    /// </remarks>
    private async Task VersLesClientsAsync(
        BattementDePosition battement,
        DateTimeOffset vuA,
        CancellationToken cancellationToken)
    {
        var groupes = await annuaire.GroupesDuChauffeurAsync(battement.DriverId).ConfigureAwait(false);

        if (groupes.Count == 0)
        {
            return;
        }

        foreach (var groupe in groupes)
        {
            var connexions = presences.ConnexionsDuGroupe(groupe);

            if (connexions.Count == 0)
            {
                // CE GROUPE N'A AUCUN ABONNE SUR CETTE REPLIQUE. Une autre replique s'en
                // charge, ou personne ne regarde. Dans les deux cas il n'y a rien a faire —
                // et surtout pas envoyer au groupe, ce qui ferait traverser le backplane et
                // doublerait le battement pour les abonnes des autres repliques.
                continue;
            }

            var (nature, missionId) = Groupes.Decomposer(groupe);

            if (missionId.Length == 0)
            {
                continue;
            }

            await clients.Clients.Clients(connexions).SendAsync(
                Trames.Methodes.Position,
                new PositionDeMission(nature, missionId, battement.Latitude, battement.Longitude, vuA),
                cancellationToken).ConfigureAwait(false);
        }
    }
}
