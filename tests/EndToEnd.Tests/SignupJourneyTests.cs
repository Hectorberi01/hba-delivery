using System.Net.Http.Json;
using FluentAssertions;
using Grpc.Core;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Grpc;
using Hba.Contracts.Identity.V1;
using Hba.Identity.Domain.Accounts;
using Hba.Identity.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using IdentityRoles = Hba.Identity.Domain.Roles;
using Account = Hba.Identity.Domain.Accounts.Account;

namespace Hba.EndToEnd.Tests;

/// <summary>
/// Le parcours qui casse le plus de choses quand il casse : un numéro inconnu
/// demande un code, le saisit, et repart avec un jeton que les autres services
/// sauront vérifier.
/// </summary>
[Collection(IdentityCollection.Name)]
public sealed class SignupJourneyTests(IdentityHostFixture fixture)
{
    private IdentityService.IdentityServiceClient Client()
        => new(fixture.CreateGrpcChannel());

    private static string NewPhone() => "+229" + Random.Shared.Next(90_000_000, 99_999_999);

    [Fact]
    public async Task Une_inscription_par_SMS_délivre_un_jeton_vérifiable_avec_les_JWKS()
    {
        var identity = Client();
        var phone = NewPhone();

        var challenge = await identity.RequestOtpAsync(new RequestOtpRequest
        {
            Phone = phone,
            Intent = OtpIntent.Customer,
            DeviceId = "test-device",
        });

        challenge.ChallengeId.Should().NotBeNullOrWhiteSpace();
        challenge.ExpiresAt.Should().NotBeNull();

        var pair = await identity.VerifyOtpAsync(new VerifyOtpRequest
        {
            ChallengeId = challenge.ChallengeId,
            Code = IdentityHostFixture.FixedOtpCode,
            DeviceId = "test-device",
            DisplayName = "Hector",
        });

        pair.AccessToken.Should().NotBeNullOrWhiteSpace();
        pair.RefreshToken.Should().NotBeNullOrWhiteSpace();
        pair.Principal.Roles.Should().Equal("customer");
        pair.Principal.Phone.Should().Be(phone);

        // C'est le vrai enjeu : les six autres services valideront ce jeton en
        // allant chercher la clé publique ici. Si ce test passe, la chaîne
        // Identity → JWKS → JwtBearer tient.
        var jwks = await fixture.CreateClient().GetStringAsync("/.well-known/jwks.json");

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(
            pair.AccessToken,
            new TokenValidationParameters
            {
                ValidIssuer = IdentityHostFixture.Issuer,
                ValidAudience = "hba-delivery",
                IssuerSigningKeys = new JsonWebKeySet(jwks).GetSigningKeys(),
                RoleClaimType = "roles",
                NameClaimType = "sub",
            });

        result.IsValid.Should().BeTrue(result.Exception?.Message);
        result.Claims.Should().ContainKey("sub");
    }

    [Fact]
    public async Task Le_document_de_découverte_annonce_l_émetteur_et_les_clés()
    {
        var discovery = await fixture.CreateClient()
            .GetFromJsonAsync<Dictionary<string, object>>("/.well-known/openid-configuration");

        discovery.Should().NotBeNull();
        discovery!["issuer"].ToString().Should().Be(IdentityHostFixture.Issuer);
        discovery["jwks_uri"].ToString().Should().EndWith("/.well-known/jwks.json");
    }

    [Fact]
    public async Task La_demande_de_code_dépose_une_commande_d_envoi_dans_l_Outbox()
    {
        var phone = NewPhone();

        await Client().RequestOtpAsync(new RequestOtpRequest { Phone = phone, Intent = OtpIntent.Customer });

        await fixture.WithDbAsync(async db =>
        {
            var pending = await db.OutboxMessages
                .Where(m => m.Topic == "hba.notification.commands.v1" && m.PartitionKey == phone)
                .ToListAsync();

            pending.Should().ContainSingle();
            pending[0].EventType.Should().Be("hba.notification.v1.SendSms");
            pending[0].PublishedAt.Should().BeNull("aucun courtier ne tourne pendant le test");
        });
    }

