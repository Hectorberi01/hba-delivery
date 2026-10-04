using Hba.BuildingBlocks.Domain;

namespace Hba.Pricing.Domain.Tariffs;

/// <summary>
/// Plage horaire d'application d'une grille, en heure locale.
/// </summary>
///
/// <remarks>
/// POURQUOI UNE PLAGE SUR LA GRILLE PLUTÔT QU'UNE MAJORATION PROGRAMMÉE. Une
/// grille de nuit a ses propres montants — un plancher plus haut, une part du
/// chauffeur différente — et pas seulement un multiplicateur sur ceux du jour. Un
/// coefficient aurait aussi obligé à écrire la règle de déclenchement dans le code,
/// alors que le point ouvert sur <c>SurgeBasisPoints</c> dit explicitement qu'elle
/// n'est pas tranchée.
///
/// EN MINUTES DEPUIS MINUIT, HEURE LOCALE DE COTONOU (ADR 0019). Les instants
/// circulent en UTC partout ailleurs ; ici la question est « quelle heure est-il
/// pour le chauffeur et le passager », et 22 h à Cotonou n'est pas 22 h UTC. Le
/// même choix est déjà fait par <c>OpeningHours</c> dans Directory.
///
/// UNE PLAGE PEUT PASSER MINUIT, et c'est le cas le plus utile : 22 h → 6 h. Elle
/// s'écrit alors avec un début supérieur à sa fin, et <see cref="Contient"/> teste
/// l'union des deux morceaux au lieu de l'intervalle. Interdire ce cas aurait forcé
/// à découper la nuit en deux grilles, qu'il faudrait garder identiques à la main.
///
/// LES BORNES SONT [début, fin[ . Une plage 0 → 1440 couvre la journée entière,
/// et c'est la valeur qu'ont reçue toutes les grilles existantes.
/// </remarks>
public sealed class TimeBand : ValueObject
{
    /// <summary>Minutes dans une journée. Une plage ne peut pas sortir de là.</summary>
    public const int MinutesParJour = 24 * 60;

    private TimeBand()
    {
    }

    private TimeBand(int fromMinute, int toMinute)
    {
        FromMinute = fromMinute;
        ToMinute = toMinute;
    }

    public int FromMinute { get; private set; }

    public int ToMinute { get; private set; }

    /// <summary>La journée entière : c'est ce que vaut une grille sans plage.</summary>
    public static TimeBand Journee => new(0, MinutesParJour);

    /// <summary>Vrai quand la plage enjambe minuit, donc quand le début suit la fin.</summary>
    public bool PasseMinuit => FromMinute > ToMinute;

    public bool CouvreToutLeJour => FromMinute == 0 && ToMinute == MinutesParJour;

    public static TimeBand Create(int fromMinute, int toMinute)
    {
        if (fromMinute is < 0 or > MinutesParJour || toMinute is < 0 or > MinutesParJour)
        {
            throw new DomainException(
                "INVALID_TIME_BAND",
                $"Une plage horaire tient entre 0 et {MinutesParJour} minutes.");
        }

        // DEUX BORNES ÉGALES NE SONT PAS « TOUTE LA JOURNÉE », c'est une plage VIDE —
        // et une grille qui ne s'applique jamais est un trou tarifaire silencieux :
        // chaque devis de cette zone échouerait sur NO_TARIFF sans que rien ne dise
        // pourquoi. La journée entière s'écrit 0 → 1440.
        if (fromMinute == toMinute)
        {
            throw new DomainException(
                "INVALID_TIME_BAND",
                "Une plage horaire dont les deux bornes sont égales ne s'applique jamais. "
                + "La journée entière s'écrit de 0 à 1440.");
        }

        return new TimeBand(fromMinute, toMinute);
    }

    /// <summary>La plage contient-elle cette heure locale ?</summary>
    public bool Contient(TimeOnly heureLocale)
    {
        var minute = (heureLocale.Hour * 60) + heureLocale.Minute;

        return PasseMinuit
            ? minute >= FromMinute || minute < ToMinute
            : minute >= FromMinute && minute < ToMinute;
    }

    /// <summary>
    /// Deux plages se recouvrent-elles ?
    /// </summary>
    ///
    /// <remarks>
    /// CE TEST EST CE QUI EMPÊCHE DEUX PRIX POUR UNE MÊME MINUTE. Sans lui, une
    /// grille 0→1440 et une grille 22h→6h cohabiteraient sur la même zone et le même
    /// véhicule, et le prix d'une course à 23 h dépendrait de l'ordre de lecture en
    /// base — donc changerait sans raison d'un appel à l'autre.
    ///
    /// IL SE RAMÈNE AUX MINUTES, PAS AUX BORNES. Comparer les bornes de deux plages
    /// dont l'une enjambe minuit demande quatre cas et se trompe au moins une fois ;
    /// chercher une minute commune en demande un seul. Mille quatre cent quarante
    /// itérations ne coûtent rien à la création d'une grille.
    /// </remarks>
    public bool Chevauche(TimeBand autre)
    {
        ArgumentNullException.ThrowIfNull(autre);

        for (var minute = 0; minute < MinutesParJour; minute++)
        {
            var heure = new TimeOnly(minute / 60, minute % 60);

            if (Contient(heure) && autre.Contient(heure))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>« 22:00–06:00 », pour un journal ou un écran d'administration.</summary>
    public override string ToString()
        => $"{Formater(FromMinute)}–{Formater(ToMinute)}";

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return FromMinute;
        yield return ToMinute;
    }

    private static string Formater(int minute)
        => minute == MinutesParJour ? "24:00" : $"{minute / 60:D2}:{minute % 60:D2}";
}
