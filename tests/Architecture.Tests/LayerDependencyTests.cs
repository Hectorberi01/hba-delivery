using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Hba.Architecture.Tests;

/// <summary>
/// Les règles de dépendance ne se rappellent pas en revue de code : elles se
/// vérifient. Ces tests échouent si une couche se met à connaître une couche
/// qu'elle n'a pas à connaître.
/// </summary>
public sealed class LayerDependencyTests
{
    private static readonly Assembly DomainAssembly = typeof(Hba.Delivery.Domain.Deliveries.Delivery).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Hba.Delivery.Application.PartnerIds).Assembly;
    private static readonly Assembly InfrastructureAssembly =
        typeof(Hba.Delivery.Infrastructure.Persistence.DeliveryDbContext).Assembly;

    [Fact]
    public void Le_domaine_ignore_toute_infrastructure()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "Confluent.Kafka",
                "Grpc",
                "Google.Protobuf",
                "StackExchange.Redis",
                "Microsoft.AspNetCore")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Le_domaine_ignore_la_couche_Application()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOn("Hba.Delivery.Application")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty();
    }

    [Fact]
    public void La_couche_Application_ignore_l_infrastructure_concrète()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Hba.Delivery.Infrastructure",
                "Hba.Delivery.Api",
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "StackExchange.Redis")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty();
    }

    [Fact]
    public void L_infrastructure_ne_remonte_pas_vers_l_hôte()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn("Hba.Delivery.Api")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Les_agrégats_sont_scellés()
    {
        var result = Types.InAssembly(DomainAssembly)
            .That()
            .Inherit(typeof(BuildingBlocks.Domain.AggregateRoot))
            .Should()
            .BeSealed()
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Les_handlers_de_commande_sont_scellés()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveNameEndingWith("Handler")
            .And()
            .AreClasses()
            .And()
            .AreNotAbstract()
            .Should()
            .BeSealed()
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty();
    }
}