    [Fact]
    public async Task Un_second_code_demandé_tout_de_suite_est_refusé()
    {
        var identity = Client();
        var phone = NewPhone();

        await identity.RequestOtpAsync(new RequestOtpRequest { Phone = phone, Intent = OtpIntent.Customer });

        var act = async () => await identity.RequestOtpAsync(
            new RequestOtpRequest { Phone = phone, Intent = OtpIntent.Customer });

        var failure = await act.Should().ThrowAsync<RpcException>();
        failure.Which.Trailers.GetValue(GrpcMetadataKeys.ErrorCode).Should().Be("OTP_RATE_LIMITED");
    }

    [Fact]
    public async Task Un_code_incorrect_est_refusé()
    {
        var identity = Client();

        var challenge = await identity.RequestOtpAsync(
            new RequestOtpRequest { Phone = NewPhone(), Intent = OtpIntent.Customer });

        var act = async () => await identity.VerifyOtpAsync(new VerifyOtpRequest
        {
            ChallengeId = challenge.ChallengeId,
            Code = "000000",
        });

        var failure = await act.Should().ThrowAsync<RpcException>();
        failure.Which.Trailers.GetValue(GrpcMetadataKeys.ErrorCode).Should().Be("INVALID_OTP");
    }

    /// <summary>
    /// Le point qui justifie tout le reste : la route publique ne doit pas
    /// permettre de savoir si un numéro a un compte chez HBA, ni de quel type.
    /// </summary>
    [Fact]
    public async Task Un_numéro_de_commerçant_répond_exactement_comme_un_numéro_inconnu()
    {
        var identity = Client();
        var merchantPhone = NewPhone();
        var unknownPhone = NewPhone();

        await fixture.WithDbAsync(async db =>
        {
            var account = Account.CreateMerchantUser(
                Guid.CreateVersion7(),
                merchantId: Guid.CreateVersion7().ToString(),
                EmailAddress.Create("boutique@hbatechettrade.com"),
                PhoneNumber.Create(merchantPhone),
                "Boutique",
                IdentityRoles.MerchantOwner,
                PasswordHash.FromPlainText("un-mot-de-passe-correct"),
                Actor.Admin("test"),
                DateTimeOffset.UtcNow);

            db.Accounts.Add(account);
            await db.SaveChangesAsync();
        });

        var onMerchant = await identity.RequestOtpAsync(
            new RequestOtpRequest { Phone = merchantPhone, Intent = OtpIntent.Customer });

        var onUnknown = await identity.RequestOtpAsync(
            new RequestOtpRequest { Phone = unknownPhone, Intent = OtpIntent.Customer });

        // Mêmes champs renseignés, mêmes durées : rien ne distingue les deux.
        onMerchant.RetryAfterSeconds.Should().Be(onUnknown.RetryAfterSeconds);
        onMerchant.ChallengeId.Should().NotBeNullOrWhiteSpace();
        onMerchant.ExpiresAt.Should().NotBeNull();

        // Et aucun SMS n'est parti pour le commerçant, donc aucun code ne peut
        // aboutir — avec le message ordinaire, pas un refus révélateur.
        await fixture.WithDbAsync(async db =>
        {
            var sent = await db.OutboxMessages.CountAsync(m => m.PartitionKey == merchantPhone);
            sent.Should().Be(0);
        });

        var act = async () => await identity.VerifyOtpAsync(new VerifyOtpRequest
        {
            ChallengeId = onMerchant.ChallengeId,
            Code = IdentityHostFixture.FixedOtpCode,
        });

        var failure = await act.Should().ThrowAsync<RpcException>();
        failure.Which.Trailers.GetValue(GrpcMetadataKeys.ErrorCode).Should().Be("INVALID_OTP");
    }
}
