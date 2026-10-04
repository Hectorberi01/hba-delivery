using Hba.BuildingBlocks.Application.Messaging;
using Hba.Driver.Application.Common.Interfaces;
using Hba.Driver.Application.Common.Views;
using Hba.Driver.Domain.Drivers;

namespace Hba.Driver.Application.Features.Drivers.Queries;

/// <summary>
/// « Ces livreurs-la, tels que la carte du client a le droit de les montrer. »
/// </summary>
///
/// <remarks>
/// C'EST LA MOITIE D'UNE CARTE, ET L'AUTRE MOITIE EST CHEZ LOCATION, exactement
/// comme pour <see cref="FilterAvailableDriversHandler"/>. Ce handler ne sait
/// pas ou sont ces livreurs, et n'a pas a le savoir.
///
/// POURQUOI IL NE SE CONFOND PAS AVEC LE FILTRE. Depuis le 3 octobre 2026 la
/// carte du client montre TOUS les livreurs du rayon, en distinguant ceux qui
/// peuvent prendre la mission (point 24, troisieme revision). Filtrer ecarterait
/// precisement ceux qu'il faut afficher en eteint.
///
/// POURQUOI IL NE SE CONFOND PAS DAVANTAGE AVEC GetDriverSummariesHandler :
/// celui-la rend un nom et un telephone, et il verifie le role parce qu'il les
/// rend. Ici il n'y a rien a proteger de cet ordre — un vehicule et un booleen —
/// et c'est ce qui permet a cette lecture de n'exiger aucun role, comme
/// <see cref="CheckDriverAvailabilityHandler"/>.
///
/// AUCUNE RAISON D'INDISPONIBILITE N'EST RENDUE. « Available = false » couvre la
/// mission en cours, la deconnexion recente et le dossier redevenu invalide. Les
/// distinguer en dirait bien plus sur chacun que ce que la carte demande.
/// </remarks>
public sealed record GetDriverPinsQuery(IReadOnlyCollection<Guid> DriverIds)
    : IQuery<IReadOnlyList<DriverPinView>>;

public sealed class GetDriverPinsHandler(IDriverRepository drivers)
    : IQueryHandler<GetDriverPinsQuery, IReadOnlyList<DriverPinView>>
{
    public async Task<IReadOnlyList<DriverPinView>> HandleAsync(
        GetDriverPinsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.DriverIds.Count == 0)
        {
            // UNE LISTE VIDE REND UNE LISTE VIDE, PAS « TOUS LES LIVREURS ».
            // Meme garde-fou que dans le filtre, et pour la meme raison : selon
            // l'implementation du depot, une liste vide pourrait rendre
            // l'annuaire entier parce que Location n'a trouve personne.
            return [];
        }

        var connus = await drivers
            .FindByIdsAsync([.. query.DriverIds], cancellationToken)
            .ConfigureAwait(false);

        return [.. connus.Select(d => new DriverPinView(d.Id, d.Vehicle.Type, Offrable(d)))];
    }

    /// <summary>Peut-il prendre une mission de colis, maintenant ?</summary>
    ///
    /// <remarks>
    /// LES TROIS CONDITIONS SONT CELLES DU FILTRE, ET ELLES DOIVENT LE RESTER.
    /// Si l'une des deux listes se mettait a dire oui quand l'autre dit non, un
    /// livreur apparaitrait en relief sur la carte du client sans jamais
    /// recevoir d'offre — ou l'inverse. Les deux se lisent cote a cote dans ce
    /// dossier ; toute condition ajoutee a l'une se recopie dans l'autre.
    ///
    /// LE COLIS EST L'HABILITATION RETENUE parce que c'est le socle : c'est ce
    /// que FilterAvailableDrivers prend par defaut, et c'est ce que la carte
    /// d'accueil annonce quand elle montre quelqu'un.
    /// </remarks>
    private static bool Offrable(DriverAggregate livreur)
        => livreur.VerificationStatus == VerificationStatus.Verified
           && livreur.OperationalStatus == OperationalStatus.Available
           && livreur.Habilite(DriverCapability.PackageDelivery);
}
