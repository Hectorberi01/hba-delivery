using Hba.BuildingBlocks.Security;
using Hba.Contracts.Billing.V1;
using CommonMoney = Hba.Contracts.Common.V1.Money;

namespace Hba.Gateway.Endpoints.Web;

/// <summary>
/// Les comptes de facturation des donneurs d'ordre, pour la finance.
/// </summary>
///
/// <remarks>
/// CES TROIS ROUTES N'EXISTAIENT PAS, ET LEUR ABSENCE BOUCHAIT TOUT LE B2B.
///
/// « OpenAccount » et « Credit » n'etaient exposes que sur le gRPC interne de
/// Billing, que le proxy ne route pas : aucun chemin, meme manuel, ne permettait
/// d'ouvrir un compte ni d'y porter une recharge. La premiere course de tout
/// partenaire echouait donc en « compte de facturation introuvable » — et son
/// devis etait deja consomme. La decision du 29 septembre 2026 — « recharge a la
/// main par finance, pour commencer » — n'avait litteralement pas de porte.
///
/// LE GROUPE EST CELUI DU BACK-OFFICE, MAIS LE SERVICE N'ADMET QUE finance ET
/// admin. Meme raison que la file des versements : la politique de groupe ecarte
/// ce qui n'a manifestement rien a faire la, et c'est BillingAccess qui tranche.
/// Une politique plus stricte ici ferait croire que la passerelle decide.
///
/// CE QUE CES ROUTES NE FONT PAS : elles ne debitent pas. Un debit est la
/// consequence d'une course, jamais un geste d'exploitation — et BillingAccess le
/// refuse au back-office, faute de cas d'usage.
/// </remarks>
public static class AdminBillingEndpoints
{
    public static IEndpointRouteBuilder MapAdminBillingEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/admin/v1/billing/accounts")
            .RequireAuthorization(HbaPolicies.BackOffice);

        // L'ETAT D'UN COMPTE. Le titulaire se nomme par son couple, dans
        // l'adresse : « merchant/m-42 », « partner/p-7 ». C'est le meme couple
        // partout dans ce service, et il se lit sans rien interroger.
        group.MapGet("/{ownerType}/{ownerId}", async (
            string ownerType,
            string ownerId,
            BillingService.BillingServiceClient billing,
            CancellationToken cancellationToken) =>
        {
            var compte = await billing.GetAccountAsync(
                new GetAccountRequest { Owner = Titulaire(ownerType, ownerId) },
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(compte));
        });

        // OUVRIR UN COMPTE. Toujours en prepaye : le postpaye est un plafond que
        // finance accorde ensuite, a un compte qui existe et dont on a vu le
        // comportement.
        //
        // PAS D'OUVERTURE AUTOMATIQUE AU PREMIER DEBIT, ET C'EST DELIBERE. Ce
        // serait commode — cela supprimerait le 404 de la premiere course — mais
        // un compte de facturation accompagne un contrat commercial. En ouvrir un
        // parce qu'une course est arrivee reviendrait a faire credit a quelqu'un
        // que personne n'a accepte.
        group.MapPost("", async (
            OuvertureDto body,
            BillingService.BillingServiceClient billing,
            CancellationToken cancellationToken) =>
        {
            if (!EstUnTypeConnu(body.OwnerType))
            {
                return Results.BadRequest(new { code = "INVALID_OWNER_TYPE", ownerType = body.OwnerType });
            }

            var compte = await billing.OpenAccountAsync(
                new OpenAccountRequest
                {
                    Owner = Titulaire(body.OwnerType, body.OwnerId),

                    // LE SEUIL D'ALERTE EST LE SEUL REGLAGE A L'OUVERTURE. Zero
                    // est accepte par le domaine et veut dire « ne previens
                    // jamais » ; la passerelle ne le refuse pas, parce que ce
                    // n'est pas a elle de decider ce qu'un seuil doit valoir.
                    LowBalanceThreshold = Montant(body.LowBalanceThresholdXof),
                },
                cancellationToken: cancellationToken);

            return Results.Ok(Lisible(compte));
        });

