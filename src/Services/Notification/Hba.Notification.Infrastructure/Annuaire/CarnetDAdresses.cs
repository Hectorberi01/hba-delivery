using Grpc.Core;
using Hba.BuildingBlocks.Grpc.ServiceAuth;
using Hba.Contracts.Directory.V1;
using Hba.Notification.Application.Ports;
using Microsoft.Extensions.Logging;

namespace Hba.Notification.Infrastructure.Annuaire;

/// <summary>
/// Demande à Directory l'adresse d'un compte.
/// </summary>
///
/// <remarks>
/// L'APPEL PART AVEC UN JETON DE SERVICE, PAS CELUI D'UN UTILISATEUR. Il
/// descend d'un consommateur Kafka : aucune personne ne l'a déclenché, et il
/// n'y a donc aucun jeton à reporter.
///
/// CE COMMENTAIRE DISAIT QUE « AddHbaGrpcClient » LE POSAIT. C'ÉTAIT FAUX, ET
/// AUCUN REÇU NE PARTAIT. AddHbaGrpcClient n'ajoute que la corrélation, le
/// report du jeton utilisateur — inopérant ici, faute de HttpContext — et le
/// délai. L'appel partait donc nu, Directory est en [Authorize], la réponse
/// était « non authentifié », et le catch ci-dessous la rendait indistinguable
/// d'un client sans adresse : l'envoi était consigné « le compte n'a pas de
/// courriel », motif faux qui masquait la cause pour de bon.
///
/// LE JETON EST DONC DEMANDÉ ET POSÉ À LA MAIN, comme le font déjà
/// DriverGrpcDirectory (Delivery) et DriverFinder (Dispatch). Il faut que
/// ServiceToken:ClientId et ClientSecret soient renseignés pour Notification —
/// ils le sont dans son compose — et que le même secret figure côté Identity,
/// sous ServiceClients__Clients__notification.
///
/// LE JETON PORTE LE RÔLE « service », ajouté au référentiel le 30 septembre
/// 2026. Sans rôle, il passait l'authentification et échouait sur la règle
/// d'autorisation de Directory, qui n'accorde une fiche qu'à son titulaire ou au
/// back-office — un cran plus loin, mais tout aussi muet.
///
/// TOUTE PANNE REND NULL PLUTOT QUE DE LEVER. L'appelant traite « pas
/// d'adresse » comme le cas courant qu'il est, et consigne l'envoi en
/// « ignoré ». Lever ferait rejouer le message par l'Inbox — utile pour une
/// panne passagère, désastreux pour un client qui n'a simplement pas de
/// courriel, et l'un ne se distingue pas de l'autre depuis ici.
///
/// C'est un compromis assumé : une panne de Directory pendant l'envoi fait
/// perdre le reçu, pas la course. Le journal de Notification garde la trace
/// « ignoré », qui dira pourquoi.
/// </remarks>
public sealed class CarnetDAdresses(
    DirectoryService.DirectoryServiceClient annuaire,
    IServiceTokenProvider jetons,
    ILogger<CarnetDAdresses> journal) : ICarnetDAdresses
{
    public async Task<Adresse> CourrielAsync(Guid subjectId, CancellationToken cancellationToken)
    {
        try
        {
            var entetes = await jetons.AuthorizationAsync(cancellationToken).ConfigureAwait(false);

            var fiche = await annuaire.GetCustomerAsync(
                new GetCustomerRequest { CustomerId = subjectId.ToString() },
                headers: entetes,
                cancellationToken: cancellationToken);

            return Adresse.De(fiche.Email);
        }
        catch (RpcException erreur) when (erreur.StatusCode == StatusCode.NotFound)
        {
            // Une fiche absente n'est pas une panne : le compte peut avoir ete
            // efface entre la course et l'envoi du recu.
            return Adresse.Absente;
        }
        catch (RpcException erreur)
        {
            journal.LogWarning(
                erreur,
                "Directory n'a pas rendu l'adresse du compte {Sujet} ({Statut}) : le message ne partira pas.",
                subjectId,
                erreur.StatusCode);

            // « INJOIGNABLE » ET NON « ABSENTE ». C'est cette distinction qui
            // manquait : l'appelant consignait « le compte n'a pas de
            // courriel » pour une panne d'annuaire, et personne ne pouvait plus
            // voir la vraie cause dans les journaux.
            return Adresse.Injoignable;
        }
    }
}
