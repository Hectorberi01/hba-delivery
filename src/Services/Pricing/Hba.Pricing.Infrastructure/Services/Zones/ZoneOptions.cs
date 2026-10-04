namespace Hba.Pricing.Infrastructure.Services.Zones;

/// <summary>
/// Reglages de zone.
/// </summary>
///
/// <remarks>
/// CE FICHIER S'APPELAIT ConfiguredZoneLocator.cs, et il portait la resolution de
/// zone PROVISOIRE : elle ne lisait aucun polygone — il n'y en avait aucun de
/// dessine — et rendait la zone de repli configuree pour TOUT point du globe. Le
/// zonage tarifaire etait donc fictif, et une seule grille s'appliquait partout.
/// PostgisZoneStore l'a remplacee le 1er octobre 2026 (point 40) ; il ne reste ici
/// que le reglage, qui lui survit.
/// </remarks>
public sealed class ZoneOptions
{
    public const string SectionName = "Zones";

    /// <summary>
    /// Zone appliquee a un point qu'AUCUN polygone ne couvre.
    /// </summary>
    ///
    /// <remarks>
    /// VIDE SIGNIFIE « AUCUNE ZONE », donc aucun prix : c'est le comportement
    /// correct en production, ou livrer hors zone n'a pas de sens, et le refus
    /// ZONE_NOT_COVERED est alors la bonne reponse. Une valeur non vide fait entrer
    /// le monde entier dans cette zone, ce qui n'est acceptable qu'en
    /// developpement.
    ///
    /// SON ROLE A CHANGE. Elle etait la SEULE reponse ; elle n'est plus qu'un
    /// dernier recours, consulte apres la recherche geographique. Le jour ou les
    /// zones de production seront dessinees, elle devra etre videe — sans quoi une
    /// course hors de toute zone sera facturee au tarif d'une ville ou elle n'est
    /// pas.
    /// </remarks>
    public string? FallbackZoneCode { get; set; }
}
