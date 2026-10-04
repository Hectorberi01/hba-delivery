using Grpc.Core;
using Hba.BuildingBlocks.Grpc.ServiceAuth;
using Hba.Contracts.Routing.V1;
using Hba.Dispatch.Application.Common.Interfaces;
using Hba.Dispatch.Domain.Dispatching;
using Microsoft.Extensions.Logging;
using DomainGeoPoint = Hba.Dispatch.Domain.ValueObjects.GeoPoint;
using DomainVehicle = Hba.Dispatch.Domain.Dispatching.VehicleType;

namespace Hba.Dispatch.Infrastructure.Services.Routing;

/// <summary>
/// Classe les candidats par temps de trajet reel jusqu'au point de prise en
/// charge. C'est l'etape 5 de l'algorithme de selection.
/// </summary>
///
/// <remarks>
/// POURQUOI LE PLUS PROCHE N'EST PAS LE PLUS RAPIDE. La recherche de proximite
/// ordonne a vol d'oiseau, parce que c'est ce que Redis GEO sait faire. A
/// Cotonou, une lagune, un pont unique ou un sens interdit suffisent a inverser
/// l'ordre : le chauffeur a huit cents metres de l'autre cote de l'eau arrive
/// apres celui qui est a deux kilometres par la route.
///
/// UNE SEULE MATRICE, PAS UN ITINERAIRE PAR CANDIDAT. Cinq appels sequentiels
/// avant la premiere offre feraient attendre le client plus longtemps que le gain
/// ne le vaut. La matrice demande les cinq durees d'un coup.
///
/// IL NE PEUT PAS FAIRE ECHOUER UNE VAGUE, ET C'EST SA PROPRIETE LA PLUS
/// IMPORTANTE. Routing injoignable, en erreur, ou rendant une mesure degradee :
/// on garde l'ordre d'origine, qui est celui de la proximite — exactement le
/// comportement d'avant le 1er octobre 2026. Un classement est une amelioration,
/// pas une dependance.
///
/// UNE MESURE DEGRADEE EST ECARTEE, PAS UTILISEE. Tant que la carte du Benin
/// n'est pas preparee pour OSRM, Routing repond en distance a vol d'oiseau et
/// marque <c>from_fallback</c>. Classer sur cette mesure donnerait exactement
/// l'ordre que Redis GEO a deja rendu, en faisant croire a un classement
/// routier : on prefere ne pas reordonner et le dire dans le journal. C'est la
/// lecture du point 36 appliquee ici.
/// </remarks>
internal sealed class RoutingEtaRanker(
    RoutingService.RoutingServiceClient client,
    IServiceTokenProvider tokens,
    ILogger<RoutingEtaRanker> journal) : IEtaRanker
{
    public async Task<IReadOnlyList<CandidateDriver>> ClasserAsync(
        IReadOnlyList<CandidateDriver> candidats,
        DomainGeoPoint pickup,
        DomainVehicle vehicleType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidats);
        ArgumentNullException.ThrowIfNull(pickup);

        // UN SEUL CANDIDAT N'A RIEN A CLASSER. Appeler Routing pour confirmer
        // l'ordre d'une liste d'un element serait un aller-retour reseau sur le
        // chemin chaud du dispatch, pour aucune decision.
        if (candidats.Count <= 1)
        {
            return candidats;
        }

        var requete = new CalculateMatrixRequest { Profile = Profil(vehicleType) };

        // LES CHAUFFEURS SONT LES ORIGINES, LE RETRAIT EST LA DESTINATION, et le
        // sens compte : on mesure le temps qu'ils mettent a VENIR. L'inverse —
        // une origine, N destinations — mesurerait le temps de repartir du point
        // de retrait, qui n'est pas la question.
        requete.Origins.AddRange(candidats.Select(c => new Hba.Contracts.Common.V1.GeoPoint
        {
            Latitude = c.Latitude,
            Longitude = c.Longitude,
        }));

        requete.Destinations.Add(new Hba.Contracts.Common.V1.GeoPoint
        {
            Latitude = pickup.Latitude,
            Longitude = pickup.Longitude,
        });

        RouteMatrix matrice;

        try
        {
            var entetes = await tokens.AuthorizationAsync(cancellationToken).ConfigureAwait(false);

            matrice = await client
                .CalculateMatrixAsync(requete, headers: entetes, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RpcException exception)
        {
            journal.LogWarning(
                exception,
                "Routing injoignable : les {Nombre} candidats restent classes a vol d'oiseau.",
                candidats.Count);

            return candidats;
        }

        if (matrice.FromFallback)
        {
            journal.LogInformation(
                "Mesure routiere degradee ({Fournisseur}) : aucun reclassement, l'ordre de proximite est conserve.",
                matrice.Provider);

            return candidats;
        }

        if (matrice.Rows.Count != candidats.Count)
        {
            // LA MATRICE NE CORRESPOND PAS A LA QUESTION POSEE. Apparier a
            // l'indice une reponse de taille differente melangerait les
            // chauffeurs : on prefere ne pas classer.
            journal.LogWarning(
                "Matrice incoherente : {Lignes} ligne(s) pour {Candidats} candidat(s). Aucun reclassement.",
                matrice.Rows.Count,
                candidats.Count);

            return candidats;
        }

        var mesures = new List<(CandidateDriver Candidat, int Duree)>(candidats.Count);
        var injoignables = new List<CandidateDriver>();

        for (var i = 0; i < candidats.Count; i++)
        {
            var cellule = matrice.Rows[i].Cells.Count > 0 ? matrice.Rows[i].Cells[0] : null;

            // INJOIGNABLE NE VEUT PAS DIRE « A ZERO SECONDE ». Un point tombe dans
            // la lagune, ou de l'autre cote d'un bras d'eau sans pont, n'a pas
            // d'itineraire : prendre son zero le ferait gagner le classement.
            // On le renvoie en fin de liste sans l'exclure — il reste le seul
            // candidat si les autres manquent.
            if (cellule is null || !cellule.Reachable)
            {
                injoignables.Add(candidats[i]);
                continue;
            }

            mesures.Add((candidats[i], cellule.DurationSeconds));
        }

        if (mesures.Count == 0)
        {
            journal.LogWarning(
                "Aucun des {Nombre} candidats n'a d'itineraire vers le retrait. Ordre de proximite conserve.",
                candidats.Count);

            return candidats;
        }

        return [.. mesures.OrderBy(m => m.Duree).Select(m => m.Candidat), .. injoignables];
    }

    /// <summary>
    /// Le profil de calcul, d'apres le vehicule de la mission.
    /// </summary>
    ///
    /// <remarks>
    /// LE FOURGON ROULE EN PROFIL VOITURE, et le tricycle en profil moto : OSRM
    /// n'en connait que trois. Ce n'est pas exact — un fourgon ne passe pas
    /// partout ou passe une berline — mais c'est l'approximation la moins fausse,
    /// et elle vaut mieux que de renvoyer un profil non precise.
    ///
    /// UN VEHICULE NON PRECISE PASSE EN MOTO, parce que c'est le vehicule par
    /// defaut de toutes les livraisons du depot. Le profil voiture surestimerait
    /// les durees dans un trafic ou la moto se faufile.
    /// </remarks>
    private static RouteProfile Profil(DomainVehicle type) => type switch
    {
        DomainVehicle.Car or DomainVehicle.Van => RouteProfile.Car,
        DomainVehicle.Bicycle => RouteProfile.Bicycle,
        _ => RouteProfile.Motorcycle,
    };
}
