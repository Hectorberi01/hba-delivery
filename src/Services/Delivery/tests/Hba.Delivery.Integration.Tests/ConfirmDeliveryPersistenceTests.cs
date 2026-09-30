using FluentAssertions;
using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Delivery.Application.Commands.DriverActions;
using Hba.Delivery.Application.Ports;
using Hba.Delivery.Domain.Deliveries;
using Hba.Delivery.Domain.ValueObjects;
using Hba.Delivery.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

// L'alias est un « global using » des projets de production ; il ne traverse pas
// la frontiere d'assemblage, donc il se repose ici.
using DeliveryAggregate = Hba.Delivery.Domain.Deliveries.Delivery;

namespace Hba.Delivery.Integration.Tests;

/// <summary>
/// Le verrou du code de remise, éprouvé à travers une vraie base.
/// </summary>
///
/// <remarks>
/// POURQUOI CES TESTS EXISTENT. Le 29 septembre 2026, le verrou des cinq
/// tentatives était couvert par un test de domaine vert, et ne fonctionnait pas.
/// Le test enchaînait cinq refus sur la même instance en mémoire ; la production,
/// elle, levait « INVALID_OTP » depuis l'agrégat avant que le gestionnaire
/// n'atteigne SaveChanges. Le compteur n'était jamais écrit, repartait de zéro à
/// chaque requête, et un code de six chiffres devenait forçable par le livreur
/// affecté.
///
/// C'EST DONC LE RECHARGEMENT QUI EST L'OBJET DU TEST, pas la règle. Chaque
/// tentative passe par le gestionnaire, un contexte neuf, et une relecture
/// depuis PostgreSQL. Un test qui garderait le même contexte ne prouverait
/// exactement rien — c'était le défaut du précédent.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class ConfirmDeliveryPersistenceTests(PostgresFixture fixture)
{
    private const string DriverId = "driver-otp-1";
    private const string CustomerId = "customer-otp-1";

    private static readonly DateTimeOffset Now = new(2026, 9, 30, 10, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// UNE SEULE MEMOIRE POUR TOUTES LES REQUETES D'UN TEST, et ce détail décide
    /// de ce que deux de ces tests prouvent. En construire une par appel les
    /// aurait rendus verts sans rien démontrer : le rejeu d'une clé ne peut être
    /// éprouvé que si la clé survit à la requête, comme en production. xUnit
    /// crée une instance par test, donc l'isolation entre tests reste entière.
    /// </summary>
    private readonly MemoireDIdempotence _idempotence = new();

    [Fact]
    public async Task Le_compteur_de_tentatives_survit_a_un_code_faux()
    {
        var id = await SemerUneCourseCollecteeAsync();

        await RemettreAsync(id, "000000");

        await using var relecture = fixture.CreateContext();
        var course = await relecture.Deliveries.FirstAsync(d => d.Id == id, CancellationToken.None);

        // LE COEUR DU TEST. Avant la correction, cette valeur était 0 : le refus
        // remontait en exception et rien n'était enregistré.
        course.Otp.FailedAttempts.Should().Be(1);
        course.Status.Should().Be(DeliveryStatus.PickedUp);
    }

    [Fact]
    public async Task Le_code_de_remise_se_verrouille_vraiment_apres_cinq_essais()
    {
        var id = await SemerUneCourseCollecteeAsync();

        // Cinq requêtes distinctes, chacune avec son propre contexte : c'est la
        // séquence qu'un livreur mal intentionné enverrait en boucle.
        for (var essai = 0; essai < 5; essai++)
        {
            var refus = await RemettreAsync(id, "000000");
            refus.Code.Should().Be("INVALID_OTP");
        }

        await using (var controle = fixture.CreateContext())
        {
            var course = await controle.Deliveries.FirstAsync(d => d.Id == id, CancellationToken.None);
            course.Otp.FailedAttempts.Should().Be(5);
            course.Otp.IsLocked.Should().BeTrue();
        }

        // LE BON CODE NE PASSE PLUS, et c'est la seule preuve qui compte : la
        // force brute s'arrête. La remise devient l'affaire du support.
        var bonCode = await CodeAsync(id);
        var verrou = await RemettreAsync(id, bonCode);

        verrou.Code.Should().Be("OTP_LOCKED");

        await using var final = fixture.CreateContext();
        var apres = await final.Deliveries.FirstAsync(d => d.Id == id, CancellationToken.None);
        apres.Status.Should().Be(DeliveryStatus.PickedUp);
    }

    [Fact]
    public async Task Un_code_faux_rejoue_avec_la_meme_cle_reste_un_refus()
    {
        var id = await SemerUneCourseCollecteeAsync();

        // L'application livreur met ses actions en file hors ligne et les rejoue
        // avec la MEME clé d'idempotence. Un refus ne doit jamais devenir un
        // succès par rejeu — sinon il suffirait d'un code faux et d'une relance.
        var premier = await RemettreAsync(id, "000000", cle: "deliver:1");
        var second = await RemettreAsync(id, "000000", cle: "deliver:1");

        premier.Code.Should().Be("INVALID_OTP");
        second.Code.Should().Be("INVALID_OTP");

        await using var relecture = fixture.CreateContext();
        var course = await relecture.Deliveries.FirstAsync(d => d.Id == id, CancellationToken.None);

        // Deux tentatives consommées : le rejeu d'un refus est un vrai essai.
        course.Otp.FailedAttempts.Should().Be(2);
    }

    [Fact]
    public async Task Le_bon_code_cloture_la_course_et_se_rejoue_sans_erreur()
    {
        var id = await SemerUneCourseCollecteeAsync();
        var bonCode = await CodeAsync(id);

        await ExecuterAsync(id, bonCode, cle: "deliver:ok");

        await using (var controle = fixture.CreateContext())
        {
            var course = await controle.Deliveries.FirstAsync(d => d.Id == id, CancellationToken.None);
            course.Status.Should().Be(DeliveryStatus.Delivered);
        }

        // Le rejeu d'un succès reste un succès : c'est la clé mémorisée qui le
        // garantit, et elle ne l'est qu'au succès.
        await ExecuterAsync(id, bonCode, cle: "deliver:ok");
    }

    /// <summary>
    /// Exécute le gestionnaire et rend l'exception métier attendue.
    /// </summary>
    private async Task<DomainException> RemettreAsync(Guid id, string code, string? cle = null)
    {
        var leve = await Record.ExceptionAsync(() => ExecuterAsync(id, code, cle));

        leve.Should().NotBeNull("un code refusé doit remonter une erreur métier");
        return leve.Should().BeAssignableTo<DomainException>().Subject;
    }

    /// <summary>
    /// UN CONTEXTE NEUF A CHAQUE APPEL, et c'est tout l'intérêt : l'agrégat est
    /// relu depuis PostgreSQL, comme le ferait une nouvelle requête HTTP.
    /// </summary>
    private async Task ExecuterAsync(Guid id, string code, string? cle)
    {
        await using var contexte = fixture.CreateContext();

        var gestionnaire = new ConfirmDeliveryHandler(
            new DepotDeTest(contexte),
            _idempotence,
            contexte,
            new LivreurDeTest(DriverId),
            new HorlogeFigee(Now.AddMinutes(30)));

        // Arguments nommes : ConfirmDeliveryCommand porte la cle AVANT la preuve,
        // et les deux sont des « string? ». Les intervertir passerait la cle en
        // preuve de livraison sans qu'aucun compilateur ne s'en plaigne.
        await gestionnaire.HandleAsync(
            new ConfirmDeliveryCommand(
                DeliveryId: id,
                Otp: code,
                OccurredAt: null,
                IdempotencyKey: cle,
                ProofObjectKey: null),
            CancellationToken.None);
    }

    private async Task<string> CodeAsync(Guid id)
    {
        await using var contexte = fixture.CreateContext();
        var course = await contexte.Deliveries.FirstAsync(d => d.Id == id, CancellationToken.None);
        return course.Otp.Code;
    }

    private async Task<Guid> SemerUneCourseCollecteeAsync()
    {
        var course = Domain.Deliveries.Delivery.Create(
            Guid.CreateVersion7(),
            $"HBA-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}",
            DeliverySource.ClientApp,
            "hba-internal",
            externalOrderId: null,
            customerId: CustomerId,
            merchantId: null,
            pickupPointId: null,
            Location.Create(GeoPoint.Create(6.3703, 2.3912), "Carré 442", "+22997000001", "Expéditeur"),
            Location.Create(GeoPoint.Create(6.3654, 2.4183), "Immeuble bleu", "+22997000002", "Destinataire"),
            Recipient.Create("Destinataire", "+22997000002"),
            PricingSnapshot.Create(
                "quote-otp",
                "2026-09",
                MoneyXof.From(1500),
                MoneyXof.From(800),
                MoneyXof.From(700),
                MoneyXof.Zero,
                MoneyXof.From(1100),
                3400,
                720,
                Now),
            "Colis",
            1200,
            Actor.Customer(CustomerId),
            Now);

        var livreur = Actor.Driver(DriverId);

        course.ConfirmPayment("pi-otp", Actor.FedaPay, Now.AddMinutes(1));
        course.StartDriverSearch(Actor.DispatchEngine, Now.AddMinutes(2));
        course.AssignDriver(
            AssignedDriver.Create(DriverId, "Koffi A.", "+22997000003", VehicleType.Motorcycle, "AB-1234-RB"),
            "offer-otp",
            Actor.DispatchEngine,
            Now.AddMinutes(3));
        course.MarkArrivedAtPickup(livreur, Now.AddMinutes(10));
        course.MarkPickedUp(livreur, Now.AddMinutes(12));

        await using var contexte = fixture.CreateContext();
        contexte.Deliveries.Add(course);
        await contexte.SaveChangesAsync(CancellationToken.None);

        return course.Id;
    }

    /// <summary>
    /// Le dépôt réel est interne à Infrastructure. Celui-ci lit le MEME contexte,
    /// donc le même suivi de changements : c'est ce qui compte pour ce test.
    /// </summary>
    private sealed class DepotDeTest(DeliveryDbContext contexte) : IDeliveryRepository
    {
        public Task<DeliveryAggregate?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
            => contexte.Deliveries.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

        public Task<DeliveryAggregate?> GetByExternalOrderIdAsync(
            string partnerId,
            string externalOrderId,
            CancellationToken cancellationToken)
            => contexte.Deliveries.FirstOrDefaultAsync(
                d => d.PartnerId == partnerId && d.ExternalOrderId == externalOrderId,
                cancellationToken);

        public Task<IReadOnlyList<DeliveryAggregate>> ListAsync(
            DeliveryQueryFilter filter,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("Hors périmètre de ces tests.");

        public Task<IReadOnlyList<DeliveryAggregate>> ListUnpaidBeforeAsync(
            DateTimeOffset limite,
            int batchSize,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("Hors périmètre de ces tests.");

        public Task<(int Count, long BilledTotal)> SumBilledForCustomerAsync(
            string customerId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException("Hors périmètre de ces tests.");

        public void Add(DeliveryAggregate delivery) => contexte.Deliveries.Add(delivery);
    }

    /// <summary>
    /// La mémoire d'idempotence, partagée par toutes les requêtes d'un test —
    /// c'est le rôle du magasin réel. Ce qu'elle sert à vérifier : un REFUS n'y
    /// laisse rien, un succès oui.
    /// </summary>
    private sealed class MemoireDIdempotence : IIdempotencyStore
    {
        private readonly Dictionary<string, string> _connues = [];

        public Task<string?> TryGetResultAsync(string scope, string idempotencyKey, CancellationToken cancellationToken)
            => Task.FromResult(_connues.TryGetValue($"{scope}:{idempotencyKey}", out var valeur) ? valeur : null);

        public Task RememberAsync(
            string scope,
            string idempotencyKey,
            string resourceId,
            CancellationToken cancellationToken)
        {
            _connues[$"{scope}:{idempotencyKey}"] = resourceId;
            return Task.CompletedTask;
        }
    }

    private sealed class LivreurDeTest(string driverId) : ICallerContext
    {
        public bool IsAuthenticated => true;

        public string SubjectId => driverId;

        public IReadOnlySet<string> Roles { get; } = new HashSet<string>(StringComparer.Ordinal)
        {
            HbaRoles.Driver,
        };

        public string? MerchantId => null;

        public string? PartnerId => null;

        public string? DriverId => driverId;

        public string? DisplayName => "Koffi A.";

        public string? Phone => "+22997000003";

        public string? Email => null;

        public string? TraceId => null;

        public string? CorrelationId => null;

        public Actor ToActor() => Actor.Driver(driverId);

        public bool IsInRole(string role) => Roles.Contains(role);
    }

    private sealed class HorlogeFigee(DateTimeOffset instant) : IClock
    {
        public DateTimeOffset UtcNow => instant;
    }
}
