using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Hba.Architecture.Tests;

/// <summary>
/// L'ADR 0021 appuie toute sa réversibilité sur une phrase : le choix du
/// moteur de stockage reste ouvert « tant que rien n'importe le SDK ailleurs
/// que dans l'adaptateur ».
///
/// UNE PROMESSE QUE RIEN NE VÉRIFIE N'EN EST PAS UNE. Le jour où un handler
/// appellera `MinioClient` en direct pour aller vite, l'ADR deviendra faux
/// sans que personne ne s'en aperçoive — jusqu'à la migration, où l'on
/// découvrira le coût réel. Ces tests font échouer la compilation à la place.
/// </summary>
public sealed class ObjectStoreIsolationTests
{
    private static readonly Assembly DriverDomain =
        typeof(Hba.Driver.Domain.Drivers.DriverAggregate).Assembly;

    private static readonly Assembly DriverApplication =
        typeof(Hba.Driver.Application.Common.Interfaces.IObjectStore).Assembly;

    [Fact]
    public void Le_domaine_Driver_ignore_toute_infrastructure()
    {
        var result = Types.InAssembly(DriverDomain)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "Confluent.Kafka",
                "Grpc",
                "Google.Protobuf",
                "StackExchange.Redis",
                "Minio",
                "Microsoft.AspNetCore")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty();
    }

    [Fact]
    public void La_couche_Application_de_Driver_ignore_le_SDK_de_stockage()
    {
        var result = Types.InAssembly(DriverApplication)
            .ShouldNot()
            .HaveDependencyOn("Minio")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty();
    }

    [Fact]
    public void Le_domaine_Driver_ignore_la_couche_Application()
    {
        var result = Types.InAssembly(DriverDomain)
            .ShouldNot()
            .HaveDependencyOn("Hba.Driver.Application")
            .GetResult();

        result.FailingTypeNames.Should().BeNullOrEmpty();
    }
}
