using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.Rating.Application.Common;
using Hba.Rating.Domain.Reviews;
using Microsoft.Extensions.Logging;

namespace Hba.Rating.Application.Features.Reviews;

/// <summary>
/// Ouvre la notation d'une mission qui vient de s'achever.
/// </summary>
///
/// <remarks>
/// ELLE VIENT D'UN EVENEMENT, PAS D'UNE APPLICATION. C'est la fin d'une course
/// ou d'une livraison qui l'appelle, et c'est de la que viennent le client et le
/// livreur. Laisser l'application les fournir reviendrait a laisser un client
/// noter quelqu'un qui ne l'a jamais servi.
///
/// IDEMPOTENTE, PARCE QUE KAFKA REJOUE. Un evenement relu ne doit pas ouvrir une
/// seconde notation pour la meme mission : le client en verrait deux a noter, et
/// le livreur recevrait deux fois la meme note.
/// </remarks>
public sealed record OpenReviewCommand(
    MissionType MissionType,
    Guid ReferenceId,
    string CustomerId,
    Guid DriverId,
    DateTimeOffset CompletedAt) : ICommand<ReviewView>;

public sealed class OpenReviewHandler(
    IReviewRepository reviews,
    IUnitOfWork unitOfWork,
    ILogger<OpenReviewHandler> journal) : ICommandHandler<OpenReviewCommand, ReviewView>
{
    public async Task<ReviewView> HandleAsync(
        OpenReviewCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var connue = await reviews
            .GetAsync(command.MissionType, command.ReferenceId, cancellationToken)
            .ConfigureAwait(false);

        if (connue is not null)
        {
            return ReviewView.From(connue);
        }

        var review = Review.Ouvrir(
            Guid.CreateVersion7(),
            command.MissionType,
            command.ReferenceId,
            command.CustomerId,
            command.DriverId,
            command.CompletedAt);

        reviews.Add(review);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        journal.LogInformation(
            "Notation ouverte pour la mission {Nature} {ReferenceId}.",
            command.MissionType,
            command.ReferenceId);

        return ReviewView.From(review);
    }
}
