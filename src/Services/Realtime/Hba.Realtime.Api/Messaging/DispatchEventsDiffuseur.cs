using Hba.BuildingBlocks.Messaging.Kafka;
using Hba.Contracts.Dispatch.V1;
using Hba.Realtime.Api.Diffusion;
using Hba.Realtime.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Hba.Realtime.Api.Messaging;

/// <summary>
/// Les offres : ce que le chauffeur recoit sans attendre son prochain sondage.
/// </summary>
///
/// <remarks>
/// C'EST LE FLUX QUI SE PAIE EN TAUX D'ACCEPTATION. L'application chauffeur sonde
/// toutes les cinq secondes ; une offre dure quelques dizaines de secondes. Les cinq
/// secondes perdues au debut sont donc un dixieme du temps de reflexion — et sur une
/// vague a plusieurs chauffeurs, celui qui est prevenu le premier accepte le premier.
///
/// UN SEUL EVENEMENT SUR QUATRE EST DIFFUSE, et c'est deliberement asymetrique :
///
/// — OfferSent part au chauffeur concerne : c'est l'information qu'il attend.
/// — OfferAccepted ne part pas. Le chauffeur qui a accepte le sait — il vient de
///   cliquer — et les AUTRES chauffeurs de la vague n'ont pas a apprendre qu'un
///   collegue a pris la course : leur offre expirera d'elle-meme, et leur dire
///   « perdue » reviendrait a leur compter les points.
/// — OfferExpired ne part pas : l'application connait l'echeance, elle a ferme sa
///   feuille toute seule.
/// — DispatchExhausted ne part pas ici. Il concerne le CLIENT — « aucun chauffeur
///   trouve » — et c'est Ride ou Delivery qui le lui dit, par son propre evenement.
///   Le diffuser ici enverrait la meme nouvelle deux fois par deux chemins.
///
/// L'OFFRE A LA MAIN PASSE PAR LE MEME CHEMIN. Une offre posee par l'exploitation en
/// dehors de toute vague porte wave_number a zero et sort du meme evenement : le
/// chauffeur la recoit exactement comme les autres, ce qui est le bon comportement —
/// il n'a pas a savoir d'ou elle vient.
/// </remarks>
internal sealed class DispatchEventsDiffuseur(
    IHubContext<DriverHub> chauffeurs,
    IOptions<KafkaOptions> options,
    ILogger<DispatchEventsDiffuseur> logger)
    : DiffusionConsumerBase<DispatchEvent>(options, logger)
{
    protected override string Topic => KafkaTopics.DispatchEvents;

    protected override async Task DiffuserAsync(DispatchEvent message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.PayloadCase != DispatchEvent.PayloadOneofCase.OfferSent)
        {
            return;
        }

        var offre = message.OfferSent;

        if (string.IsNullOrWhiteSpace(offre.DriverId) || string.IsNullOrWhiteSpace(offre.OfferId))
        {
            return;
        }

        await chauffeurs
            .Clients.Group(Groupes.Chauffeur(offre.DriverId))
            .SendAsync(
                Trames.Methodes.Offre,
                new OffreSignalee(
                    offre.OfferId,
                    Nature(offre.ServiceType),
                    offre.ReferenceId,
                    offre.ExpiresAt?.ToDateTimeOffset()),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// La nature, dans les mots que les applications emploient.
    ///
    /// NON PRECISEE VAUT « LIVRAISON » ICI, et c'est le seul repli de ce service. La
    /// raison est la meme que partout ailleurs dans le depot : les missions anterieures
    /// au 1er octobre 2026 sont toutes des colis, et une offre dont la nature serait
    /// vide afficherait un libelle vide dans l'application du chauffeur. Le risque est
    /// nul — ce champ ne sert qu'a nommer un ecran, pas a router un message.
    /// </summary>
    private static string Nature(Hba.Contracts.Common.V1.ServiceType nature)
        => nature == Hba.Contracts.Common.V1.ServiceType.Ride ? "course" : "livraison";
}
