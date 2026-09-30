using Hba.BuildingBlocks.Application.Messaging;
using Hba.Delivery.Application.Views;
using Hba.Delivery.Domain.Deliveries;

namespace Hba.Delivery.Application.Commands.DriverActions;

/// <summary>
/// Actions du livreur. Toutes portent un horodatage client et une clé
/// d'idempotence : l'app livreur met ses actions en file quand la connexion est
/// instable et les rejoue telles quelles.
/// </summary>
public sealed record MarkArrivedAtPickupCommand(
    Guid DeliveryId,
    DateTimeOffset? OccurredAt,
    string? IdempotencyKey) : ICommand<DeliveryView>;

public sealed record MarkPickedUpCommand(
    Guid DeliveryId,
    DateTimeOffset? OccurredAt,
    string? IdempotencyKey,
    string? ProofObjectKey) : ICommand<DeliveryView>;

/// <summary>
/// Remise au destinataire. L'OTP vient du destinataire : il n'est jamais envoyé
/// au livreur, il le saisit.
/// </summary>
/// <summary>
/// Le livreur déclare que la course ne peut pas aboutir.
/// </summary>
///
/// <remarks>
/// LE MOTIF EST OBLIGATOIRE ET LIBRE. Aucune liste n'est tranchée ; celle que
/// propose l'application est de l'habillage, pas un contrat.
/// </remarks>
public sealed record DeclareIncidentCommand(
    Guid DeliveryId,
    string Reason,
    DateTimeOffset? OccurredAt,
    string? IdempotencyKey) : ICommand<DeliveryView>;

/// <summary>Ce que rend un dépôt de preuve réussi.</summary>
public sealed record PreuveDeposee(Guid MediaId, EtapeDeLaPreuve Etape);

/// <summary>
/// Le livreur joint une photo à une étape qu'il vient de franchir.
/// </summary>
///
/// <remarks>
/// ELLE N'EST PAS DANS « MarkPickedUp » NI DANS « ConfirmDelivery », ET C'EST LA
/// DECISION. Le point 7 a tranché : la photo est proposée, jamais exigée, et
/// « la remise n'attend jamais la photo ». Une étape qui porterait le fichier
/// échouerait avec lui — réseau coupé, mémoire pleine, appareil photo refusé —
/// et immobiliserait le livreur pour une raison qui ne regarde pas la course.
/// Deux appels séparés sont la seule forme où l'échec du second ne défait pas
/// le premier.
///
/// ELLE NE PORTE NI CLE D'IDEMPOTENCE NI HORODATAGE DU TELEPHONE, contrairement
/// aux quatre autres actions. Elle n'entre pas dans la file hors ligne : cette
/// file range du JSON dans le coffre chiffré et ne sait pas transporter un
/// fichier — c'est le fait même qui a écarté « photo exigée ». Un dépôt se fait
/// donc toujours en direct : le rejeu est traité par l'agrégat, qui accepte le
/// même identifiant deux fois et refuse un autre, et l'heure est celle du
/// serveur — laisser le téléphone dater ce qu'il envoie à l'instant lui donnerait
/// le moyen de contourner la fenêtre de dépôt en se déclarant à l'heure.
/// </remarks>
public sealed record AttacherLaPreuveCommand(
    Guid DeliveryId,
    EtapeDeLaPreuve Etape,
    Stream Contenu,
    long TailleOctets,
    string TypeDeContenu) : ICommand<PreuveDeposee>;

public sealed record ConfirmDeliveryCommand(
    Guid DeliveryId,
    string Otp,
    DateTimeOffset? OccurredAt,
    string? IdempotencyKey,
    string? ProofObjectKey) : ICommand<DeliveryView>;
