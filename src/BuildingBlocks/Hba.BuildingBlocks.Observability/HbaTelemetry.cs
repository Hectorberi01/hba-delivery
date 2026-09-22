using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Hba.BuildingBlocks.Observability;

/// <summary>
/// Sources de traces et de métriques maison. Un seul nom par service, repris
/// dans la configuration du collecteur.
/// </summary>
public static class HbaTelemetry
{
    public const string ActivitySourceName = "Hba.Delivery";
    public const string MeterName = "Hba.Delivery";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static readonly Meter Meter = new(MeterName);
}
