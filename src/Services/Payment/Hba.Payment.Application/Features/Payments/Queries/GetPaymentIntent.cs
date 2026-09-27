using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Payment.Application.Common.Interfaces;
using Hba.Payment.Application.Common.Views;

namespace Hba.Payment.Application.Features.Payments.Queries;

public sealed record GetPaymentIntentQuery(Guid PaymentIntentId) : IQuery<PaymentIntentView>;

public sealed class GetPaymentIntentHandler(
    IPaymentIntentRepository intents,
    ICallerContext caller) : IQueryHandler<GetPaymentIntentQuery, PaymentIntentView>
{
    public async Task<PaymentIntentView> HandleAsync(
        GetPaymentIntentQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var intent = await intents.GetByIdAsync(query.PaymentIntentId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Intention de paiement", query.PaymentIntentId.ToString());

        // UN CLIENT NE LIT QUE SES PROPRES PAIEMENTS. La verification est ici,
        // dans le service, et pas seulement dans la gateway (ADR 0007).
        if (caller.IsInRole(HbaRoles.Customer)
            && !string.Equals(caller.SubjectId, intent.PayerId, StringComparison.Ordinal))
        {
            throw new ForbiddenException("Ce paiement n'est pas le votre.");
        }

        return PaymentIntentView.From(intent);
    }
}
