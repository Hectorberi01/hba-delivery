namespace Hba.Identity.Application.Common.Interfaces;

/// <summary>
/// Limitation de débit des envois de SMS. Un SMS coûte de l'argent, et un
/// numéro qu'on bombarde est un numéro qu'on harcèle.
/// </summary>
public interface IOtpRateLimiter
{
    // Renvoie null si l'envoi est autorisé, sinon le délai avant de pouvoir redemander un code.
    Task<TimeSpan?> TryAcquireAsync(string phone, CancellationToken cancellationToken);
}
