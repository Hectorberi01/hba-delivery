using Hba.BuildingBlocks.Security;
using Hba.Contracts.Payment.V1;

namespace Hba.Gateway.Endpoints.Web;

/// <summary>
/// La file des versements aux livreurs, pour la finance.
///
/// LE GROUPE EST CELUI DU BACK-OFFICE, MAIS LE SERVICE N'ADMET QUE finance ET
/// admin. La politique de groupe ouvre a quatre roles ; instruire un versement
/// n'en concerne que deux, et c'est Payment qui tranche — « toute autorisation
/// se verifie cote service ». Une politique de plus ici ferait croire que la
/// passerelle decide, et le jour ou les deux listes divergeraient, c'est la
/// mauvaise qui gagnerait dans l'esprit du lecteur.
///
/// CE QUE CETTE PASSERELLE NE FAIT PAS : elle n'ouvre aucune demande. Une
/// demande de versement est un acte de volonte du livreur, et la route pour
/// cela vit du cote livreur. La finance instruit ce qui lui arrive.
/// </summary>
public static class AdminPayoutEndpoints
{
    public static IEndpointRouteBuilder MapAdminPayoutEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/admin/v1/payouts").RequireAuthorization(HbaPolicies.BackOffice);

        // SANS « status », LA FILE D'ATTENTE. Demandees et approuvees
        // ensemble, la plus ancienne d'abord : une demande approuvee dont le
        // virement n'a jamais ete consigne est precisement celle qu'on oublie.
        group.MapGet("", async (
            PaymentService.PaymentServiceClient payments,
            CancellationToken cancellationToken,
            string? status = null,
            int limit = 0) =>
        {
            var requete = new ListPayoutRequestsRequest { Limit = limit };

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!TryLire(status, out var etat))
                {
                    return Results.BadRequest(new { code = "INVALID_PAYOUT_STATUS", status });
                }

                requete.Status = etat;
            }

            var liste = await payments.ListPayoutRequestsAsync(
                requete,
                cancellationToken: cancellationToken);

