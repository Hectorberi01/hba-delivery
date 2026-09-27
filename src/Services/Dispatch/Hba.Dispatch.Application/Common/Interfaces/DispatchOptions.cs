namespace Hba.Dispatch.Application.Common.Interfaces;

/// <summary>
/// Politique de vagues, tranchee en septembre 2026 (point 5 des points a
/// trancher) : TROIS VAGUES A 2, 4 ET 6 KILOMETRES, trente secondes chacune,
/// soit quatre-vingt-dix secondes avant NO_DRIVER_FOUND.
///
/// ELLES SONT EN CONFIGURATION, PAS EN DUR, et ce n'est pas de la prudence
/// gratuite : ce sont des parametres d'exploitation. Le pilote dira si deux
/// kilometres suffisent aux heures creuses, et l'ajustement ne doit pas
/// demander une compilation.
/// </summary>
public sealed class DispatchOptions
{
    public const string SectionName = "Dispatch";

    /// <summary>
    /// Rayon de chaque vague, dans l'ordre. La taille de ce tableau EST le
    /// nombre de vagues : il n'y a pas de second reglage a tenir en phase.
    /// </summary>
    public int[] WaveRadiiMeters { get; set; } = [2000, 4000, 6000];

    /// <summary>Delai de reponse. Le referentiel fixe 30 s par defaut.</summary>
    public int OfferSeconds { get; set; } = 30;

    /// <summary>
    /// Nombre de livreurs sollicites par vague.
    ///
    /// PLUS N'EST PAS MIEUX. Chaque offre immobilise un livreur pendant trente
    /// secondes : en solliciter vingt pour une course en gele dix-neuf pour
    /// rien, et les courses voisines n'ont plus personne.
    /// </summary>
    public int DriversPerWave { get; set; } = 5;

    /// <summary>Duree du verrou d'acceptation. Courte : il ne couvre qu'une transaction.</summary>
    public int AcceptanceLockSeconds { get; set; } = 10;

    /// <summary>Periode de balayage du planificateur.</summary>
    public int SweepSeconds { get; set; } = 5;

    public int WaveCount => WaveRadiiMeters.Length;

    public TimeSpan OfferLifetime => TimeSpan.FromSeconds(OfferSeconds);

    /// <summary>Rayon de la vague numerotee a partir de 1, ou null au-dela de la derniere.</summary>
    public int? RadiusForWave(int waveNumber)
        => waveNumber >= 1 && waveNumber <= WaveRadiiMeters.Length ? WaveRadiiMeters[waveNumber - 1] : null;
}
