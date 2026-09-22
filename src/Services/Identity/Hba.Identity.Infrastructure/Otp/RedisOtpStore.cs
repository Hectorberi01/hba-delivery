using System.Globalization;
using System.Text.Json;
using Hba.Identity.Application.Ports;
using Hba.Identity.Domain.Otp;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Hba.Identity.Infrastructure.Otp;

/// <summary>
/// Les défis OTP vivent cinq minutes. Les mettre en base coûterait une écriture
/// durable, une transaction et une purge, pour une donnée qui n'a aucune valeur
/// dix minutes plus tard.
/// </summary>
internal sealed class RedisOtpStore(IConnectionMultiplexer redis) : IOtpStore
{
    private const string KeyPrefix = "otp:challenge:";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SaveAsync(OtpChallenge challenge, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(challenge);

        var ttl = challenge.ExpiresAt - DateTimeOffset.UtcNow;

        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(Snapshot.From(challenge), JsonOptions);

        await redis.GetDatabase()
            .StringSetAsync(KeyPrefix + challenge.Id.ToString("N"), payload, ttl)
            .ConfigureAwait(false);
    }

    public async Task<OtpChallenge?> GetAsync(Guid challengeId, CancellationToken cancellationToken)
    {
        var value = await redis.GetDatabase()
            .StringGetAsync(KeyPrefix + challengeId.ToString("N"))
            .ConfigureAwait(false);

        if (value.IsNullOrEmpty)
        {
            return null;
        }

        var snapshot = JsonSerializer.Deserialize<Snapshot>(value!, JsonOptions);

        return snapshot?.ToDomain();
    }

    public async Task DeleteAsync(Guid challengeId, CancellationToken cancellationToken)
        => await redis.GetDatabase()
            .KeyDeleteAsync(KeyPrefix + challengeId.ToString("N"))
            .ConfigureAwait(false);

    private sealed record Snapshot(
        Guid Id,
        string Phone,
        string CodeHash,
        int Intent,
        string? DeviceId,
        bool Eligible,
        long CreatedAtUnix,
        long ExpiresAtUnix,
        int Attempts)
    {
        public static Snapshot From(OtpChallenge challenge) => new(
            challenge.Id,
            challenge.Phone,
            challenge.CodeHash,
            (int)challenge.Intent,
            challenge.DeviceId,
            challenge.Eligible,
            challenge.CreatedAt.ToUnixTimeSeconds(),
            challenge.ExpiresAt.ToUnixTimeSeconds(),
            challenge.Attempts);

        public OtpChallenge ToDomain() => OtpChallenge.Restore(
            Id,
            Phone,
            CodeHash,
            (OtpIntent)Intent,
            DeviceId,
            Eligible,
            DateTimeOffset.FromUnixTimeSeconds(CreatedAtUnix),
            DateTimeOffset.FromUnixTimeSeconds(ExpiresAtUnix),
            Attempts);
    }
}

/// <summary>
/// Un SMS coûte de l'argent, et un numéro qu'on bombarde est un numéro qu'on
/// harcèle. Un envoi par minute et par numéro, compté dans Redis.
/// </summary>
internal sealed class RedisOtpRateLimiter(IConnectionMultiplexer redis, IOptions<OtpOptions> options)
    : IOtpRateLimiter
{
    private const string KeyPrefix = "otp:cooldown:";

    private const string HourlyPrefix = "otp:hourly:";

    public async Task<TimeSpan?> TryAcquireAsync(string phone, CancellationToken cancellationToken)
    {
        var database = redis.GetDatabase();
        var settings = options.Value;
        var cooldown = TimeSpan.FromSeconds(settings.ResendCooldownSeconds);

        // Deux garde-fous complémentaires : le délai entre deux envois évite le
        // pilonnage immédiat, le plafond horaire évite qu'on contourne le délai
        // en attendant patiemment soixante secondes, mille fois de suite.
        var hourlyKey = HourlyPrefix + phone;
        var sent = await database.StringIncrementAsync(hourlyKey).ConfigureAwait(false);

        if (sent == 1)
        {
            await database.KeyExpireAsync(hourlyKey, TimeSpan.FromHours(1)).ConfigureAwait(false);
        }

        if (sent > settings.MaxPerHour)
        {
            var window = await database.KeyTimeToLiveAsync(hourlyKey).ConfigureAwait(false);
            return window ?? TimeSpan.FromHours(1);
        }

        var key = KeyPrefix + phone;

        var acquired = await database
            .StringSetAsync(
                key,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                cooldown,
                When.NotExists)
            .ConfigureAwait(false);

        if (acquired)
        {
            return null;
        }

        var remaining = await database.KeyTimeToLiveAsync(key).ConfigureAwait(false);

        return remaining ?? cooldown;
    }
}
