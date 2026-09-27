using Hba.Dispatch.Application.Common.Interfaces;
using StackExchange.Redis;

namespace Hba.Dispatch.Infrastructure.Services.Locking;

/// <summary>
/// Verrou d'acceptation, un par livraison.
///
/// LA LIBERATION EST CONDITIONNELLE, par script Lua : on ne rend que le verrou
/// qu'on a pris. Sans cette precaution, un processus lent dont le verrou a
/// expire libererait celui d'un autre en croyant liberer le sien — et deux
/// acceptations passeraient en meme temps, ce que ce verrou existe justement
/// pour empecher.
/// </summary>
internal sealed class RedisAcceptanceLock(IConnectionMultiplexer redis) : IAcceptanceLock
{
    private const string KeyPrefix = "dispatch:accept:";

    private const string ReleaseScript = """
        if redis.call('get', KEYS[1]) == ARGV[1] then
          return redis.call('del', KEYS[1])
        else
          return 0
        end
        """;

    public async Task<string?> TryAcquireAsync(
        Guid deliveryId,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");

        var acquis = await redis.GetDatabase()
            .StringSetAsync(Key(deliveryId), token, duration, When.NotExists)
            .ConfigureAwait(false);

        return acquis ? token : null;
    }

    public async Task ReleaseAsync(Guid deliveryId, string token, CancellationToken cancellationToken)
        => await redis.GetDatabase()
            .ScriptEvaluateAsync(ReleaseScript, [Key(deliveryId)], [token])
            .ConfigureAwait(false);

    private static RedisKey Key(Guid deliveryId) => KeyPrefix + deliveryId.ToString("D");
}
