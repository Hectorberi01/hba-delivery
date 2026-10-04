using Confluent.Kafka;
using Google.Protobuf;
using Hba.BuildingBlocks.Messaging.Kafka;
using Microsoft.Extensions.Options;

namespace Hba.Realtime.Api.Messaging;

/// <summary>
/// Consommateur Kafka SANS INBOX, pour diffuser et rien d'autre.
/// </summary>
///
/// <remarks>
/// POURQUOI CE SOCLE EXISTE A COTE DE <c>KafkaConsumerBase</c>, ET N'EST PAS DANS LES
/// BUILDINGBLOCKS.
///
/// Le socle commun du depot ouvre une Inbox avant chaque traitement : c'est ce qui
/// empeche de crediter deux fois un grand livre ou d'envoyer deux fois un SMS. Il
/// exige donc une base de donnees, une transaction par message, et une table qui
/// grandit.
///
/// ICI L'EFFET D'UN MESSAGE EST D'ECRIRE UNE TRAME DANS UNE SOCKET, et l'ecrire deux
/// fois est sans consequence : l'ecran affiche la meme chose. L'Inbox couterait une
/// base entiere a un service qui ne possede rien, pour se proteger d'un doublon
/// inoffensif. Ce socle est donc une VARIANTE ASSUMEE, et il reste dans ce service
/// parce qu'il ne vaut que pour ce compromis-la — le mettre dans les BuildingBlocks
/// inviterait un autre service a s'en servir pour un traitement qui, lui, aurait des
/// effets.
///
/// DEUX DIFFERENCES DE REGLAGE, ET LA SECONDE EST LA PLUS IMPORTANTE.
///
/// 1. AUCUNE INBOX, donc aucun scope de requete, aucune transaction, aucun
///    enregistrement. Le message est lu, diffuse, acquitte.
///
/// 2. <c>AutoOffsetReset.Latest</c> AU LIEU DE Earliest, ET LE GROUPE PORTE
///    L'INSTANCE. C'est ce qui empeche un redemarrage de deverser l'histoire dans les
///    sockets : avec Earliest et un groupe partage, un service qui repart apres une
///    heure d'arret pousserait une heure de changements d'etat a des clients dont
///    l'ecran affiche deja la situation finale — « votre chauffeur arrive » pour une
///    course livree depuis quarante minutes. Le temps reel n'a aucun interet a
///    rattraper le passe : ce qui compte est l'etat MAINTENANT, et le sondage ralenti
///    des applications le donne deja.
///
/// CE QUE CELA COUTE, ET QUI EST ECRIT NOIR SUR BLANC DANS Readme.cs : les messages
/// emis pendant une interruption de ce service sont perdus pour la diffusion. C'est
/// pour cela que le sondage reste en place dans les applications. Le temps reel est
/// un confort ; le sondage est la verite.
/// </remarks>
internal abstract class DiffusionConsumerBase<TMessage>(
    IOptions<KafkaOptions> options,
    ILogger logger) : BackgroundService
    where TMessage : IMessage<TMessage>, new()
{
    private readonly KafkaOptions _options = options.Value;

    protected abstract string Topic { get; }

    /// <summary>Diffuse. Ne doit jamais lever pour un message mal forme.</summary>
    protected abstract Task DiffuserAsync(TMessage message, CancellationToken cancellationToken);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => Task.Run(() => BoucleAsync(stoppingToken), stoppingToken);

    private async Task BoucleAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,

            // UN GROUPE PAR INSTANCE, ET NON UN GROUPE PAR SERVICE.
            //
            // Partout ailleurs dans ce depot, un groupe par service est ce qu'il faut :
            // le message doit etre traite UNE fois, quelle que soit la replique qui le
            // prend. Ici, chaque replique detient des connexions differentes, et le
            // backplane Redis se charge de faire traverser un message de groupe d'une
            // replique a l'autre. Partager le groupe fonctionnerait donc — mais
            // laisserait la diffusion dependre de la repartition des partitions, et un
            // deploiement en roulement redistribuerait les partitions a chaque bascule,
            // avec une interruption a chaque fois.
            //
            // CHAQUE REPLIQUE LIT TOUT, et chacune diffuse a ses propres membres : c'est
            // plus de trafic Kafka — negligeable — et pas de dependance au hasard des
            // partitions. Voir aussi DiffuseurDePositions, qui fait le meme choix pour
            // la meme raison, et qui explique pourquoi on envoie alors aux CONNEXIONS
            // locales plutot qu'au groupe.
            GroupId = $"{_options.ConsumerGroupId}-{Environment.MachineName}",

            AutoOffsetReset = AutoOffsetReset.Latest,
            EnableAutoCommit = false,
            SecurityProtocol = Enum.Parse<SecurityProtocol>(_options.SecurityProtocol, ignoreCase: true),
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(Topic);

        logger.LogInformation("Diffusion : abonnement a {Topic}, groupe {Groupe}.", Topic, config.GroupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, byte[]>? resultat;

                try
                {
                    resultat = consumer.Consume(stoppingToken);
                }
                catch (ConsumeException ex)
                {
                    logger.LogError(ex, "Erreur de consommation sur {Topic}.", Topic);
                    continue;
                }

                if (resultat?.Message is null)
                {
                    continue;
                }

                await TraiterAsync(resultat, stoppingToken).ConfigureAwait(false);

                // ON ACQUITTE TOUJOURS, MEME EN CAS D'ECHEC, ET C'EST LE CONTRAIRE DU
                // SOCLE COMMUN. Rejouer un message dont la diffusion a echoue n'a aucun
                // sens : dix secondes plus tard, l'etat qu'il portait est deja perime, et
                // le bloquer gelerait la partition — donc TOUT le temps reel de ce topic
                // — pour un message devenu inutile.
                consumer.Commit(resultat);
            }
        }
        catch (OperationCanceledException)
        {
            // Arret normal.
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task TraiterAsync(ConsumeResult<string, byte[]> resultat, CancellationToken cancellationToken)
    {
        TMessage message;

        try
        {
            var parseur = new MessageParser<TMessage>(() => new TMessage());
            message = parseur.ParseFrom(resultat.Message.Value);
        }
        catch (InvalidProtocolBufferException ex)
        {
            logger.LogError(ex, "Message illisible sur {Topic} a l'offset {Offset}.", Topic, resultat.Offset.Value);
            return;
        }

        try
        {
            await DiffuserAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // UNE DIFFUSION RATEE NE DOIT PAS TUER LA BOUCLE.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // PAS DE REESSAI, ET PAS D'ABANDON SILENCIEUX NON PLUS. On consigne et on
            // avance : le prochain message portera un etat plus recent, et le sondage des
            // applications rattrapera ce qui manque. Reessayer ferait attendre des
            // messages frais derriere un message perime.
            logger.LogError(
                ex,
                "Diffusion en echec sur {Topic} a l'offset {Offset}. Le sondage des applications rattrapera.",
                Topic,
                resultat.Offset.Value);
        }
    }
}
