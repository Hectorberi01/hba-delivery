using System.Collections.Concurrent;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Hba.BuildingBlocks.Application.Time;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Grpc.Time;
using Hba.BuildingBlocks.Security;
using Hba.Contracts.Delivery.V1;
using Hba.Contracts.Dispatch.V1;
using Hba.Contracts.Driver.V1;
using Hba.Contracts.Payment.V1;
using DomainWindow = Hba.BuildingBlocks.Application.Time.TimeWindow;
using ProtoWindow = Hba.Contracts.Common.V1.TimeWindow;

namespace Hba.Gateway.Endpoints.Web;

/// <summary>
/// Les indicateurs du tableau de bord.
///
/// Quatre services répondent chacun sur ce qu'il possède (ADR 0020) ; c'est
/// ici qu'ils sont recollés en une seule réponse.
/// </summary>
public static class KpiEndpoints
{
    /// <summary>
    /// Échéance de chaque appel, plus courte que celle des clients.
    ///
    /// DELIVERY EST CONFIGURE A TRENTE SECONDES parce qu'une création de
    /// course déclenche deux appels sortants derrière lui. Une lecture
    /// d'indicateurs n'a pas cette excuse : attendre une demi-minute qu'un
    /// service lent réponde ferait attendre tout le tableau de bord pour un
    /// seul bloc.
    /// </summary>
    private static readonly TimeSpan Echeance = TimeSpan.FromSeconds(8);

    public static IEndpointRouteBuilder MapKpiEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/admin/v1/kpi").RequireAuthorization(HbaPolicies.BackOffice);

        group.MapGet("", async (
            DeliveryService.DeliveryServiceClient deliveries,
            PaymentService.PaymentServiceClient payments,
            DispatchService.DispatchServiceClient dispatch,
            DriverService.DriverServiceClient drivers,
            ITimeCalendar calendrier,
            ILoggerFactory journaux,
            CancellationToken cancellationToken,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            string? range = null,
            string? granularity = null) =>
        {
            var logger = journaux.CreateLogger("Kpi");

            DomainWindow fenetre;

            try
            {
                fenetre = Resoudre(calendrier, from, to, range);
            }
            catch (DomainException exception)
            {
                return Results.BadRequest(new { code = exception.Code, message = exception.Message });
            }

            var pas = string.Equals(granularity, "month", StringComparison.OrdinalIgnoreCase)
                ? Contracts.Common.V1.TimeGranularity.Month
                : Contracts.Common.V1.TimeGranularity.Day;

            // LA FENETRE EST RESOLUE ICI, PUIS IMPOSEE AUX QUATRE. Chaque
            // service sait calculer sa fenêtre par défaut, et les quatre
            // tomberaient d'accord tant que leur configuration est la même.
            // « Tant que » est précisément le problème : un fuseau qui dérive
            // sur un seul service donnerait quatre blocs couvrant quatre
            // périodes, sans que rien ne le signale.
            var protoFenetre = TimeWindowMapper.ToProto(fenetre);

            var indisponibles = new ConcurrentBag<string>();

            async Task<T?> Tenter<T>(string bloc, Func<Task<T>> appel)
                where T : class
            {
                try
                {
                    return await appel().ConfigureAwait(false);
                }
                catch (RpcException exception)
                {
                    // UN BLOC MANQUANT PLUTOT QU'UNE PAGE VIDE. Un tableau de
                    // bord qui tombe entier parce qu'un service redémarre
                    // n'est pas un tableau de bord. Le nom du bloc absent part
                    // dans la réponse : l'écran peut le dire au lieu
                    // d'afficher zéro, qui se lirait comme une mesure.
                    logger.LogWarning(
                        exception,
                        "Indicateurs indisponibles pour {Bloc} : {Statut}.",
                        bloc,
                        exception.StatusCode);

                    indisponibles.Add(bloc);
                    return null;
                }
            }

            var tCourses = Tenter("deliveries", () => deliveries.GetDeliveryStatsAsync(
                new GetDeliveryStatsRequest { Window = protoFenetre, Granularity = pas },
                deadline: DateTime.UtcNow.Add(Echeance),
                cancellationToken: cancellationToken).ResponseAsync);

            var tPaiements = Tenter("payments", () => payments.GetPaymentStatsAsync(
                new GetPaymentStatsRequest { Window = protoFenetre, Granularity = pas },
                deadline: DateTime.UtcNow.Add(Echeance),
                cancellationToken: cancellationToken).ResponseAsync);

            var tDispatch = Tenter("dispatch", () => dispatch.GetDispatchStatsAsync(
                new GetDispatchStatsRequest { Window = protoFenetre },
                deadline: DateTime.UtcNow.Add(Echeance),
                cancellationToken: cancellationToken).ResponseAsync);

            var tLivreurs = Tenter("drivers", () => drivers.GetDriverStatsAsync(
                new GetDriverStatsRequest { Window = protoFenetre },
                deadline: DateTime.UtcNow.Add(Echeance),
                cancellationToken: cancellationToken).ResponseAsync);

            // EN PARALLELE, PAS EN CHAINE. Quatre appels séquentiels de deux
            // cents millisecondes font presque une seconde de page blanche ;
            // menés ensemble, ils coûtent le plus lent des quatre.
            await Task.WhenAll(tCourses, tPaiements, tDispatch, tLivreurs).ConfigureAwait(false);

            return Results.Ok(new
            {
                window = new { from = fenetre.From, to = fenetre.To },
                granularity = pas == Contracts.Common.V1.TimeGranularity.Month ? "month" : "day",
                deliveries = await tCourses.ConfigureAwait(false),
                payments = await tPaiements.ConfigureAwait(false),
                dispatch = await tDispatch.ConfigureAwait(false),
                drivers = await tLivreurs.ConfigureAwait(false),

                // Les blocs que l'écran ne doit pas lire comme des zéros.
                unavailable = indisponibles.ToArray(),
            });
        });

