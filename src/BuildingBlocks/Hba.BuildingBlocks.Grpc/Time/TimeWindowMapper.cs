using Hba.BuildingBlocks.Application.Time;
using ProtoGranularity = Hba.Contracts.Common.V1.TimeGranularity;
using ProtoWindow = Hba.Contracts.Common.V1.TimeWindow;

namespace Hba.BuildingBlocks.Grpc.Time;

/// <summary>
/// Traduit la fenêtre du contrat en fenêtre métier, et rien d'autre.
///
/// C'EST ICI QUE SE DECIDE CE QUE VAUT « RIEN ». Un appelant qui n'envoie pas
/// de fenêtre — le tableau de bord au premier chargement — doit recevoir une
/// réponse, pas une erreur. Laisser chaque service choisir son défaut ferait
/// afficher trente jours sur un écran et sept sur l'autre.
/// </summary>
public static class TimeWindowMapper
{
    public static TimeWindow ToDomain(ProtoWindow? fenetre, ITimeCalendar calendrier)
    {
        ArgumentNullException.ThrowIfNull(calendrier);

        if (fenetre?.From is null || fenetre.To is null)
        {
            return calendrier.Default();
        }

        return calendrier.Validate(
            TimeWindow.Create(fenetre.From.ToDateTimeOffset(), fenetre.To.ToDateTimeOffset()));
    }

    public static TimeGranularity ToDomain(ProtoGranularity granularite) => granularite switch
    {
        ProtoGranularity.Month => TimeGranularity.Month,
        // UNSPECIFIED vaut « par jour », pas « zéro » : c'est la valeur par
        // défaut de protobuf, et le jour est le pas que demandent tous les
        // écrans sauf la tendance annuelle.
        _ => TimeGranularity.Day,
    };

    public static ProtoWindow ToProto(TimeWindow fenetre)
    {
        ArgumentNullException.ThrowIfNull(fenetre);

        return new ProtoWindow
        {
            From = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(fenetre.From),
            To = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(fenetre.To),
        };
    }
}
