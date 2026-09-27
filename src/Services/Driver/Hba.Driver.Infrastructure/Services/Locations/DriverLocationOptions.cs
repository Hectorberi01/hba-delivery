namespace Hba.Driver.Infrastructure.Services.Locations;

public sealed class DriverLocationOptions
{
    public const string SectionName = "DriverLocations";

    /// <summary>
    /// Au-dela de ce delai sans nouveau point, la position n'est plus
    /// consideree comme fiable et le livreur sort des recherches.
    ///
    /// DEUX MINUTES EST UN COMPROMIS, PAS UNE VERITE. Trop court, un livreur
    /// dans une zone mal couverte disparait alors qu'il roule ; trop long, une
    /// vague entiere part vers des telephones eteints et le client attend
    /// trente secondes pour rien. A ajuster quand le pilote aura montre la
    /// vraie qualite du reseau a Cotonou.
    ///
    /// CE NOMBRE EST LIE A UN AUTRE, DANS L'APPLICATION LIVREUR : son
    /// battement de position est regle a vingt secondes. Le rapport entre les
    /// deux est la marge d'echecs toleree — six battements perdus d'affilee
    /// ici. Raccourcir ce delai sans raccourcir le battement fait disparaitre
    /// des livreurs parfaitement en ligne, sans erreur nulle part.
    /// </summary>
    public int FreshnessSeconds { get; set; } = 120;
}
