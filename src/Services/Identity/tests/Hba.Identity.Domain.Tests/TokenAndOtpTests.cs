using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Identity.Domain.Otp;
using Hba.Identity.Domain.Sessions;
using Xunit;

namespace Hba.Identity.Domain.Tests;

public sealed class RefreshTokenTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    private static RefreshToken NewToken() => RefreshToken.Issue(
        Guid.CreateVersion7(),
        "empreinte",
        "device-1",
        Guid.CreateVersion7(),
        Now,
        TimeSpan.FromDays(30));

    [Fact]
    public void Un_jeton_ne_sert_qu_une_fois()
    {
        var token = NewToken();
        token.Consume(Guid.CreateVersion7(), Now.AddMinutes(10));

        var act = () => token.Consume(Guid.CreateVersion7(), Now.AddMinutes(11));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("REFRESH_TOKEN_REUSED");
    }

    [Fact]
    public void Un_jeton_expiré_ou_révoqué_est_refusé()
    {
        var expired = NewToken();
        var act = () => expired.Consume(Guid.CreateVersion7(), Now.AddDays(31));
        act.Should().Throw<ForbiddenException>();

        var revoked = NewToken();
        revoked.Revoke("déconnexion", Now);
        var act2 = () => revoked.Consume(Guid.CreateVersion7(), Now.AddMinutes(1));
        act2.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void Un_jeton_frais_est_actif()
    {
        NewToken().IsActive(Now.AddMinutes(1)).Should().BeTrue();
    }
}

public sealed class OtpChallengeTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 21, 10, 0, 0, TimeSpan.Zero);

    private static OtpChallenge NewChallenge() => OtpChallenge.Create(
        Guid.CreateVersion7(),
        "+22997000001",
        "empreinte-attendue",
        OtpIntent.Customer,
        "device-1",
        eligible: true,
        Now,
        TimeSpan.FromMinutes(5));

    [Fact]
    public void Le_bon_code_passe_et_le_mauvais_compte_une_tentative()
    {
        var challenge = NewChallenge();

        challenge.Verify("autre-empreinte", Now).Should().BeFalse();
        challenge.Attempts.Should().Be(1);

        challenge.Verify("empreinte-attendue", Now).Should().BeTrue();
    }

    [Fact]
    public void Cinq_tentatives_épuisent_le_défi()
    {
        var challenge = NewChallenge();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            challenge.Verify("faux", Now);
        }

        var act = () => challenge.Verify("empreinte-attendue", Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("OTP_ATTEMPTS_EXCEEDED");
    }

    [Fact]
    public void Un_défi_expiré_est_refusé()
    {
        var challenge = NewChallenge();

        var act = () => challenge.Verify("empreinte-attendue", Now.AddMinutes(6));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("OTP_EXPIRED");
    }
}
