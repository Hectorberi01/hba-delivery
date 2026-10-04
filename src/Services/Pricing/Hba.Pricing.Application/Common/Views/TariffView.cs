using Hba.Pricing.Domain.Tariffs;

namespace Hba.Pricing.Application.Common.Views;

/// <summary>
/// Grille tarifaire telle qu'elle sort du service. Les montants sont des
/// entiers de francs CFA : le XOF n'a pas de subdivision, et l'ADR 0006
/// interdit les decimaux.
/// </summary>
///
/// <remarks>
/// LES DEUX TAUX SONT EN POINTS DE BASE, PAS EN POURCENTAGE, et c'est le meme
/// choix que dans l'agregat : 10000 vaut 1,00. Un pourcentage en decimal
/// reintroduirait exactement ce que l'ADR 0006 ecarte, et une part de livreur
/// arrondie au centieme se voit sur une facture de fin de mois.
///
/// LA PLAGE HORAIRE SORT EN MINUTES, PAS EN TEXTE. « 22:00–06:00 » se lit mieux,
/// mais un ecran d'administration qui doit RENVOYER la grille modifiee devrait
/// alors reanalyser cette chaine, et un fuseau glisse a chaque aller-retour. Le
/// libelle est reconstruit a l'affichage ; les minutes sont la donnee.
/// </remarks>
public sealed record TariffView(
    Guid Id,
    string TariffVersion,
    string ZoneCode,
    ServiceType ServiceType,
    VehicleType VehicleType,
    long BaseFare,
    long PerKilometer,
    long PerMinute,
    long MinimumFare,
    long PerKilogram,
    int IncludedWeightGrams,
    int BandFromMinute,
    int BandToMinute,
    int SurgeBasisPoints,
    int DriverShareBasisPoints,
    int CancellationFeeBasisPoints,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidUntil)
{
    public static TariffView From(Tariff tariff)
    {
        ArgumentNullException.ThrowIfNull(tariff);

        return new TariffView(
            tariff.Id,
            tariff.TariffVersion,
            tariff.ZoneCode,
            tariff.ServiceType,
            tariff.VehicleType,
            tariff.BaseFare.Amount,
            tariff.PerKilometer.Amount,
            tariff.PerMinute.Amount,
            tariff.MinimumFare.Amount,
            tariff.PerKilogram.Amount,
            tariff.IncludedWeightGrams,
            tariff.Band.FromMinute,
            tariff.Band.ToMinute,
            tariff.SurgeBasisPoints,
            tariff.DriverShareBasisPoints,
            tariff.CancellationFeeBasisPoints,
            tariff.ValidFrom,
            tariff.ValidUntil);
    }
}