            return Results.Ok(new { payouts = liste.Payouts.Select(Rendre) });
        });

        group.MapPost("/{id}/approve", async (
            string id,
            PaymentService.PaymentServiceClient payments,
            CancellationToken cancellationToken) =>
        {
            var demande = await payments.ApprovePayoutAsync(
                new ApprovePayoutRequest { PayoutId = id },
                cancellationToken: cancellationToken);

            return Results.Ok(Rendre(demande));
        });

        // LE MOTIF EST EXIGE DES L'ENTREE, comme pour les autres gestes
        // sensibles du back-office. Le domaine le refuse aussi, et les deux
        // controles ont leur raison : celui-ci evite un aller-retour reseau
        // pour rien, celui du domaine tient quel que soit le chemin.
        group.MapPost("/{id}/reject", async (
            string id,
            RejetVersementDto body,
            PaymentService.PaymentServiceClient payments,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(body.Reason))
            {
                return Results.BadRequest(new { code = "MISSING_REJECTION_REASON" });
            }

            var demande = await payments.RejectPayoutAsync(
                new RejectPayoutRequest { PayoutId = id, Reason = body.Reason },
                cancellationToken: cancellationToken);

            return Results.Ok(Rendre(demande));
        });

        // LE SEUL GESTE QUI DEBITE LE COMPTE DU LIVREUR. La reference n'est pas
        // une formalite : c'est ce qu'on montrera le jour ou il dira n'avoir
        // rien recu.
        group.MapPost("/{id}/paid", async (
            string id,
            VersementDto body,
            PaymentService.PaymentServiceClient payments,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(body.PaymentReference))
            {
                return Results.BadRequest(new { code = "MISSING_PAYMENT_REFERENCE" });
            }

            var demande = await payments.MarkPayoutPaidAsync(
                new MarkPayoutPaidRequest { PayoutId = id, PaymentReference = body.PaymentReference },
                cancellationToken: cancellationToken);

            return Results.Ok(Rendre(demande));
        });

        // LE RELEVE D'UN LIVREUR, VU DU BACK-OFFICE.
        //
        // POURQUOI CETTE ROUTE EXISTE ICI ET PAS DANS L'ANNUAIRE : instruire
        // une demande sans voir le compte revient a approuver un chiffre nu.
        // Le service n'admet que finance et admin, comme les quatre routes
        // ci-dessus — l'annuaire des livreurs, lui, est ouvert au support.
        //
        // CE N'EST PAS UNE VERIFICATION DU MONTANT. Le service a deja refuse
        // au-dela du disponible au moment de la demande, et rien ne peut faire
        // baisser un solde entre-temps : un credit ne s'efface pas, et un seul
        // versement est en cours a la fois. Cette lecture sert a COMPRENDRE le
        // dossier, pas a le recalculer.
        var releves = app.MapGroup("/api/admin/v1/drivers").RequireAuthorization(HbaPolicies.BackOffice);

        releves.MapGet("/{driverId}/earnings", async (
            string driverId,
            PaymentService.PaymentServiceClient payments,
            CancellationToken cancellationToken,
            int limit = 0) =>
        {
            var releve = await payments.GetDriverStatementAsync(
                new GetDriverStatementRequest { DriverId = driverId, Limit = limit },
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                driverId = releve.DriverId,
                earnedXof = releve.EarnedXof,
                paidOutXof = releve.PaidOutXof,
                dueXof = releve.DueXof,
                totalEntries = releve.TotalEntries,
                entries = releve.Entries.Select(entry => new
                {
                    id = entry.Id,
                    kind = Nommer(entry.Kind),
                    direction = Nommer(entry.Direction),
                    amountXof = entry.AmountXof,
                    deliveryId = entry.DeliveryId,
                    deliveryReference = entry.DeliveryReference,
                    payoutId = entry.PayoutId,
                    occurredAt = entry.OccurredAt?.ToDateTimeOffset(),
                }),
            });
        });

        return app;
    }

    private static string Nommer(LedgerEntryKind kind) => kind switch
    {
        LedgerEntryKind.DeliveryEarning => "LEDGER_ENTRY_KIND_DELIVERY_EARNING",
        LedgerEntryKind.Payout => "LEDGER_ENTRY_KIND_PAYOUT",
        _ => "LEDGER_ENTRY_KIND_UNSPECIFIED",
    };

    private static string Nommer(LedgerDirection direction) => direction switch
    {
        LedgerDirection.Credit => "LEDGER_DIRECTION_CREDIT",
        LedgerDirection.Debit => "LEDGER_DIRECTION_DEBIT",
        _ => "LEDGER_DIRECTION_UNSPECIFIED",
    };

    /// <summary>
    /// LE BACK-OFFICE VOIT « decidedBy », LE LIVREUR NON — c'est la seule
    /// difference avec la mise en forme du cote livreur, et elle est ici parce
    /// que c'est ici qu'on sait a qui l'on parle.
    /// </summary>
    private static object Rendre(PayoutRequest demande) => new
    {
        id = demande.Id,
        driverId = demande.DriverId,
        amountXof = demande.AmountXof,
        status = Nommer(demande.Status),
        requestedAt = demande.RequestedAt?.ToDateTimeOffset(),
        decidedAt = demande.DecidedAt?.ToDateTimeOffset(),
        decidedBy = demande.DecidedBy,
        rejectionReason = demande.RejectionReason,
        paidAt = demande.PaidAt?.ToDateTimeOffset(),
        paymentReference = demande.PaymentReference,
    };

    // LES NOMS DU CONTRAT, PAS CEUX DU C# GENERE. Un switch plutot que la
    // reflexion : si une valeur s'ajoute au contrat, celui-ci cesse de
    // compiler, la ou la reflexion rendrait une chaine vide en silence.
    private static string Nommer(PayoutStatus statut) => statut switch
    {
        PayoutStatus.Requested => "PAYOUT_STATUS_REQUESTED",
        PayoutStatus.Approved => "PAYOUT_STATUS_APPROVED",
        PayoutStatus.Paid => "PAYOUT_STATUS_PAID",
        PayoutStatus.Rejected => "PAYOUT_STATUS_REJECTED",
        _ => "PAYOUT_STATUS_UNSPECIFIED",
    };

    /// <summary>
    /// UN ETAT INCONNU EST UNE ERREUR DE L'APPELANT, PAS « TOUT ». Tomber
    /// silencieusement sur la file d'attente quand la console envoie un nom
    /// mal orthographie donnerait un ecran qui affiche autre chose que ce que
    /// son filtre annonce, et personne ne s'en apercevrait.
    /// </summary>
    private static bool TryLire(string brut, out PayoutStatus statut)
    {
        statut = brut.Trim().ToUpperInvariant() switch
        {
            "PAYOUT_STATUS_REQUESTED" or "REQUESTED" => PayoutStatus.Requested,
            "PAYOUT_STATUS_APPROVED" or "APPROVED" => PayoutStatus.Approved,
            "PAYOUT_STATUS_PAID" or "PAID" => PayoutStatus.Paid,
            "PAYOUT_STATUS_REJECTED" or "REJECTED" => PayoutStatus.Rejected,
            _ => PayoutStatus.Unspecified,
        };

        return statut != PayoutStatus.Unspecified;
    }
}

public sealed record RejetVersementDto(string Reason);

public sealed record VersementDto(string PaymentReference);
