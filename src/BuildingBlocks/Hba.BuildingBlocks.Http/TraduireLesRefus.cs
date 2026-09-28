using Hba.BuildingBlocks.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

// LES USINGS SONT EXPLICITES ICI, ET ILS NE L'ETAIENT PAS AVANT.
//
// Ce fichier vivait dans un projet « Microsoft.NET.Sdk.Web », qui fournit
// Microsoft.AspNetCore.Http et Microsoft.Extensions.Logging en usings
// IMPLICITES. En le deplacant vers une bibliotheque « Microsoft.NET.Sdk », le
// code est parti sans ce que le SDK lui donnait gratuitement : cinq types
// introuvables, dans un fichier qui n'avait pas change d'une ligne.

namespace Hba.BuildingBlocks.Http;

/// <summary>
/// Traduit une exception du domaine en réponse HTTP.
///
/// LES ROUTES gRPC ONT LEUR INTERCEPTEUR ; CELLES-CI N'AVAIENT RIEN. Un
/// dossier suspendu, un format refusé, une pièce déposée sur un dossier déjà
/// validé : toutes ces règles lèvent une DomainException, et sans ce filtre
/// elles sortaient en 500 avec un corps vide. La passerelle relaie ce corps
/// tel quel au livreur — qui voyait donc « une erreur est survenue » là où le
/// service avait une phrase exploitable à lui dire.
///
/// IL A QUITTE LE SERVICE DRIVER LE 28 SEPTEMBRE 2026, quand Media est devenu
/// le second service a exposer des routes HTTP. Rien dedans n'etait propre au
/// livreur : c'etait une traduction d'exceptions de domaine, et la copier
/// aurait garanti qu'un jour les deux ne rendent plus le meme code pour la meme
/// regle refusee.
///
/// LES CODES SUIVENT CEUX DE L'INTERCEPTEUR gRPC, à dessein : la même règle
/// refusée doit produire le même code, qu'on l'atteigne par gRPC ou par cette
/// poignée de routes HTTP.
/// </summary>
public sealed class TraduireLesRefus(ILogger<TraduireLesRefus> journal) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext contexte,
        EndpointFilterDelegate suivant)
    {
        ArgumentNullException.ThrowIfNull(contexte);
        ArgumentNullException.ThrowIfNull(suivant);

        try
        {
            return await suivant(contexte).ConfigureAwait(false);
        }
        catch (NotFoundException exception)
        {
            return Refus(StatusCodes.Status404NotFound, exception);
        }
        catch (ForbiddenException exception)
        {
            return Refus(StatusCodes.Status403Forbidden, exception);
        }
        catch (DomainException exception)
        {
            journal.LogInformation(
                "Regle metier refusee sur {Chemin} : {Code} — {Message}",
                contexte.HttpContext.Request.Path,
                exception.Code,
                exception.Message);

            // 409 ET NON 400 : la requête est bien formée, c'est l'état du
            // dossier qui s'y oppose. Un 400 ferait croire au client qu'il a
            // mal écrit sa demande et qu'il doit la corriger.
            return Refus(
                exception.Code == "VALIDATION_FAILED"
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status409Conflict,
                exception);
        }
    }

    private static IResult Refus(int statut, DomainException exception) =>
        Results.Json(
            new { code = exception.Code, message = exception.Message },
            statusCode: statut);
}
