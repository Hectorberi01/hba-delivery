using Hba.BuildingBlocks.Application.Messaging;
using Hba.Ride.Application.Common.Views;
using Hba.Ride.Domain.Results;

namespace Hba.Ride.Application.Features.Rides.Commands;

// Les transitions demandées par une personne, avec un jeton. Chacune rend la
// course telle qu'elle est devenue : l'application cliente n'a pas à relire
// derrière elle pour savoir où elle en est.

/// <summary>
/// Le chauffeur part vers le point de prise en charge.
/// </summary>
///
/// <remarks>
/// CETTE ÉTAPE N'EST PAS DÉCORATIVE. La table des transitions interdit d'aller
/// de <c>DriverAssigned</c> à <c>DriverArrived</c> : c'est ce signal, et lui
/// seul, qui fait apparaître le chauffeur en approche sur l'écran du passager.
/// </remarks>
public sealed record DriverIsOnTheWayCommand(Guid RideId) : ICommand<Result<RideView>>;

/// <summary>Le chauffeur est sur place et attend le passager.</summary>
public sealed record DriverHasArrivedCommand(Guid RideId) : ICommand<Result<RideView>>;

/// <summary>
/// Le passager est à bord : la course part.
/// </summary>
///
/// <remarks>
/// SEUL LE CHAUFFEUR DÉMARRE, ET L'AGRÉGAT EXIGE QU'IL SOIT ARRIVÉ. Un démarrage
/// déclaré depuis le téléphone du passager, ou avant l'arrivée, ferait courir le
/// compteur sur un trajet que personne n'a commencé.
/// </remarks>
public sealed record StartRideCommand(Guid RideId) : ICommand<Result<RideView>>;

/// <summary>
/// Le passager est descendu : la course est terminée.
/// </summary>
///
/// <remarks>
/// AUCUN MONTANT N'EST ACCEPTÉ ICI. Le prix est ferme à la commande (point 35) :
/// l'agrégat recopie l'estimation en prix final. Un paramètre de montant
/// laisserait croire qu'un chauffeur peut décider de ce qui est dû.
/// </remarks>
public sealed record CompleteRideCommand(Guid RideId) : ICommand<Result<RideView>>;

/// <summary>
/// Annulation, par le passager ou par le chauffeur affecté.
/// </summary>
///
/// <remarks>
/// LE MOTIF EST FACULTATIF ET N'EST PAS UNE AUTORISATION. Il est enregistré pour
/// être lu plus tard ; ce n'est pas lui qui décide si l'annulation passe, c'est
/// l'état de la course et l'identité de l'appelant.
/// </remarks>
public sealed record CancelRideCommand(Guid RideId, string? Reason = null) : ICommand<Result<RideView>>;
