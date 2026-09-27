using Hba.BuildingBlocks.Domain;

namespace Hba.BuildingBlocks.Application.Time;

/// <summary>
/// Fenêtre de lecture : deux instants absolus, borne basse INCLUSE et borne
/// haute EXCLUE.
///
/// LE DEMI-OUVERT N'EST PAS UNE COQUETTERIE. Avec deux bornes incluses, une
/// course créée exactement à minuit appartient à la veille et au lendemain :
/// la somme de douze mois dépasse l'année, et personne ne voit d'où vient
/// l'écart. Avec le demi-ouvert, deux fenêtres consécutives pavent le temps
/// sans trou ni recouvrement.
/// </summary>
public sealed record TimeWindow
{
    private TimeWindow(DateTimeOffset from, DateTimeOffset to)
    {
        From = from;
        To = to;
    }

    /// <summary>Premier instant compris dans la fenêtre.</summary>
    public DateTimeOffset From { get; }

    /// <summary>Premier instant HORS de la fenêtre.</summary>
    public DateTimeOffset To { get; }

    public TimeSpan Duration => To - From;

    public static TimeWindow Create(DateTimeOffset from, DateTimeOffset to)
    {
        if (to <= from)
        {
            throw new DomainException(
                "INVALID_TIME_WINDOW",
                "La fin de la fenêtre doit être postérieure à son début.");
        }

        // On normalise en UTC : deux fenêtres identiques écrites avec des
        // décalages différents doivent être égales, et un record compare ses
        // membres tels quels.
        return new TimeWindow(from.ToUniversalTime(), to.ToUniversalTime());
    }

    public bool Contains(DateTimeOffset instant) => instant >= From && instant < To;

    /// <summary>
    /// Fenêtre de même durée, collée juste avant. C'est elle qui donne le
    /// « contre la période précédente » des écarts.
    ///
    /// EN DUREE, PAS EN MOIS CALENDAIRES : comparer février à janvier par
    /// cette méthode compare 28 jours à 28 jours, pas février à janvier. Le
    /// jour où une comparaison mois à mois sera demandée, elle se calculera
    /// sur le calendrier local, pas ici.
    /// </summary>
    public TimeWindow Previous() => new(From - Duration, From);

    public override string ToString() => $"[{From:O} ; {To:O}[";
}
