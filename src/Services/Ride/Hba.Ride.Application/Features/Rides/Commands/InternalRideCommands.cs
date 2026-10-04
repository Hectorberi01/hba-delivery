using Hba.BuildingBlocks.Application.Messaging;
using Hba.Ride.Domain.Results;

namespace Hba.Ride.Application.Features.Rides.Commands;

// Les transitions déclenchées par un acteur système, depuis les consommateurs
// Kafka. Elles ne passent par aucun jeton : l'acteur est imposé par le code
// (Actor.DispatchEngine), et c'est lui qui apparaît dans l'audit. Les exposer
// derrière une route HTTP les rendrait appelables par un téléphone.

/// <summary>La première vague d'offres est partie : la recherche est ouverte.</summary>
public sealed record StartRideSearchCommand(Guid RideId, DateTimeOffset OccurredAt) : ICommand<Result>;

/// <summary>
/// Un chauffeur a accepté. Le gagnant est déjà départagé côté Dispatch.
/// </summary>
///
/// <remarks>
/// L'IDENTIFIANT DU CHAUFFEUR VIENT DE DISPATCH, PAS D'UN JETON, et c'est la
/// seule exception assumée à la règle inverse qui vaut pour les commandes
/// humaines : ici l'appelant n'est pas le chauffeur, c'est le moteur qui a reçu
/// son acceptation et tranché entre plusieurs.
/// </remarks>
public sealed record AssignRideDriverCommand(
    Guid RideId,
    string DriverId,
    DateTimeOffset AssignedAt) : ICommand<Result>;

/// <summary>
/// Toutes les vagues ont échoué.
/// </summary>
///
/// <remarks>
/// LE NOMBRE DE VAGUES EST PORTÉ MAIS PAS ENCORE CONSERVÉ. L'agrégat ne stocke
/// pas ce compteur : il n'en a pas besoin pour décider, et l'inventer dans le
/// modèle sans règle qui le lise serait une colonne morte. Il reste dans la
/// commande parce que Dispatch le connaît et que le journal le mérite.
/// </remarks>
public sealed record MarkNoRideDriverFoundCommand(
    Guid RideId,
    int WavesAttempted,
    DateTimeOffset OccurredAt) : ICommand<Result>;

/// <summary>
/// Le paiement de la course a échoué ou expiré.
/// </summary>
///
/// <remarks>
/// LE MOTIF VIENT DU FOURNISSEUR ET N'EST PAS UNE PHRASE :
/// « insufficient_balance » s'enregistre pour le support, et c'est l'application
/// qui écrit ce que lit le passager.
/// </remarks>
public sealed record FailRidePaymentCommand(
    Guid RideId,
    string? Reason,
    DateTimeOffset OccurredAt) : ICommand<Result>;
