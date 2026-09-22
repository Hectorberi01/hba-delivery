using Microsoft.AspNetCore.Authorization;

namespace Hba.Dispatch.Api.Grpc;

/// <summary>
/// Entree synchrone du service Dispatch. Aucune methode n'est encore implementee :
/// chaque appel renvoie UNIMPLEMENTED, ce qui est explicite pour les appelants
/// et ne fait pas croire a un comportement inexistant.
/// </summary>
[Authorize]
public sealed class DispatchGrpcService : Hba.Contracts.Dispatch.V1.DispatchService.DispatchServiceBase;
