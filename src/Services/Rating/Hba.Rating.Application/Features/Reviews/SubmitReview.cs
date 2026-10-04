using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Rating.Application.Common;
using Hba.Rating.Domain.Reviews;

namespace Hba.Rating.Application.Features.Reviews;

/// <summary>Le client donne sa note.</summary>
public sealed record SubmitReviewCommand(
    MissionType MissionType,
    Guid ReferenceId,
    int Stars,
    string? Comment) : ICommand<ReviewView>;

public sealed class SubmitReviewHandler(
    IReviewRepository reviews,
    IUnitOfWork unitOfWork,
    ICallerContext caller,
    IClock clock) : ICommandHandler<SubmitReviewCommand, ReviewView>
{
    public async Task<ReviewView> HandleAsync(
        SubmitReviewCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var review = await reviews
            .GetAsync(command.MissionType, command.ReferenceId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("Notation", command.ReferenceId.ToString());

        // VERIFICATION COTE SERVICE, JAMAIS COTE APPELANT (ADR 0007). Un client
        // ne note pas la mission d'un autre : le jeton dit qui il est, et la
        // notation porte le client que l'evenement de fin de mission a nomme.
        //
        // LE BACK-OFFICE NE PASSE PAS NON PLUS. Noter a la place de quelqu'un
        // fabriquerait une reputation, ce qui est exactement ce qu'un systeme
        // d'avis ne doit pas permettre — meme a un administrateur.
        if (!string.Equals(caller.SubjectId, review.CustomerId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Cette mission n'est pas la votre.");
        }

        review.Soumettre(command.Stars, command.Comment, clock.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ReviewView.From(review);
    }
}
