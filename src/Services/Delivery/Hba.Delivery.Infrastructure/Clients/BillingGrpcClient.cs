using Grpc.Core;
using Hba.BuildingBlocks.Grpc;
using Hba.BuildingBlocks.Grpc.ServiceAuth;
using Hba.Contracts.Billing.V1;
using Hba.Delivery.Application.Ports;
using Hba.Delivery.Domain.ValueObjects;
using CommonMoney = Hba.Contracts.Common.V1.Money;

namespace Hba.Delivery.Infrastructure.Clients;

/// <summary>
/// Accès au service Billing.
/// </summary>
///
/// <remarks>
/// DEUX APPELS, DEUX IDENTITÉS, ET CE N'EST PAS UNE INCOHÉRENCE.
///
/// LE DÉBIT PART AVEC LE JETON DU DONNEUR D'ORDRE, reporté par
/// TokenForwardingInterceptor. C'est ce qui permet à Billing de vérifier
/// lui-même que celui qui débite est bien le titulaire du compte, au lieu de
/// croire Delivery sur parole.
///
/// L'ANNULATION PART AVEC LE JETON DE SERVICE, et il a fallu une faille pour le
/// comprendre. La clé d'un débit est l'identifiant de la course, rendu au
/// donneur d'ordre : tant que l'annulation était ouverte au titulaire, il
/// pouvait se faire rembourser une course EN COURS et la garder. Seul le
/// système sait qu'une création a échoué, donc c'est le système qui annule.
/// </remarks>
internal sealed class BillingGrpcClient(
    BillingService.BillingServiceClient client,
    IServiceTokenProvider tokens) : IBillingClient
{
    public async Task<string> DebitAsync(
        string ownerType,
        string ownerId,
        MoneyXof amount,
        string reference,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(amount);

        // AUCUN try/catch ICI, ET C'EST DELIBERE. Un refus de Billing —
        // INSUFFICIENT_BALANCE, compte suspendu — doit remonter tel quel
        // jusqu'au client : c'est le seul message qui lui dise quoi faire.
        // L'intercepteur d'exceptions traduit deja les codes gRPC en fautes de
        // domaine ; les avaler ici les remplacerait par « une erreur est
        // survenue ».
        var mouvement = await client.DebitAsync(
            new DebitRequest
            {
                Owner = new AccountOwner { OwnerType = ownerType, OwnerId = ownerId },
                Amount = new CommonMoney { Amount = amount.Amount, Currency = MoneyXof.CurrencyCode },
                Reference = reference,
                IdempotencyKey = idempotencyKey,
            },
            cancellationToken: cancellationToken);

        return mouvement.Id;
    }

    public async Task<bool> AccountExistsAsync(
        string ownerType,
        string ownerId,
        CancellationToken cancellationToken)
    {
        try
        {
            // LE JETON DU DONNEUR D'ORDRE SUFFIT, et c'est le bon : BillingAccess
            // laisse un titulaire lire son propre compte. Pas besoin du jeton de
            // service ici, donc on ne s'en sert pas — un privilege qu'on peut ne
            // pas prendre est un privilege qu'on ne prend pas.
            await client.GetAccountAsync(
                new GetAccountRequest
                {
                    Owner = new AccountOwner { OwnerType = ownerType, OwnerId = ownerId },
                },
                cancellationToken: cancellationToken);

            return true;
        }
        catch (RpcException erreur) when (erreur.StatusCode == StatusCode.NotFound)
        {
            // BILLING REPOND « INTROUVABLE » DANS DEUX CAS, et les deux valent
            // faux ici : le compte n'existe pas, ou l'appelant n'est pas son
            // titulaire — BillingAccess refuse la lecture en NotFound pour qu'on
            // ne puisse pas enumerer les comptes. Un donneur d'ordre qui
            // demanderait un compte qui n'est pas le sien n'a de toute facon rien
            // a debiter.
            return false;
        }
    }

    public async Task ReverseDebitAsync(
        string ownerType,
        string ownerId,
        string debitIdempotencyKey,
        CancellationToken cancellationToken)
    {
        // « GetTokenAsync » ET NON « AuthorizationAsync », ET C'EST TOUT LE
        // POINT DE CETTE METHODE.
        //
        // AuthorizationAsync rend des en-tetes VIDES quand l'appel descend d'une
        // requete utilisateur : elle laisse deliberement passer le jeton de la
        // personne, parce que c'est presque toujours ce qu'il faut. Ici c'est
        // l'inverse. Cette compensation naît bien d'une requete — la creation de
        // course qui vient d'echouer — mais ce n'est PAS le donneur d'ordre qui
        // demande l'annulation, c'est le systeme qui constate que la course
        // n'existe pas. Lui laisser reporter son jeton rouvrirait la faille :
        // avec l'identifiant de sa course, il se remboursait et la gardait.
        //
        // L'INTERCEPTEUR NE L'ECRASERA PAS : TokenForwardingInterceptor n'ajoute
        // l'en-tete que s'il est ABSENT. Le poser ici gagne, et c'est ce qui rend
        // ce retournement possible sans toucher a l'intercepteur.
        var jeton = await tokens.GetTokenAsync(cancellationToken).ConfigureAwait(false);

        var entetes = new Metadata { { GrpcMetadataKeys.Authorization, $"Bearer {jeton}" } };

        // PAS DE try/catch ICI NON PLUS. L'appelant compense dans un bloc qui
        // journalise et qui relance l'exception d'origine : c'est lui qui sait
        // que l'echec de la compensation ne doit pas remplacer l'echec initial.
        await client.ReverseDebitAsync(
            new ReverseDebitRequest
            {
                Owner = new AccountOwner { OwnerType = ownerType, OwnerId = ownerId },
                DebitIdempotencyKey = debitIdempotencyKey,
            },
            headers: entetes,
            cancellationToken: cancellationToken);
    }
}
