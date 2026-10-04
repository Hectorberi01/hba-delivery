using Hba.Pricing.Domain.Quotes;
using Hba.Pricing.Domain.Tariffs;
using Hba.Pricing.Domain.ValueObjects;
using Hba.Pricing.Domain.Zones;

namespace Hba.Pricing.Application.Common.Interfaces;

/// <summary>
/// Zone tarifaire d'un point.
///
/// LE POLYGONE RESTE EN BASE, comme le dit l'agregat Zone : c'est la requete
/// qui fait la geometrie (ST_Contains), pas le domaine.
/// </summary>
public interface IZoneLocator
{
    /// <summary>Code de zone, ou null si le point n'est couvert par aucune.</summary>
    Task<string?> ResolveAsync(GeoPoint point, CancellationToken cancellationToken);
}

/// <summary>
/// Les zones, sans leur contour.
/// </summary>
///
/// <remarks>
/// UNE ZONE SE CHERCHE PAR SON CODE, PAS PAR SON IDENTIFIANT, et c'est le code qui
/// la lie a ses grilles : une grille nomme sa zone par « cotonou-centre », jamais
/// par un GUID. L'identifiant technique ne sert qu'a retrouver le contour.
/// </remarks>
public interface IZoneRepository
{
    Task<Zone?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Les zones connues, par priorite decroissante puis par code.
    ///
    /// LES DESACTIVEES SE CONSERVENT : des grilles et des devis les nomment, et les
    /// effacer rendrait illisibles des prix deja factures.
    /// </summary>
    Task<IReadOnlyList<Zone>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    void Add(Zone zone);
}

/// <summary>
/// Le contour d'une zone, qui vit en base et nulle part ailleurs.
/// </summary>
///
/// <remarks>
/// POURQUOI CE PORT EXISTE A COTE DU DEPOT. Le polygone n'est PAS une propriete de
/// l'agregat — le domaine ne fait pas de geometrie, et les tests d'architecture lui
/// interdisent la dependance qui le permettrait. Il faut donc un second chemin pour
/// l'ecrire et le relire, et c'est celui-ci. Sa mise en oeuvre est du SQL : ST_Covers
/// pour chercher, ST_IsValid pour refuser, ST_AsGeoJSON pour dessiner.
///
/// LA VALIDITE EST UNE QUESTION POUR LA BASE. « Trois sommets » et « coordonnees dans
/// les bornes » se verifient en arithmetique ; « ce contour ne se recoupe pas » non.
/// </remarks>
public interface IZoneRingStore
{
    /// <summary>Ce contour forme-t-il un polygone que la base accepte ?</summary>
    Task<bool> IsValidAsync(IReadOnlyList<GeoPoint> ring, CancellationToken cancellationToken);

    /// <summary>
    /// Pose le contour de cette zone, en remplacant celui qui s'y trouvait.
    ///
    /// APPELE APRES L'ENREGISTREMENT DE LA LIGNE, jamais avant : une zone sans
    /// contour ne contient aucun point et se voit, un contour sans zone est orphelin
    /// et ne se voit pas.
    /// </summary>
    Task SaveAsync(Guid zoneId, IReadOnlyList<GeoPoint> ring, CancellationToken cancellationToken);

    /// <summary>
    /// Les contours de ces zones, en un seul appel. Une zone sans contour est
    /// ABSENTE du resultat — elle n'a pas encore ete dessinee.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<GeoPoint>>> ReadAsync(
        IReadOnlyList<Guid> zoneIds,
        CancellationToken cancellationToken);
}

public interface ITariffRepository
{
    /// <summary>
    /// Grille en vigueur pour une zone, une nature, un vehicule, un instant ET une
    /// heure locale.
    /// </summary>
    ///
    /// <remarks>
    /// DEUX NOTIONS DE TEMPS, ET IL FAUT LES DEUX. <paramref name="at"/> dit quelle
    /// VERSION etait en vigueur — une grille n'est jamais modifiee, on en cree une
    /// nouvelle version et on clot la precedente, donc une grille close en mars ne
    /// sert plus en avril ;
    /// <paramref name="heureLocale"/> dit quelle PLAGE s'applique — la grille de nuit
    /// est en vigueur toute l'annee et ne s'applique pas a midi. Les confondre ferait
    /// facturer la nuit en plein jour.
    ///
    /// L'HEURE EST PASSEE, PAS DEDUITE DE <paramref name="at"/>. Le fuseau metier est
    /// une decision de configuration (ADR 0019) : la deduire ici ferait de ce depot un
    /// endroit qui connait Cotonou, alors que c'est ITimeCalendar qui le sait.
    /// </remarks>
    Task<Tariff?> FindInForceAsync(
        string zoneCode,
        ServiceType serviceType,
        VehicleType vehicleType,
        DateTimeOffset at,
        TimeOnly heureLocale,
        CancellationToken cancellationToken);

    /// <summary>
    /// Les grilles en vigueur sur la meme cle, pour verifier qu'une nouvelle plage
    /// n'en recouvre aucune.
    /// </summary>
    ///
    /// <remarks>
    /// CE CONTROLE EST CE QUI EMPECHE DEUX PRIX POUR UNE MEME MINUTE. Sans lui, une
    /// grille 0–1440 et une grille 22h–6h cohabiteraient sur la meme zone et le meme
    /// vehicule : le prix d'une course a 23 h dependrait de l'ordre de lecture en base,
    /// donc changerait d'un appel a l'autre sans raison.
    ///
    /// IL EST APPLICATIF ET NON EN BASE, faute de pouvoir exprimer « ces deux plages ne
    /// se recouvrent pas » en SQL sans une extension que ce depot n'installe pas. Deux
    /// creations concurrentes sur la meme cle peuvent donc passer — c'est un geste
    /// d'exploitation, rare et attribue, pas un chemin chaud.
    /// </remarks>
    Task<IReadOnlyList<Tariff>> FindInForceOnKeyAsync(
        string zoneCode,
        ServiceType serviceType,
        VehicleType vehicleType,
        DateTimeOffset at,
        CancellationToken cancellationToken);

    void Add(Tariff tariff);

    Task<bool> AnyAsync(CancellationToken cancellationToken);

    /// <summary>Une grille par son identifiant, pour la clore.</summary>
    Task<Tariff?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Les grilles connues, filtrees.
    ///
    /// L'HISTORIQUE NE SE SUPPRIME PAS, et c'est pour cela que
    /// <paramref name="includeClosed"/> existe. Un devis emis hier se reclame de
    /// la grille d'hier (ADR 0004) : effacer une grille close rendrait
    /// illisibles les prix deja factures. On choisit donc de la MONTRER ou non,
    /// jamais de la perdre.
    /// </summary>
    Task<IReadOnlyList<Tariff>> ListAsync(
        string? zoneCode,
        ServiceType? serviceType,
        VehicleType? vehicleType,
        bool includeClosed,
        DateTimeOffset at,
        CancellationToken cancellationToken);
}

public interface IQuoteRepository
{
    Task<Quote?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    void Add(Quote quote);
}
