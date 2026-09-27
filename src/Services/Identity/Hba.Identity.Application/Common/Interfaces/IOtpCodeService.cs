namespace Hba.Identity.Application.Common.Interfaces;

/// <summary>Génération et hachage des codes à usage unique.</summary>
public interface IOtpCodeService
{
    string GenerateCode();

    /// <summary>
    /// Empreinte liée au défi : le même code pour deux défis donne deux
    /// empreintes différentes.
    /// </summary>
    string Hash(Guid challengeId, string code);
}
