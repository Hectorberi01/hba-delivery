using Hba.BuildingBlocks.Domain;
using Hba.Location.Domain.Exceptions;

namespace Hba.Location.Domain.Positions;

/// <summary>
/// Ce que le telephone dit du MOUVEMENT, en plus du point : cap, vitesse,
/// precision.
/// </summary>
///
/// <remarks>
/// LES TROIS SONT FACULTATIFS, ET LE NUL N'EST PAS ZERO. Un telephone a l'arret ne
/// rend aucun cap ; zero degre, lui, veut dire « plein nord ». Les confondre ferait
/// pointer toutes les motos immobiles vers le nord sur la carte de l'exploitation,
/// et personne ne saurait si c'est la realite ou un defaut d'affichage.
///
/// ILS SONT VALIDES MAIS JAMAIS UTILISES POUR FILTRER. Ce service n'ecarte aucun
/// point : une position a cinquante metres pres reste la meilleure information
/// disponible, et la jeter laisserait un chauffeur invisible sous un toit de tole.
/// La precision sert a LIRE une carte — deux points a trois cents metres l'un de
/// l'autre peuvent etre le meme vehicule — et le jour ou un seuil sera decide, il
/// sera deja la.
///
/// POURQUOI LES VALIDER ALORS. Parce qu'une valeur impossible — un cap de 540
/// degres, une vitesse negative — vient d'un bogue d'application ou d'un appelant
/// qui s'est trompe d'unite, et qu'un tel point, accepte en silence, dessinerait une
/// fleche absurde que personne ne saurait expliquer. Le refus nomme le champ.
/// </remarks>
public sealed class Motion : ValueObject
{
    /// <summary>
    /// Vitesse au-dela de laquelle on refuse, en metres par seconde : 400 km/h.
    ///
    /// CE N'EST PAS UNE LIMITATION DE VITESSE, c'est un filet contre les unites.
    /// Un telephone qui annoncerait 120 en croyant parler en km/h se verrait accepte
    /// — 120 m/s valent 432 km/h, donc au-dessus du seuil — et c'est exactement le
    /// genre d'erreur que ce plafond attrape.
    /// </summary>
    private const double VitesseMaximale = 111.2;

    private Motion(double? headingDegrees, double? speedMps, double? accuracyMeters)
    {
        HeadingDegrees = headingDegrees;
        SpeedMps = speedMps;
        AccuracyMeters = accuracyMeters;
    }

    /// <summary>Cap suivi, de 0 inclus a 360 exclu. Nul quand le telephone se tait.</summary>
    public double? HeadingDegrees { get; }

    /// <summary>Vitesse en metres par seconde. Zero veut dire « a l'arret ».</summary>
    public double? SpeedMps { get; }

    /// <summary>Rayon d'incertitude annonce par le GPS, en metres.</summary>
    public double? AccuracyMeters { get; }

    /// <summary>Ce que rend un point dont le telephone n'a dit que les coordonnees.</summary>
    public static Motion Inconnu { get; } = new(null, null, null);

    public bool EstInconnu => HeadingDegrees is null && SpeedMps is null && AccuracyMeters is null;

    public static Motion Create(double? headingDegrees, double? speedMps, double? accuracyMeters)
    {
        if (headingDegrees is { } cap && (double.IsNaN(cap) || cap is < 0 or >= 360))
        {
            throw new DomainException(
                LocationErrorCodes.InvalidHeading,
                "Un cap tient entre 0 inclus et 360 exclu.");
        }

        if (speedMps is { } vitesse && (double.IsNaN(vitesse) || vitesse < 0 || vitesse > VitesseMaximale))
        {
            throw new DomainException(
                LocationErrorCodes.InvalidSpeed,
                "Une vitesse en metres par seconde tient entre 0 et 111,2 — soit 400 km/h. "
                + "Au-dela, c'est une erreur d'unite.");
        }

        if (accuracyMeters is { } precision && (double.IsNaN(precision) || precision < 0))
        {
            throw new DomainException(
                LocationErrorCodes.InvalidAccuracy,
                "Une precision GPS est un rayon en metres, donc jamais negative.");
        }

        return new Motion(headingDegrees, speedMps, accuracyMeters);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return HeadingDegrees;
        yield return SpeedMps;
        yield return AccuracyMeters;
    }
}
