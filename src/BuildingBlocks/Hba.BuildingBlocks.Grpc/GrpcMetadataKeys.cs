namespace Hba.BuildingBlocks.Grpc;

public static class GrpcMetadataKeys
{
    public const string Authorization = "authorization";
    public const string CorrelationId = "hba-correlation-id";
    public const string IdempotencyKey = "hba-idempotency-key";

    /// <summary>Code d'erreur métier stable, renvoyé dans les trailers.</summary>
    public const string ErrorCode = "hba-error-code";
}
