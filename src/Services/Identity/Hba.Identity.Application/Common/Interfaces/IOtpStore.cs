using Hba.Identity.Domain.Otp;

namespace Hba.Identity.Application.Common.Interfaces;

/// <summary>
/// Stockage des défis OTP. Redis : cinq minutes de durée de vie, aucune valeur
/// à conserver après.
/// </summary>
public interface IOtpStore
{
    Task SaveAsync(OtpChallenge challenge, CancellationToken cancellationToken);

    Task<OtpChallenge?> GetAsync(Guid challengeId, CancellationToken cancellationToken);

    Task DeleteAsync(Guid challengeId, CancellationToken cancellationToken);
}
