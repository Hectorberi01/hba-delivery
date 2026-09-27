namespace Hba.BuildingBlocks.Application.Time;

/// <summary>
/// Le calendrier métier : ce qui traduit des instants en journées, et des
/// journées en instants.
///
/// UN SEUL ENDROIT POUR CETTE TRADUCTION, DANS TOUT LE DEPOT. Dès que deux
/// services décident chacun de leur côté ce qu'est « aujourd'hui », ils
/// affichent deux chiffres différents pour la même journée, et le désaccord
/// n'apparaît qu'en comparant deux écrans.
/// </summary>
public interface ITimeCalendar
{
    /// <summary>Fuseau métier configuré.</summary>
    TimeZoneInfo Zone { get; }

    /// <summary>
    /// Identifiant du fuseau, tel qu'il peut être écrit dans un
    /// <c>AT TIME ZONE</c> PostgreSQL. Validé au démarrage : il finit dans du
    /// SQL, il ne peut donc pas contenir n'importe quoi.
    /// </summary>
    string ZoneId { get; }

    /// <summary>Clé de regroupement locale : « 2026-09-26 » ou « 2026-09 ».</summary>
    string Key(DateTimeOffset instant, TimeGranularity granularity);

    /// <summary>
    /// Vérifie qu'une fenêtre reçue de l'extérieur est acceptable, et la rend
    /// telle quelle. Lève si elle dépasse le plafond.
    /// </summary>
    TimeWindow Validate(TimeWindow window);

    /// <summary>
    /// Les <paramref name="days"/> derniers jours LOCAUX, aujourd'hui compris
    /// en entier : du début du jour local J-(n-1) au début de demain.
    /// </summary>
    TimeWindow LastDays(int days);

    /// <summary>Fenêtre par défaut, quand l'appelant n'en demande aucune.</summary>
    TimeWindow Default();

    /// <summary>Du début du mois local en cours au début du mois suivant.</summary>
    TimeWindow CurrentMonth();

    /// <summary>
    /// Toutes les clés que la fenêtre traverse, y compris celles sans donnée.
    ///
    /// C'EST CE QUI EMPECHE LE GRAPHIQUE DE MENTIR. Un GROUP BY ne rend aucune
    /// ligne pour un jour sans course : sans cette liste, le graphique
    /// resserre les barres et une journée creuse disparaît au lieu de
    /// s'afficher à zéro.
    /// </summary>
    IReadOnlyList<string> Keys(TimeWindow window, TimeGranularity granularity);
}