        // RECHARGER. C'est finance qui constate un virement recu et le porte au
        // compte : rien dans ce depot ne l'apprend tout seul.
        group.MapPost("/{ownerType}/{ownerId}/credit", async (
            string ownerType,
            string ownerId,
            RechargeDto body,
            BillingService.BillingServiceClient billing,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(body.Reference))
            {
                return Results.BadRequest(new { code = "MISSING_REFERENCE" });
            }

            if (body.AmountXof <= 0)
            {
                return Results.BadRequest(new { code = "INVALID_AMOUNT", amountXof = body.AmountXof });
            }

            var mouvement = await billing.CreditAsync(
                new CreditRequest
                {
                    Owner = Titulaire(ownerType, ownerId),
                    Kind = MovementKind.Topup,
                    Amount = Montant(body.AmountXof),
                    Reference = body.Reference.Trim(),

                    // LA CLE D'IDEMPOTENCE EST DERIVEE DE LA REFERENCE DU
                    // VIREMENT, ET C'EST LA SEULE PROTECTION REELLE CONTRE UNE
                    // DOUBLE RECHARGE.
                    //
                    // La demander a l'operateur reviendrait a lui demander
                    // d'inventer un identifiant, ce qu'il ferait differemment a
                    // chaque essai : deux clics sur « recharger » creeraient deux
                    // recharges. Derivee du virement, la seconde tentative
                    // retrouve le mouvement deja ecrit et ne credite pas une
                    // seconde fois — l'index unique de la table le garantit dans
                    // toute la base, donc un meme virement ne peut pas non plus
                    // etre porte a deux comptes.
                    //
                    // CE QU'ELLE NE PROTEGE PAS : une reference mal recopiee est
                    // un autre virement aux yeux du systeme. C'est pour cela que
                    // l'ecran la fait relire.
                    IdempotencyKey = $"topup:{body.Reference.Trim()}",
                },
                cancellationToken: cancellationToken);

            return Results.Ok(new
            {
                id = mouvement.Id,
                accountId = mouvement.AccountId,
                amountXof = mouvement.Amount?.Amount ?? 0,
                balanceAfterXof = mouvement.BalanceAfter?.Amount ?? 0,
                reference = mouvement.Reference,
                createdAt = mouvement.CreatedAt?.ToDateTimeOffset(),
            });
        });

        return app;
    }

    /// <summary>
    /// Le couple qui designe un compte. « merchant » ou « partner », et rien
    /// d'autre : Billing compare ces chaines a l'octet.
    /// </summary>
    private static AccountOwner Titulaire(string ownerType, string ownerId)
        => new() { OwnerType = ownerType, OwnerId = ownerId };

    /// <summary>
    /// LE TYPE EST VERIFIE A L'OUVERTURE, ET SEULEMENT LA. Un compte ouvert avec
    /// « Partner » au lieu de « partner » serait invisible et indebitable pour son
    /// titulaire, avec un « compte introuvable » incomprehensible : la faute se
    /// commet une fois, a la creation, et elle ne se corrige pas. En lecture, un
    /// type inconnu rend simplement introuvable, ce qui est la bonne reponse.
    /// </summary>
    private static bool EstUnTypeConnu(string? ownerType)
        => ownerType is "merchant" or "partner";

    private static CommonMoney Montant(long francs) => new() { Amount = francs, Currency = "XOF" };

    private static object Lisible(Account compte) => new
    {
        id = compte.Id,
        ownerType = compte.Owner?.OwnerType,
        ownerId = compte.Owner?.OwnerId,
        mode = compte.Mode.ToString(),
        status = compte.Status.ToString(),
        balanceXof = compte.Balance?.Amount ?? 0,
        creditLimitXof = compte.CreditLimit?.Amount ?? 0,

        // CE QUE LE COMPTE PEUT ENCORE PORTER, rendu par le service plutot que
        // laisse a l'addition de l'ecran, qui la ferait un jour de travers.
        availableXof = compte.Available?.Amount ?? 0,
        lowBalanceThresholdXof = compte.LowBalanceThreshold?.Amount ?? 0,
    };
}

/// <summary>Ouverture d'un compte. Le seuil peut valoir zero.</summary>
public sealed record OuvertureDto(string OwnerType, string OwnerId, long LowBalanceThresholdXof);

/// <summary>
/// Une recharge constatee. La reference est celle du virement recu, et elle est
/// obligatoire : c'est elle qui rend la recharge rejouable sans double effet.
/// </summary>
public sealed record RechargeDto(long AmountXof, string Reference);
