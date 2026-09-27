using System.Globalization;
using System.Text.RegularExpressions;
using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Domain;
using Microsoft.Extensions.Options;

namespace Hba.BuildingBlocks.Application.Time;

public sealed partial class TimeCalendar : ITimeCalendar
{
    private readonly IClock _clock;
    private readonly TimeOptions _options;

    public TimeCalendar(IOptions<TimeOptions> options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options.Value;
        _clock = clock;

        // LE DEMARRAGE ECHOUE PLUTOT QUE DE DEVINER. Un fuseau absent de
        // l'image — tzdata manquant, identifiant mal orthographié — ferait
        // sinon retomber silencieusement sur UTC : les journées seraient
        // décalées d'une heure, tous les totaux resteraient plausibles, et
        // rien ne le signalerait.
        if (!ZoneIdAutorise().IsMatch(_options.ZoneId))
        {
            throw new InvalidOperationException(
                $"Time:ZoneId « {_options.ZoneId} » contient des caractères interdits. "
                + "Cette valeur est écrite dans du SQL (AT TIME ZONE) : seuls les identifiants "
                + "IANA sont acceptés, par exemple Africa/Porto-Novo.");
        }

        try
        {
            Zone = TimeZoneInfo.FindSystemTimeZoneById(_options.ZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException(
                $"Fuseau Time:ZoneId « {_options.ZoneId} » introuvable sur cet hôte. "
                + "Vérifiez l'identifiant, et que le paquet tzdata est présent dans l'image.",
                exception);
        }
    }

    public TimeZoneInfo Zone { get; }

    public string ZoneId => _options.ZoneId;

    public string Key(DateTimeOffset instant, TimeGranularity granularity)
    {
        var local = TimeZoneInfo.ConvertTime(instant, Zone);

        return granularity == TimeGranularity.Month
            ? local.ToString("yyyy-MM", CultureInfo.InvariantCulture)
            : local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public TimeWindow Validate(TimeWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window.Duration > TimeSpan.FromDays(_options.MaxWindowDays))
        {
            throw new DomainException(
                "TIME_WINDOW_TOO_WIDE",
                $"La fenêtre demandée dépasse {_options.MaxWindowDays} jours.");
        }

        return window;
    }

    public TimeWindow LastDays(int days)
    {
        var nombre = Math.Max(days, 1);
        var aujourdhui = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.UtcNow, Zone).DateTime);

        return TimeWindow.Create(
            DebutDuJour(aujourdhui.AddDays(-(nombre - 1))),
            DebutDuJour(aujourdhui.AddDays(1)));
    }

    public TimeWindow Default() => LastDays(_options.DefaultWindowDays);

    public TimeWindow CurrentMonth()
    {
        var local = TimeZoneInfo.ConvertTime(_clock.UtcNow, Zone);
        var premier = new DateOnly(local.Year, local.Month, 1);

        return TimeWindow.Create(DebutDuJour(premier), DebutDuJour(premier.AddMonths(1)));
    }

    public IReadOnlyList<string> Keys(TimeWindow window, TimeGranularity granularity)
    {
        ArgumentNullException.ThrowIfNull(window);

        var cles = new List<string>();
        var local = TimeZoneInfo.ConvertTime(window.From, Zone);

        if (granularity == TimeGranularity.Month)
        {
            var mois = new DateOnly(local.Year, local.Month, 1);

            while (DebutDuJour(mois) < window.To)
            {
                cles.Add(mois.ToString("yyyy-MM", CultureInfo.InvariantCulture));
                mois = mois.AddMonths(1);
            }

            return cles;
        }

        var jour = DateOnly.FromDateTime(local.DateTime);

        while (DebutDuJour(jour) < window.To)
        {
            cles.Add(jour.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            jour = jour.AddDays(1);
        }

        return cles;
    }

    /// <summary>
    /// Minuit local d'une date, rendu en instant absolu.
    ///
    /// GetUtcOffset EST INTERROGE POUR CETTE DATE-LA, pas pour aujourd'hui :
    /// le Bénin n'a pas d'heure d'été, mais ce calendrier sert aussi aux pays
    /// de l'UEMOA, et un décalage figé fausserait une fenêtre à cheval sur un
    /// changement d'heure.
    /// </summary>
    private DateTimeOffset DebutDuJour(DateOnly date)
    {
        var minuit = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(minuit, Zone.GetUtcOffset(minuit));
    }

    [GeneratedRegex(@"^[A-Za-z0-9_+\-/]{1,64}$")]
    private static partial Regex ZoneIdAutorise();
}
