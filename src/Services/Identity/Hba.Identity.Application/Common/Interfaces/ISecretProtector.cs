namespace Hba.Identity.Application.Common.Interfaces;

/// <summary>
/// Chiffrement réversible, pour les seules données qui doivent être relues :
/// aujourd'hui, le secret de signature des webhooks partenaires.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plainText);

    string Unprotect(string protectedText);
}
