namespace Hba.Location.Domain.Positions;

/// <summary>
/// Distance a vol d'oiseau entre deux points.
/// </summary>
///
/// <remarks>
/// CE N'EST PAS UN ITINERAIRE, ET IL NE FAUT PAS S'EN SERVIR COMME TEL. A Cotonou,
/// deux points separes par la lagune sont proches a vol d'oiseau et loin par la
/// route. Cette mesure sert a ORDONNER des candidats, pas a annoncer un temps
/// d'arrivee : le temps de trajet reel se demande a Routing, qui sait par ou passent
/// les rues.
/// </remarks>
public static class Distances
{
    /// <summary>
    /// LE RAYON TERRESTRE DE REDIS, PAS CELUI DES MANUELS.
    ///
    /// GEORADIUS mesure sur une sphere de 6 372 797,560856 m ; prendre les
    /// 6 371 000 m habituels ici rendrait deux distances differentes pour le meme
    /// couple de points selon qu'on passe par la recherche de proximite — ou c'est
    /// Redis qui mesure — ou par la lecture d'un chauffeur nomme, ou c'est ce code.
    /// L'ecart est de trois pour mille : invisible sur le terrain, et largement
    /// suffisant pour faire douter de la mesure quand deux ecrans l'affichent cote a
    /// cote.
    /// </summary>
    private const double RayonTerrestreMetres = 6372797.560856;

    private const double EnRadians = Math.PI / 180d;

    /// <summary>Distance orthodromique entre deux points, en metres, arrondie.</summary>
    public static int Metres(GeoPoint depuis, GeoPoint vers)
    {
        ArgumentNullException.ThrowIfNull(depuis);
        ArgumentNullException.ThrowIfNull(vers);

        var phi1 = depuis.Latitude * EnRadians;
        var phi2 = vers.Latitude * EnRadians;
        var dPhi = (vers.Latitude - depuis.Latitude) * EnRadians;
        var dLambda = (vers.Longitude - depuis.Longitude) * EnRadians;

        var sinPhi = Math.Sin(dPhi / 2d);
        var sinLambda = Math.Sin(dLambda / 2d);

        var a = (sinPhi * sinPhi) + (Math.Cos(phi1) * Math.Cos(phi2) * sinLambda * sinLambda);

        var metres = 2d * RayonTerrestreMetres * Math.Asin(Math.Min(1d, Math.Sqrt(a)));

        return (int)Math.Round(metres, MidpointRounding.AwayFromZero);
    }
}
