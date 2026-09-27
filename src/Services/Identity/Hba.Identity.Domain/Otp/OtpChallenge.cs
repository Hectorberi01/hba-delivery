using Hba.BuildingBlocks.Domain;
using Hba.Identity.Domain.Exceptions;

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
    public Guid Id { get; }
    public string Phone { get; }
    public string CodeHash { get; }
    public OtpIntent Intent { get; }
    public string? DeviceId { get; }
    public bool Eligible { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset ExpiresAt { get; }
    public int Attempts { get; private set; }
    public const int MaxAttempts = 5;
    public bool IsExhausted => Attempts >= MaxAttempts;


    // Constructeur
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



    /// <summary>
    /// L'identifiant est fourni par l'appelant : l'empreinte du code en dépend,
    /// il faut donc le connaître avant de hacher.
    /// </summary>
    public static OtpChallenge Create(Guid id, string phone, string codeHash, OtpIntent intent, string? deviceId, bool eligible, DateTimeOffset now, TimeSpan lifetime)
    {
        return new OtpChallenge(id, phone, codeHash, intent, deviceId, eligible, now, now.Add(lifetime), attempts: 0);
    }

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
            throw new DomainException(IdentityErrorCodes.OtpExpired, "Ce code a expiré. Demandez-en un nouveau.");
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
