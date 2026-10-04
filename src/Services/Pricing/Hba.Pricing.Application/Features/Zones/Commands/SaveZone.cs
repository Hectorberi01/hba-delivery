using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Pricing.Application.Authorization;
using Hba.Pricing.Application.Common.Interfaces;
using Hba.Pricing.Application.Common.Views;
using Hba.Pricing.Domain.Exceptions;
using Hba.Pricing.Domain.ValueObjects;
using Hba.Pricing.Domain.Zones;

namespace Hba.Pricing.Application.Features.Zones.Commands;

/// <summary>
/// Cree une zone, ou remplace le contour de celle qui porte deja ce code.
/// </summary>
///
/// <remarks>
/// UNE ZONE SE REMPLACE, ELLE NE SE VERSIONNE PAS, et c'est la difference avec
/// une grille. Une grille porte de l'argent et un devis emis hier s'en reclame
/// (ADR 0004) : la modifier reecrirait le passe, donc on en cree une version et
/// on clot la precedente. Une zone, elle, porte une FORME. Redessiner
/// Cotonou-centre ne change aucun prix deja facture : un devis a capte le CODE de
/// sa zone, pas son polygone.
///
/// CE QUI CHANGE QUAND MEME, ET IL FAUT LE SAVOIR. Un point qui passe de
/// « peripherie » a « centre » changera de bareme au PROCHAIN devis. Redessiner
/// une zone deplace donc des prix sans toucher a une seule grille — c'est
/// pourquoi l'ecriture est reservee a admin (point 40).
///
/// LA GEOMETRIE EST VERIFIEE PAR LA BASE, PAS ICI. Trois sommets et des
/// coordonnees dans les bornes se verifient en arithmetique, et c'est fait
/// ci-dessous. « Ce contour ne se recoupe pas » demande de la geometrie : seul
/// PostGIS sait le dire (ST_IsValid), et le domaine n'a pas le droit a la
/// dependance qui le permettrait.
/// </remarks>
public sealed record SaveZoneCommand(
    string Code,
    string Name,
    int Priority,
    IReadOnlyList<GeoPoint> Ring) : ICommand<ZoneView>;

public sealed class SaveZoneHandler(
    IZoneRepository zones,
    IZoneRingStore contours,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<SaveZoneCommand, ZoneView>
{
    /// <summary>
    /// Un contour en demande au moins trois, sinon il n'enferme aucune surface.
    /// </summary>
    private const int SommetsMinimum = 3;

    public async Task<ZoneView> HandleAsync(SaveZoneCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        PricingAccess.EnsureCanAdministerZones(caller);

        if (command.Ring is null || command.Ring.Count < SommetsMinimum)
        {
            throw new DomainException(
                PricingErrorCodes.InvalidZoneRing,
                $"Un contour de zone demande au moins {SommetsMinimum} sommets.");
        }

        // LA BASE TRANCHE AVANT QU'ON ECRIVE QUOI QUE CE SOIT. Un contour qui se
        // recoupe — un « 8 », ou deux sommets traverses dans le mauvais ordre —
        // produirait un polygone que ST_Covers refuserait d'evaluer : la zone
        // existerait sans jamais contenir personne, et personne ne saurait
        // pourquoi. Le refus tombe donc ici, avec un message qu'un humain lit.
        var valide = await contours
            .IsValidAsync(command.Ring, cancellationToken)
            .ConfigureAwait(false);

        if (!valide)
        {
            throw new DomainException(
                PricingErrorCodes.InvalidZoneRing,
                "Ce contour n'est pas un polygone valide : il se recoupe, ou deux de "
                + "ses sommets sont confondus. Reprenez le trace.");
        }

        var code = command.Code.Trim().ToLowerInvariant();

        var zone = await zones.GetByCodeAsync(code, cancellationToken).ConfigureAwait(false);

        if (zone is null)
        {
            zone = Zone.Create(Guid.CreateVersion7(), code, command.Name, command.Priority, clock.UtcNow);
            zones.Add(zone);
        }
        else
        {
            zone.Rename(command.Name);
            zone.Reprioritise(command.Priority);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // LE CONTOUR S'ECRIT APRES LA LIGNE, ET L'ORDRE EST VOULU.
        //
        // Les deux ecritures ne sont pas dans la meme transaction : la premiere
        // passe par EF, la seconde est du SQL brut. Si la seconde echoue, il reste
        // une zone SANS contour — qui ne contient aucun point, donc ne s'applique
        // a personne, et que l'ecran montre comme « pas encore dessinee ». L'ordre
        // inverse laisserait un contour orphelin, invisible et sans proprietaire.
        // Des deux echecs possibles, celui-ci se voit et se repare d'un second
        // enregistrement.
        await contours.SaveAsync(zone.Id, command.Ring, cancellationToken).ConfigureAwait(false);

        return ZoneView.From(zone, command.Ring);
    }
}

/// <summary>
/// Active ou desactive une zone.
/// </summary>
///
/// <remarks>
/// DESACTIVER N'EST PAS SUPPRIMER, ET IL N'Y A PAS DE SUPPRESSION. Des grilles et
/// des devis nomment cette zone par son code : l'effacer rendrait illisibles des
/// prix deja factures. Une zone desactivee cesse simplement de contenir des
/// points — ses devis passes restent explicables.
///
/// CE QUE CELA PRODUIT SUR LE CHAMP : les points qu'elle couvrait tombent dans la
/// zone suivante par priorite, ou dans aucune — et un devis sans zone echoue sur
/// ZONE_NOT_COVERED, ce qui est la bonne reponse quand on retire une ville.
/// </remarks>
public sealed record SetZoneActiveCommand(string Code, bool Active) : ICommand<ZoneView>;

public sealed class SetZoneActiveHandler(
    IZoneRepository zones,
    IZoneRingStore contours,
    IUnitOfWork unitOfWork,
    ICallerContext caller) : ICommandHandler<SetZoneActiveCommand, ZoneView>
{
    public async Task<ZoneView> HandleAsync(SetZoneActiveCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        PricingAccess.EnsureCanAdministerZones(caller);

        var code = command.Code.Trim().ToLowerInvariant();

        var zone = await zones.GetByCodeAsync(code, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Zone", code);

        if (command.Active)
        {
            zone.Activate();
        }
        else
        {
            zone.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var contour = await contours.ReadAsync([zone.Id], cancellationToken).ConfigureAwait(false);

        return ZoneView.From(zone, contour.TryGetValue(zone.Id, out var ring) ? ring : []);
    }
}
