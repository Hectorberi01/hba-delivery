namespace Hba.BuildingBlocks.Application.Time;

public sealed class TimeOptions
{
    public const string SectionName = "Time";

    /// <summary>
    /// Fuseau métier. Le Bénin est à UTC+1 toute l'année, sans heure d'été ;
    /// l'UEMOA ne l'est pas entièrement — le Mali et le Sénégal sont à UTC+0.
    /// D'où un réglage, et non une constante.
    /// </summary>
    public string ZoneId { get; set; } = "Africa/Porto-Novo";

    /// <summary>
    /// Fenêtre servie quand l'appelant n'en demande aucune.
    /// </summary>
    public int DefaultWindowDays { get; set; } = 30;

    /// <summary>
    /// Plafond d'une fenêtre.
    ///
    /// UN GARDE-FOU, PAS UNE REGLE METIER : sans lui, un « du 1er janvier
    /// 1970 à aujourd'hui » envoyé par erreur balaie toute la table, et
    /// personne ne s'en aperçoit avant que la base ne ralentisse pour tout
    /// le monde.
    /// </summary>
    public int MaxWindowDays { get; set; } = 366;
}
