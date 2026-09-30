using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Delivery.Application.Ports;
using Hba.Delivery.Application.Views;
using Hba.Delivery.Domain.Deliveries;

namespace Hba.Delivery.Application.Commands.DriverActions;

/// <summary>
/// Socle commun : charger, vérifier que l'appelant est bien LE livreur affecté,
/// appliquer, enregistrer. L'agrégat refait la vérification de son côté.
/// </summary>
public abstract class DriverActionHandlerBase(
    IDeliveryRepository repository,
    IIdempotencyStore idempotency,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
{
    /// <summary>
    /// UNE SEULE PORTÉE POUR LES QUATRE ACTIONS, parce que la clé porte déjà
    /// l'étape : l'application livreur envoie « deliver:&lt;course&gt; »,
    /// « arrived:&lt;course&gt; ». Une portée par action n'ajouterait rien et
    /// obligerait chaque gestionnaire à en connaître une.
    /// </summary>
    private const string IdempotencyScope = "delivery:driver-action";

    protected IClock Clock => clock;

    protected ICallerContext Caller => caller;

    /// <param name="apply">
    /// Applique l'étape et rend VRAI si elle a réussi. Ce booléen ne décide que
    /// d'une chose : si la clé d'idempotence est mémorisée. La sauvegarde, elle,
    /// a lieu dans les deux cas — voir plus bas, c'est le fond de la correction
    /// du 30 septembre 2026.
    /// </param>
    protected async Task<DeliveryView> ApplyAsync(
        Guid deliveryId,
        DateTimeOffset? clientTimestamp,
        string? idempotencyKey,
        Func<DeliveryAggregate, Actor, DateTimeOffset, bool> apply,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(apply);

        if (!caller.IsInRole(HbaRoles.Driver))
        {
            throw new ForbiddenException("Cette action appartient au livreur affecté.");
        }

        var delivery = await repository.GetByIdAsync(deliveryId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Livraison", deliveryId.ToString());

        if (delivery.Driver is null || delivery.Driver.DriverId != caller.DriverId)
        {
            // NotFound et non Forbidden : ne rien révéler d'une course qui n'est
            // pas la sienne.
            throw new NotFoundException("Livraison", deliveryId.ToString());
        }

        // LA CLÉ ÉTAIT TRANSPORTÉE PARTOUT ET LUE NULLE PART.
        //
        // La passerelle la prend dans l'en-tête, la commande la porte, et aucun
        // gestionnaire ne la regardait : l'idempotence reposait entièrement sur
        // les gardes d'état de l'agrégat. Cela tient pour « arrivé » et
        // « collecté », qui sortent en silence si l'étape est déjà franchie.
        //
        // CELA NE TIENT PAS POUR LA REMISE, ET C'EST LE CAS QUI COMPTE. L'app
        // livreur met ses actions en file quand le réseau tombe et les rejoue
        // au retour. Une remise DÉJÀ RÉUSSIE rejouée tombait sur une transition
        // interdite depuis DELIVERED : le livreur lisait une erreur pour une
        // course qu'il avait bel et bien livrée. Et un OTP erroné rejoué brûlait
        // une des cinq tentatives à chaque fois.
        //
        // ON NE MÉMORISE QUE LE SUCCÈS : un refus n'est pas un résultat à
        // rejouer, et le rejeu d'un code faux doit rester un refus.
        var cle = string.IsNullOrWhiteSpace(idempotencyKey) ? null : $"{deliveryId}:{idempotencyKey}";

        if (cle is not null)
        {
            var connu = await idempotency
                .TryGetResultAsync(IdempotencyScope, cle, cancellationToken)
                .ConfigureAwait(false);

            if (connu is not null)
            {
                return DeliveryViewMapper.ToView(delivery, caller);
            }
        }

        var reussi = apply(delivery, caller.ToActor(), NormalizeTimestamp(clientTimestamp));

        if (cle is not null && reussi)
        {
            await idempotency
                .RememberAsync(IdempotencyScope, cle, deliveryId.ToString(), cancellationToken)
                .ConfigureAwait(false);
        }

        // ON ENREGISTRE MEME QUAND L'ETAPE A ECHOUE, et c'est la correction du
        // 30 septembre 2026.
        //
        // Un code de remise refusé n'est pas un non-événement : il CONSOMME une
        // tentative sur les cinq, donc il écrit. Tant que le refus remontait en
        // exception depuis l'agrégat, cette ligne n'était jamais atteinte, le
        // compteur repartait de zéro à chaque requête, et le verrou ne
        // protégeait rien — le code de remise était forçable. L'agrégat rend
        // maintenant son issue au lieu de la lever, précisément pour qu'on passe
        // ici avant de refuser.
        //
        // UNE SEULE TRANSACTION : l'état de la course et la clé sont validés
        // ensemble. Écrire la clé à part laisserait une action « déjà faite »
        // dont l'effet n'aurait jamais été enregistré. Et comme la clé n'est
        // mémorisée qu'au succès, le rejeu d'un code faux reste un refus.
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return DeliveryViewMapper.ToView(delivery, caller);
    }

    /// <summary>
    /// Une preuve que personne n'a pu délivrer n'est pas une preuve.
    /// </summary>
    ///
    /// <remarks>
    /// LE CHAMP ETAIT ACCEPTE TEL QUEL, ET RANGE COMME UNE PREUVE. La passerelle
    /// transmet « ProofObjectKey » depuis le corps de la requête, l'agrégat
    /// l'écrivait tel quel, et RIEN ne vérifiait que cette chaîne désigne un
    /// objet existant, du bon type, rattaché à cette course. Le livreur pouvait
    /// donc écrire lui-même la preuve censée l'engager.
    ///
    /// ON LE REFUSE PLUTOT QUE DE L'IGNORER. L'ignorer en silence laisserait
    /// l'appelant croire qu'une preuve est conservée ; la stocker fabriquerait
    /// une pièce à charge inventée par celui qu'elle doit engager. Entre les
    /// deux, la seule réponse honnête est de dire non.
    ///
    /// LE CHEMIN EXISTE DEPUIS LE 30 SEPTEMBRE 2026, ET CE GARDE RESTE.
    /// Une photo se dépose désormais par sa propre route, et l'agrégat la
    /// rattache par <c>AttacherLaPreuve</c> — avec un identifiant rendu par
    /// Media, donc un fichier qui existe. Ce champ-ci, lui, porte toujours une
    /// chaîne venue du téléphone que rien ne peut vérifier : elle reste refusée,
    /// et le jour où plus personne ne l'envoie, c'est le champ qui doit
    /// disparaître du contrat, pas ce refus.
    ///
    /// ET L'ADR 0005 N'A PAS BOUGE : ce qui fait foi reste le CODE DE REMISE,
    /// dicté par le destinataire. La photo est un complément.
    /// </remarks>
    protected static void EnsureNoUnverifiedProof(string? proofObjectKey)
    {
        if (string.IsNullOrWhiteSpace(proofObjectKey))
        {
            return;
        }

        throw new DomainException(
            "PROOF_NOT_SUPPORTED",
            "Le dépôt d'une preuve n'est pas encore ouvert : aucune clé ne peut "
            + "être vérifiée, et une preuve invérifiable n'en est pas une.");
    }

    /// <summary>
    /// L'horodatage client est accepté, mais borné : jamais dans le futur, et pas
    /// plus de 24 h dans le passé. Un téléphone mal réglé ne doit pas réécrire
    /// l'histoire d'une course.
    /// </summary>
    private DateTimeOffset NormalizeTimestamp(DateTimeOffset? clientTimestamp)
    {
        var now = clock.UtcNow;

        if (clientTimestamp is null)
        {
            return now;
        }

        var value = clientTimestamp.Value;

        if (value > now || value < now.AddHours(-24))
        {
            return now;
        }

        return value;
    }
}

public sealed class MarkArrivedAtPickupHandler(
    IDeliveryRepository repository,
    IIdempotencyStore idempotency,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
    : DriverActionHandlerBase(repository, idempotency, unitOfWork, caller, clock),
      ICommandHandler<MarkArrivedAtPickupCommand, DeliveryView>
{
    public Task<DeliveryView> HandleAsync(MarkArrivedAtPickupCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ApplyAsync(
            command.DeliveryId,
            command.OccurredAt,
            command.IdempotencyKey,
            (delivery, actor, at) =>
            {
                delivery.MarkArrivedAtPickup(actor, at);
                return true;
            },
            cancellationToken);
    }
}

public sealed class MarkPickedUpHandler(
    IDeliveryRepository repository,
    IIdempotencyStore idempotency,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
    : DriverActionHandlerBase(repository, idempotency, unitOfWork, caller, clock),
      ICommandHandler<MarkPickedUpCommand, DeliveryView>
{
    public Task<DeliveryView> HandleAsync(MarkPickedUpCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureNoUnverifiedProof(command.ProofObjectKey);

        return ApplyAsync(
            command.DeliveryId,
            command.OccurredAt,
            command.IdempotencyKey,
            (delivery, actor, at) =>
            {
                delivery.MarkPickedUp(actor, at);
                return true;
            },
            cancellationToken);
    }
}

public sealed class DeclareIncidentHandler(
    IDeliveryRepository repository,
    IIdempotencyStore idempotency,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
    : DriverActionHandlerBase(repository, idempotency, unitOfWork, caller, clock),
      ICommandHandler<DeclareIncidentCommand, DeliveryView>
{
    public Task<DeliveryView> HandleAsync(DeclareIncidentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ApplyAsync(
            command.DeliveryId,
            command.OccurredAt,
            command.IdempotencyKey,
            (delivery, actor, at) =>
            {
                delivery.DeclareIncident(command.Reason, actor, at);
                return true;
            },
            cancellationToken);
    }
}

public sealed class ConfirmDeliveryHandler(
    IDeliveryRepository repository,
    IIdempotencyStore idempotency,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock)
    : DriverActionHandlerBase(repository, idempotency, unitOfWork, caller, clock),
      ICommandHandler<ConfirmDeliveryCommand, DeliveryView>
{
    public async Task<DeliveryView> HandleAsync(
        ConfirmDeliveryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureNoUnverifiedProof(command.ProofObjectKey);

        var issue = DeliveryConfirmation.Confirmed;

        var vue = await ApplyAsync(
            command.DeliveryId,
            command.OccurredAt,
            command.IdempotencyKey,
            (delivery, actor, at) =>
            {
                issue = delivery.ConfirmDelivery(command.Otp, actor, at);
                return issue == DeliveryConfirmation.Confirmed;
            },
            cancellationToken).ConfigureAwait(false);

        // LE REFUS SE LEVE APRES LA SAUVEGARDE, JAMAIS AVANT, et l'ordre est
        // toute la correction : la tentative consommée est déjà en base quand on
        // arrive ici. Le livreur reçoit le même INVALID_OTP qu'avant — rien ne
        // change pour l'application —, mais cette fois la cinquième tentative
        // verrouille vraiment la course.
        if (issue == DeliveryConfirmation.OtpRefused)
        {
            throw new DomainException("INVALID_OTP", "Code de remise incorrect.");
        }

        return vue;
    }
}
