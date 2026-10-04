using Hba.BuildingBlocks.Application.Messaging;
using Hba.Ride.Application.Common.Views;
using Hba.Ride.Domain.Results;

namespace Hba.Ride.Application.Features.Rides.Queries;

/// <summary>Une course par son identifiant.</summary>
public sealed record GetRideByIdQuery(Guid RideId) : IQuery<Result<RideView>>;

/// <summary>
/// Les courses en cours de l'appelant. Liste vide s'il n'en a aucune.
/// </summary>
///
/// <remarks>
/// AUCUN IDENTIFIANT DANS LA REQUÊTE, ET C'EST LE POINT. Le client est lu dans
/// le jeton : un paramètre laisserait n'importe qui demander les courses en
/// cours d'un autre, avec leurs adresses de prise en charge.
///
/// L'ABSENCE DE COURSE N'EST PAS UN REFUS. Un client sans course en cours est le
/// cas ordinaire au lancement de l'application : la requête réussit et rend une
/// liste vide, là où une 404 obligerait l'écran d'accueil à traiter une erreur
/// pour afficher son état normal.
///
/// UNE LISTE PARCE QU'UN CLIENT PEUT EN AVOIR PLUSIEURS. L'application cliente
/// doit donc savoir laquelle elle suit : un écran qui prend la première de la
/// liste changerait de course sous les doigts du passager dès qu'il en commande
/// une seconde.
/// </remarks>
public sealed record GetCustomerActiveRidesQuery : IQuery<Result<IReadOnlyList<RideView>>>;

/// <summary>L'historique du client appelant, page par page.</summary>
public sealed record GetCustomerRideHistoryQuery(int PageSize = 20, int Offset = 0)
    : IQuery<Result<RidePageView>>;

/// <summary>
/// La course en cours du chauffeur appelant, s'il en a une.
/// </summary>
///
/// <remarks>
/// C'EST CE QUE L'APPLICATION CHAUFFEUR INTERROGE AU RÉVEIL, après une coupure
/// réseau ou un redémarrage du téléphone. Sans elle, un chauffeur rouvrant son
/// application se retrouverait sans la mission qu'il est en train d'exécuter.
/// </remarks>
public sealed record GetDriverActiveRideQuery : IQuery<Result<RideView?>>;
