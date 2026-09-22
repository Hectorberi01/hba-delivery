using Hba.BuildingBlocks.Domain;

namespace Hba.Identity.Domain.Otp;

public enum OtpIntent
{
    Customer = 1,
    Driver = 2,
}

/// <summary>
/// Défi de vérification d'un numéro. Il vit dans Redis, pas en base : il dure
/// cinq minutes et ne mérite ni transaction ni sauvegarde.
///
/// L'empreinte du code est stockée, jamais le code. Contrairement à l'OTP de
/// remise d'un colis, que le client doit pouvoir relire, celui-ci n'a besoin
/// d'être lu par personne après son envoi.
/// </summary>
public sealed class OtpChallenge
{
    public const int MaxAttempts = 5;

    private OtpChallenge(
        Guid id,
        string phone,
        string codeHash,
        OtpIntent intent,
        string? deviceId,
        bool eligible,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        int attempts)
    {
        Id = id;
        Phone = phone;
        CodeHash = codeHash;
        Intent = intent;
        DeviceId = deviceId;
        Eligible = eligible;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        Attempts = attempts;
    }

    public Guid Id { get; }

    public string Phone { get; }

    public string CodeHash { get; }

    public OtpIntent Intent { get; }

    public string? DeviceId { get; }

    /// <summary>
    /// Vrai si un code a réellement été envoyé. Faux quand le numéro appartient
    /// à un compte qui ne se connecte pas par SMS, ou à un compte suspendu : un
    /// défi est quand même créé, pour que la réponse soit identique dans tous
    /// les cas, mais aucun code ne part et la vérification ne peut pas aboutir.
    /// </summary>
    public bool Eligible { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset ExpiresAt { get; }

    public int Attempts { get; private set; }

    public bool IsExhausted => Attempts >= MaxAttempts;

    /// <summary>
    /// L'identifiant est fourni par l'appelant : l'empreinte du code en dépend,
    /// il faut donc le connaître avant de hacher.
    /// </summary>
    public static OtpChallenge Create(
        Guid id,
        string phone,
        string codeHash,
        OtpIntent intent,
        string? deviceId,
        bool eligible,
        DateTimeOffset now,
        TimeSpan lifetime)
        => new(id, phone, codeHash, intent, deviceId, eligible, now, now.Add(lifetime), attempts: 0);

    /// <summary>Reconstruction depuis Redis.</summary>
    public static OtpChallenge Restore(
        Guid id,
        string phone,
        string codeHash,
        OtpIntent intent,
        string? deviceId,
        bool eligible,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        int attempts)
        => new(id, phone, codeHash, intent, deviceId, eligible, createdAt, expiresAt, attempts);

    /// <summary>
    /// Compare l'empreinte du code saisi. Le hachage est fait en amont, par
    /// l'infrastructure : le domaine ne choisit pas d'algorithme.
    /// </summary>
    public bool Verify(string candidateHash, DateTimeOffset now)
    {
        if (ExpiresAt <= now)
        {
            throw new DomainException("OTP_EXPIRED", "Ce code a expiré. Demandez-en un nouveau.");
        }

        if (IsExhausted)
        {
            throw new DomainException(
                "OTP_ATTEMPTS_EXCEEDED",
                "Trop de tentatives sur ce code. Demandez-en un nouveau.");
        }

        Attempts++;

        return string.Equals(CodeHash, candidateHash, StringComparison.Ordinal);
    }
}
