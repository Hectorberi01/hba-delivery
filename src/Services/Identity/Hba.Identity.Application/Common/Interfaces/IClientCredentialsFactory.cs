namespace Hba.Identity.Application.Common.Interfaces;

/// <summary>Génération des identifiants et secrets OAuth2.</summary>
public interface IClientCredentialsFactory
{
    string NewClientId();

    string NewSecret();
}
