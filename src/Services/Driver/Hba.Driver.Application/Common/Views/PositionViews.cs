using Hba.Driver.Domain.Drivers;

namespace Hba.Driver.Application.Common.Views;

/// <summary>
/// Un livreur sur la carte : ou il est, quand on l'a vu, et ce qu'il fait.
///
/// L'HORODATAGE N'EST PAS DECORATIF. Deux points affiches cote a cote peuvent
/// dater de dix secondes et de deux minutes ; sans la date, ops les croit
/// aussi surs l'un que l'autre.
/// </summary>
public sealed record DriverPositionView(
    string DriverId,
    string DisplayName,
    double Latitude,
    double Longitude,
    DateTimeOffset SeenAt,
    OperationalStatus OperationalStatus,
    VehicleType VehicleType);

/// <summary>
/// La liste, et la fenetre qui la definit.
///
/// « 14 livreurs » ne veut rien dire sans « vus dans les 120 dernieres
/// secondes » : c'est la fenetre qui dit ce que le chiffre compte.
/// </summary>
public sealed record DriverPositionPageView(
    IReadOnlyList<DriverPositionView> Positions,
    int FreshnessSeconds);
