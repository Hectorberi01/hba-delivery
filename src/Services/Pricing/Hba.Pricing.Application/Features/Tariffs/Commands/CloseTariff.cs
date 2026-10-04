using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.Pricing.Application.Authorization;
using Hba.Pricing.Application.Common.Interfaces;
using Hba.Pricing.Application.Common.Views;
using Hba.Pricing.Domain.Exceptions;

namespace Hba.Pricing.Application.Features.Tariffs.Commands;

/// <summary>
/// Clot une grille sans la remplacer.
/// </summary>
///
/// <remarks>
/// CE N'EST PAS LE CHEMIN ORDINAIRE, ET IL FAUT SAVOIR CE QU'IL LAISSE DERRIERE
/// LUI. Un changement de prix passe par CreateTariff, qui clot l'ancienne
/// version en creant la nouvelle : il n'y a alors aucun instant sans grille.
/// Cette commande-ci ne cree rien. Apres elle, le couple (zone, vehicule) n'a
/// PLUS DE PRIX, et toute course qui le concerne echoue sur NO_TARIFF.
///
/// C'EST PARFOIS EXACTEMENT CE QU'ON VEUT : retirer le tricycle d'une ville ou
/// il n'est pas autorise, fermer une zone qu'on ne dessert plus. Le refus est
/// alors la bonne reponse, et il vaut mieux qu'un prix invente.
///
/// LA GRILLE N'EST PAS SUPPRIMEE. Elle garde sa date de prise d'effet et gagne
/// une date de fin : les devis emis pendant qu'elle valait restent lisibles
/// (ADR 0004). Aucune commande de ce service ne supprime une grille.
/// </remarks>
public sealed record CloseTariffCommand(Guid TariffId, DateTimeOffset? At) : ICommand<TariffView>;

public sealed class CloseTariffHandler(
    ITariffRepository tariffs,
    ICallerContext caller,
    IUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<CloseTariffCommand, TariffView>
{
    public async Task<TariffView> HandleAsync(
        CloseTariffCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        PricingAccess.EnsureCanAdministerTariffs(caller);

        var grille = await tariffs
            .GetByIdAsync(command.TariffId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("Grille tarifaire", command.TariffId.ToString());

        // CLORE DEUX FOIS N'EST PAS ANODIN : la seconde cloture deplacerait la
        // date de fin, donc changerait retroactivement la periode pendant
        // laquelle cette grille a fait foi. On refuse plutot que d'ecraser.
        if (grille.ValidUntil is not null)
        {
            throw new DomainException(
                PricingErrorCodes.TariffAlreadyClosed,
                "Cette grille est deja close.");
        }

        // Tariff.Close refuse une date anterieure a la prise d'effet : une
        // grille ne peut pas finir avant d'avoir commence.
        grille.Close(command.At ?? clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return TariffView.From(grille);
    }
}
