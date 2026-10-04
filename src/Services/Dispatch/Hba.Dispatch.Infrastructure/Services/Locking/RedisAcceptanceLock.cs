using Hba.Dispatch.Application.Common.Interfaces;
using Hba.Dispatch.Domain.Dispatching;
using StackExchange.Redis;

namespace Hba.Dispatch.Infrastructure.Services.Locking;

/// <summary>
/// Verrou d'acceptation, un par mission.
///
/// LA CLE PORTE LA NATURE EN PLUS DE L'IDENTIFIANT. Sans elle, une livraison
/// et une course au meme identifiant partageraient un verrou : la seconde
/// acceptation serait refusee sans raison, et personne ne pourrait le
/// reproduire.
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
        ServiceType serviceType,
        Guid referenceId,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid().ToString("N");

        var acquis = await redis.GetDatabase()
            .StringSetAsync(Key(serviceType, referenceId), token, duration, When.NotExists)
            .ConfigureAwait(false);

        return acquis ? token : null;
    }

    public async Task ReleaseAsync(
        ServiceType serviceType,
        Guid referenceId,
        string token,
        CancellationToken cancellationToken)
        => await redis.GetDatabase()
            .ScriptEvaluateAsync(ReleaseScript, [Key(serviceType, referenceId)], [token])
            .ConfigureAwait(false);

    private static RedisKey Key(ServiceType serviceType, Guid referenceId)
        => $"{KeyPrefix}{serviceType.ToString().ToLowerInvariant()}:{referenceId:D}";
}