        // --- Les quatre blocs, un par un ---
        //
        // Ils restent exposés séparément : un écran qui n'a besoin que des
        // livreurs n'a pas à réveiller les trois autres services, et un
        // diagnostic se fait bloc par bloc.

        group.MapGet("/deliveries", async (
            DeliveryService.DeliveryServiceClient deliveries,
            ITimeCalendar calendrier,
            CancellationToken cancellationToken,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            string? range = null,
            string? granularity = null) =>
        {
            var requete = new GetDeliveryStatsRequest
            {
                Granularity = string.Equals(granularity, "month", StringComparison.OrdinalIgnoreCase)
                    ? Contracts.Common.V1.TimeGranularity.Month
                    : Contracts.Common.V1.TimeGranularity.Day,
            };

            return await Servir(
                calendrier,
                from,
                to,
                range,
                async fenetre =>
                {
                    requete.Window = fenetre;
                    return await deliveries
                        .GetDeliveryStatsAsync(requete, cancellationToken: cancellationToken)
                        .ResponseAsync.ConfigureAwait(false);
                }).ConfigureAwait(false);
        });

        group.MapGet("/payments", async (
            PaymentService.PaymentServiceClient payments,
            ITimeCalendar calendrier,
            CancellationToken cancellationToken,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            string? range = null,
            string? granularity = null) =>
        {
            var requete = new GetPaymentStatsRequest
            {
                Granularity = string.Equals(granularity, "month", StringComparison.OrdinalIgnoreCase)
                    ? Contracts.Common.V1.TimeGranularity.Month
                    : Contracts.Common.V1.TimeGranularity.Day,
            };

            return await Servir(
                calendrier,
                from,
                to,
                range,
                async fenetre =>
                {
                    requete.Window = fenetre;
                    return await payments
                        .GetPaymentStatsAsync(requete, cancellationToken: cancellationToken)
                        .ResponseAsync.ConfigureAwait(false);
                }).ConfigureAwait(false);
        });

        group.MapGet("/dispatch", async (
            DispatchService.DispatchServiceClient dispatch,
            ITimeCalendar calendrier,
            CancellationToken cancellationToken,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            string? range = null) =>
            await Servir(
                calendrier,
                from,
                to,
                range,
                async fenetre => await dispatch
                    .GetDispatchStatsAsync(
                        new GetDispatchStatsRequest { Window = fenetre },
                        cancellationToken: cancellationToken)
                    .ResponseAsync.ConfigureAwait(false)).ConfigureAwait(false));

        group.MapGet("/drivers", async (
            DriverService.DriverServiceClient driverService,
            ITimeCalendar calendrier,
            CancellationToken cancellationToken,
            DateTimeOffset? from = null,
            DateTimeOffset? to = null,
            string? range = null) =>
            await Servir(
                calendrier,
                from,
                to,
                range,
                async fenetre => await driverService
                    .GetDriverStatsAsync(
                        new GetDriverStatsRequest { Window = fenetre },
                        cancellationToken: cancellationToken)
                    .ResponseAsync.ConfigureAwait(false)).ConfigureAwait(false));

        return app;
    }

    /// <summary>
    /// Résout la fenêtre, puis sert un bloc. Une fenêtre invalide rend 400 et
    /// non 500 : c'est l'appelant qui s'est trompé, pas le service.
    /// </summary>
    private static async Task<IResult> Servir<T>(
        ITimeCalendar calendrier,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? range,
        Func<ProtoWindow, Task<T>> appel)
    {
        DomainWindow fenetre;

        try
        {
            fenetre = Resoudre(calendrier, from, to, range);
        }
        catch (DomainException exception)
        {
            return Results.BadRequest(new { code = exception.Code, message = exception.Message });
        }

        return Results.Ok(await appel(TimeWindowMapper.ToProto(fenetre)).ConfigureAwait(false));
    }

    /// <summary>
    /// Résout la fenêtre demandée.
    ///
    /// « range » EXISTE POUR QUE LE NAVIGATEUR NE CALCULE PAS DE DATES. Un
    /// poste à Paris et un poste à Cotonou ne commencent pas la journée au
    /// même instant : « les sept derniers jours » calculés côté client
    /// couperait la première barre en deux, et la coupure suivrait le
    /// voyageur. Toute l'arithmétique reste donc dans le fuseau métier
    /// (ADR 0019).
    ///
    /// LES DEUX BORNES OU AUCUNE, pour une plage libre : une seule des deux
    /// laisserait deviner l'autre, et deux appelants devineraient
    /// différemment.
    /// </summary>
    private static DomainWindow Resoudre(
        ITimeCalendar calendrier,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? range)
    {
        if (from is not null && to is not null)
        {
            return calendrier.Validate(DomainWindow.Create(from.Value, to.Value));
        }

        return range?.ToUpperInvariant() switch
        {
            "7D" => calendrier.LastDays(7),
            "30D" => calendrier.LastDays(30),
            "90D" => calendrier.LastDays(90),
            "365D" => calendrier.LastDays(365),
            "MONTH" => calendrier.CurrentMonth(),
            _ => calendrier.Default(),
        };
    }
}
