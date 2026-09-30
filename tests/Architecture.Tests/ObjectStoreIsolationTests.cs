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
        typeof(Hba.Driver.Application.Common.Interfaces.ObjectKeys).Assembly;

    /// <summary>
    /// Le PORT, depuis qu'il a quitté Driver pour le socle partagé.
    /// </summary>
    private static readonly Assembly StorageAbstractions =
        typeof(Hba.BuildingBlocks.Storage.IObjectStore).Assembly;

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

    /// <summary>
    /// LE PORT PARTAGÉ NE VOIT PAS LE SDK, ET C'EST CE QUI REND LA PROMESSE
    /// VÉRIFIABLE.
    ///
    /// Le 28 septembre 2026, le port et l'adaptateur sont montés dans le socle
    /// commun. Le motif écrit ici était « pour que Directory écrive la photo
    /// d'un client » : ce n'est PLUS VRAI depuis le même jour. Le point 27 a
    /// tranché pour un service Media, et Directory ne touche aucun stockage —
    /// il garde un identifiant de média et rien d'autre. Ce sont Media et
    /// Driver qui écrivent, et c'est pour eux deux que le socle est partagé.
    /// Dans un premier temps port et adaptateur ont partagé UN projet —
    /// et le test ci-dessus serait passé quand même, parce qu'il inspecte les
    /// types de l'assembly et non ses références : toute couche Application
    /// référençant ce projet aurait vu Minio par transitivité, sans qu'aucun
    /// test ne s'en plaigne. Une garantie vérifiée par accident n'en est pas
    /// une. D'où deux projets, et ce test-ci sur celui qui porte le port.
    /// </summary>
    [Fact]
    public void Le_port_de_stockage_partagé_ignore_le_SDK()
    {
        var result = Types.InAssembly(StorageAbstractions)
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
