using Microsoft.AspNetCore.Authorization;

namespace Hba.Payment.Api.Grpc;

/// <summary>
/// Entree synchrone du service Payment. Aucune methode n'est encore implementee :
/// chaque appel renvoie UNIMPLEMENTED, ce qui est explicite pour les appelants
/// et ne fait pas croire a un comportement inexistant.
/// </summary>
[Authorize]
public sealed class PaymentGrpcService : Hba.Contracts.Payment.V1.PaymentService.PaymentServiceBase;
