namespace Hba.Pricing.Domain.Exceptions;

/// <summary>
/// Codes metier stables du service Pricing.
/// </summary>
///
/// <remarks>
/// CE FICHIER ARRIVE TARD, ET IL RASSEMBLE DES CODES QUI EXISTAIENT DEJA. Ils
/// etaient ecrits en litteral la ou ils sont leves — huit fichiers, dix-sept
/// chaines. Un code d'erreur est un CONTRAT : l'application cliente distingue
/// NO_TARIFF de ZONE_NOT_COVERED pour ne pas dire la meme chose au client. Une
/// faute de frappe dans un litteral ne casse rien a la compilation et tout a
/// l'affichage.
///
/// LES VALEURS NE CHANGENT PAS. Ce regroupement ne renomme rien : chaque
/// constante porte exactement la chaine qui circulait avant elle.
/// </remarks>
public static class PricingErrorCodes
{
    // ---- Grilles tarifaires -------------------------------------------------

    /// <summary>
    /// Aucune grille en vigueur pour ce couple (zone, vehicule) a cet instant.
    ///
    /// C'EST LE REFUS LE PLUS FREQUENT D'UN SYSTEME NEUF, et il n'est pas une
    /// panne : il dit qu'un prix n'a jamais ete decide pour ce cas. Jusqu'au
    /// 1er octobre 2026 il etait aussi INREPARABLE sans redemarrage, faute de
    /// route pour creer une grille.
    /// </summary>
    public const string NoTariff = "NO_TARIFF";

    /// <summary>
    /// Le devis a ete etabli pour une autre nature de prestation.
    /// </summary>
    ///
    /// <remarks>
    /// DISTINCT DE QUOTE_TRIP_MISMATCH, et la distinction compte pour l'appelant : un
    /// trajet qui a change demande de refaire la saisie, une nature qui ne correspond
    /// pas est un bug de l'application — elle a demande un devis de colis pour une
    /// course. Les confondre enverrait l'utilisateur corriger une adresse qui est
    /// juste.
    /// </remarks>
    public const string QuoteServiceMismatch = "QUOTE_SERVICE_MISMATCH";

    /// <summary>
    /// Deux grilles couvriraient les memes heures sur la meme cle.
    /// </summary>
    ///
    /// <remarks>
    /// CE REFUS EST DESTINE A UN HUMAIN, pas a une application : il tombe sur un geste
    /// d'exploitation, et son message nomme la plage qui fait obstacle. Le laisser
    /// passer ferait dependre le prix d'une course de nuit de l'ordre de lecture en
    /// base.
    /// </remarks>
    public const string OverlappingTimeBand = "OVERLAPPING_TIME_BAND";

    public const string InvalidTariff = "INVALID_TARIFF";

    public const string MissingTariffVersion = "MISSING_TARIFF_VERSION";

    public const string MissingZoneCode = "MISSING_ZONE_CODE";

    public const string MissingZoneName = "MISSING_ZONE_NAME";

    public const string ZoneNotCovered = "ZONE_NOT_COVERED";

    /// <summary>
    /// Le contour proposé n'est pas un polygone valide.
    /// </summary>
    ///
    /// <remarks>
    /// CE REFUS VIENT DE LA BASE, et c'est la seule qui sache le prononcer : un
    /// contour qui se recoupe — un « 8 », ou deux sommets confondus — n'est pas
    /// detectable en arithmetique. Il est destine a un humain devant une carte, et
    /// son message dit quoi reprendre.
    /// </remarks>
    public const string InvalidZoneRing = "INVALID_ZONE_RING";

    /// <summary>
    /// Le schema geographique n'est pas en place : colonne « area » ou extension
    /// postgis absente.
    /// </summary>
    ///
    /// <remarks>
    /// CE N'EST PAS UNE ERREUR METIER, ET IL EST QUAND MEME LA. Sans elle, une base
    /// non migree repondait par un 500 muet : la console affichait « Internal Server
    /// Error », et rien ne disait qu'il manquait une migration. Database:AutoMigrate
    /// vaut faux dans ce deploiement — le schema s'applique explicitement, donc
    /// l'oubli est un etat NORMAL d'un environnement neuf, pas un incident rare.
    ///
    /// SON MESSAGE PORTE LE REMEDE, et c'est tout son interet.
    /// </remarks>
    public const string ZoneSchemaMissing = "ZONE_SCHEMA_MISSING";

    /// <summary>La grille demandee n'existe pas.</summary>
    public const string TariffNotFound = "TARIFF_NOT_FOUND";

    /// <summary>Elle est deja close : la clore une seconde fois ne veut rien dire.</summary>
    public const string TariffAlreadyClosed = "TARIFF_ALREADY_CLOSED";

    // PAS DE CODE POUR LE REFUS D'ECRITURE, ET C'EST VOLONTAIRE. Un refus
    // d'autorisation passe par ForbiddenException, qui porte « FORBIDDEN » pour
    // tout le depot. Declarer ici un TARIFF_ADMINISTRATION_FORBIDDEN ferait
    // croire qu'un tel code circule ; aucune reponse ne le porterait jamais.

    // ---- Devis --------------------------------------------------------------

    public const string QuoteExpired = "QUOTE_EXPIRED";

    public const string QuoteAlreadyConsumed = "QUOTE_ALREADY_CONSUMED";

    public const string QuoteTripMismatch = "QUOTE_TRIP_MISMATCH";

    public const string InvalidQuoteLifetime = "INVALID_QUOTE_LIFETIME";

    // ---- Saisies ------------------------------------------------------------

    public const string InvalidAmount = "INVALID_AMOUNT";

    public const string InvalidCoordinates = "INVALID_COORDINATES";

    public const string InvalidId = "INVALID_ID";

    public const string InvalidRate = "INVALID_RATE";

    public const string InvalidRoute = "INVALID_ROUTE";

    public const string MissingPoints = "MISSING_POINTS";

    public const string MissingDeliveryId = "MISSING_DELIVERY_ID";
}
