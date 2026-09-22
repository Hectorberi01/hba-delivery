using FluentAssertions;
using Hba.BuildingBlocks.Domain;
using Hba.Identity.Domain.ValueObjects;
using Xunit;

namespace Hba.Identity.Domain.Tests;

public sealed class PasswordHashTests
{
    [Fact]
    public void Deux_empreintes_du_même_mot_de_passe_diffèrent()
    {
        var first = PasswordHash.FromPlainText("un-mot-de-passe-correct");
        var second = PasswordHash.FromPlainText("un-mot-de-passe-correct");

        first.Encoded.Should().NotBe(second.Encoded, "le sel est tiré au hasard à chaque fois");
        first.Verify("un-mot-de-passe-correct").Should().BeTrue();
        second.Verify("un-mot-de-passe-correct").Should().BeTrue();
    }

    [Fact]
    public void Un_mauvais_mot_de_passe_est_refusé()
    {
        var hash = PasswordHash.FromPlainText("un-mot-de-passe-correct");

        hash.Verify("un-mot-de-passe-incorrect").Should().BeFalse();
        hash.Verify(null).Should().BeFalse();
        hash.Verify(string.Empty).Should().BeFalse();
    }

    [Theory]
    [InlineData("court")]
    [InlineData("")]
    [InlineData(null)]
    public void Un_mot_de_passe_trop_court_est_refusé(string? candidate)
    {
        var act = () => PasswordHash.FromPlainText(candidate);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("WEAK_PASSWORD");
    }

    [Fact]
    public void Une_empreinte_corrompue_refuse_sans_lever()
    {
        var hash = PasswordHash.FromEncoded("n-importe-quoi");

        hash.Verify("un-mot-de-passe-correct").Should().BeFalse();
    }

    [Fact]
    public void Une_empreinte_à_l_itération_courante_n_a_pas_besoin_d_être_refaite()
    {
        PasswordHash.FromPlainText("un-mot-de-passe-correct").NeedsRehash.Should().BeFalse();
        PasswordHash.FromEncoded("pbkdf2-sha256$1000$c2VsMTIzNDU2Nzg=$aGFzaA==").NeedsRehash.Should().BeTrue();
    }
}
