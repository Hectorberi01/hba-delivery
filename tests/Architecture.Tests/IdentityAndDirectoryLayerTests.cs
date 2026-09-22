using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Hba.Architecture.Tests;

/// <summary>
/// Mêmes règles de couches que pour Delivery, appliquées à Identity, Directory
/// et Notification. Elles ne valent que si elles sont vérifiées pour tous les
/// services, pas seulement pour le premier écrit.
/// </summary>
public sealed class IdentityAndDirectoryLayerTests
{
    private static readonly Assembly IdentityDomain = typeof(Hba.Identity.Domain.Roles).Assembly;
    private static readonly Assembly IdentityApplication =
        typeof(Hba.Identity.Application.Authentication.RequestOtpCommand).Assembly;
    private static readonly Assembly DirectoryDomain = typeof(Hba.Directory.Domain.Customers.Customer).Assembly;
    private static readonly Assembly DirectoryApplication =
        typeof(Hba.Directory.Application.Customers.GetCustomerQuery).Assembly;
    private static readonly Assembly NotificationDomain =
        typeof(Hba.Notification.Domain.Templates.TemplateCatalog).Assembly;
    private static readonly Assembly NotificationApplication =
        typeof(Hba.Notification.Application.Sending.SendNotificationCommand).Assembly;

    [Theory]
    [MemberData(nameof(Domains))]
    public void Un_domaine_ignore_toute_infrastructure(string name, Assembly assembly)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "Confluent.Kafka",
                "Grpc",
                "Google.Protobuf",
                "StackExchange.Redis",
                "Microsoft.AspNetCore",
                "Microsoft.IdentityModel")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty($"{name} doit rester un domaine pur");
    }

    [Theory]
    [MemberData(nameof(Applications))]
    public void Une_couche_Application_ignore_l_infrastructure_concrète(string name, Assembly assembly)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "StackExchange.Redis",
                "Microsoft.IdentityModel")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty($"{name} ne doit connaître que ses ports");
    }

    [Fact]
    public void Le_domaine_Identity_ne_dépend_pas_des_briques_de_sécurité_ASP_NET()
    {
        // C'est la raison d'être de la duplication des rôles : le domaine ne
        // doit pas tirer ASP.NET Core derrière lui.
        var result = Types.InAssembly(IdentityDomain)
            .ShouldNot()
            .HaveDependencyOn("Hba.BuildingBlocks.Security")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty();
    }

    public static TheoryData<string, Assembly> Domains() => new()
    {
        { "Identity.Domain", IdentityDomain },
        { "Directory.Domain", DirectoryDomain },
        { "Notification.Domain", NotificationDomain },
    };

    public static TheoryData<string, Assembly> Applications() => new()
    {
        { "Identity.Application", IdentityApplication },
        { "Directory.Application", DirectoryApplication },
        { "Notification.Application", NotificationApplication },
    };
}
