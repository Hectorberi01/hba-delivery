using FluentAssertions;
using Hba.BuildingBlocks.Security;
using Xunit;
using IdentityRoles = Hba.Identity.Domain.Roles;

namespace Hba.Architecture.Tests;

/// <summary>
/// Les rôles sont déclarés à deux endroits : dans Hba.Identity.Domain, qui ne
/// peut pas dépendre d'ASP.NET Core, et dans Hba.BuildingBlocks.Security, que
/// tous les services utilisent. La duplication est assumée ; sa divergence ne
/// l'est pas.
/// </summary>
public sealed class RoleConsistencyTests
{
    [Fact]
    public void Les_deux_listes_de_rôles_sont_identiques()
    {
        IdentityRoles.All.Should().BeEquivalentTo(HbaRoles.All);
    }

    [Fact]
    public void Les_rôles_du_back_office_sont_les_mêmes_des_deux_côtés()
    {
        IdentityRoles.BackOffice.Should().BeEquivalentTo(HbaRoles.BackOffice);
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("driver")]
    [InlineData("merchant_owner")]
    [InlineData("merchant_staff")]
    [InlineData("partner")]
    [InlineData("admin")]
    [InlineData("ops")]
    [InlineData("support")]
    [InlineData("finance")]
    public void Les_noms_du_référentiel_acteurs_sont_repris_tels_quels(string role)
    {
        // Le référentiel impose ces neuf chaînes, dans le code, les claims JWT
        // et la documentation. Aucune variante n'est tolérée.
        HbaRoles.All.Should().Contain(role);
        IdentityRoles.All.Should().Contain(role);
    }
}
