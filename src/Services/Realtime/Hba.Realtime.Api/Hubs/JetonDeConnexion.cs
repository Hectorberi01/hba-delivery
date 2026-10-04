using Grpc.Core;
using Hba.BuildingBlocks.Grpc;
using Microsoft.AspNetCore.SignalR;

namespace Hba.Realtime.Api.Hubs;

/// <summary>
/// Le jeton de l'abonne, tel qu'il faut le reporter aux services.
/// </summary>
///
/// <remarks>
/// POURQUOI CE FICHIER EXISTE, ALORS QUE LE DEPOT A DEJA TokenForwardingInterceptor.
///
/// Cet intercepteur lit <c>HttpContext.Request.Headers.Authorization</c>, ce qui
/// marche pour toute requete HTTP ordinaire. Mais une connexion WebSocket NE PORTE
/// PAS D'EN-TETE AUTHORIZATION : le navigateur ne permet pas d'en poser sur
/// l'ouverture d'un WebSocket, et la convention de SignalR est donc de passer le
/// jeton dans la CHAINE DE REQUETE, sous « access_token ». L'intercepteur ne trouve
/// alors rien, et l'appel sortant part sans identite — le service appele repond
/// « non authentifie », et l'abonnement echoue pour une raison que personne ne
/// comprend en lisant les journaux.
///
/// ON LE POSE DONC EXPLICITEMENT, et l'intercepteur ne le remplace pas : il ne pose
/// l'en-tete que s'il est absent.
///
/// LE JETON N'EST NI LU NI VALIDE ICI. Il est deja valide — le hub est en
/// [Authorize], donc la connexion n'existe pas sans jeton valable. Ce fichier ne
/// fait que le transporter, tel quel, pour que le service proprietaire refasse SA
/// verification (ADR 0007). C'est ce qui evite d'ecrire ici une deuxieme fois la
/// regle « cette mission est-elle la sienne », qui finirait par diverger de celle du
/// service.
/// </remarks>
internal static class JetonDeConnexion
{
    /// <summary>
    /// Les metadonnees a joindre a un appel gRPC fait pour le compte de l'abonne.
    /// Vide quand aucun jeton n'est lisible — l'appel partira sans identite et le
    /// service le refusera, ce qui est le bon echec.
    /// </summary>
    public static Metadata PourAppel(HubCallerContext contexte)
    {
        ArgumentNullException.ThrowIfNull(contexte);

        var brut = Brut(contexte);

        return string.IsNullOrWhiteSpace(brut)
            ? []
            : new Metadata { { GrpcMetadataKeys.Authorization, brut } };
    }

    private static string? Brut(HubCallerContext contexte)
    {
        var http = contexte.GetHttpContext();

        if (http is null)
        {
            return null;
        }

        var entete = http.Request.Headers.Authorization.ToString();

        if (!string.IsNullOrWhiteSpace(entete))
        {
            // UN CLIENT QUI PEUT POSER L'EN-TETE LE FAIT, et c'est le cas du transport
            // de repli en requetes longues : SignalR y passe par des requetes HTTP
            // ordinaires, qui portent l'en-tete normalement. On le prefere donc a la
            // chaine de requete quand il est la.
            return entete;
        }

        var jeton = http.Request.Query["access_token"].ToString();

        return string.IsNullOrWhiteSpace(jeton) ? null : $"Bearer {jeton}";
    }
}
