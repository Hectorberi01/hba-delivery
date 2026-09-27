using Hba.Payment.Domain.Earnings;

namespace Hba.Payment.Application.Common.Interfaces;

/// <summary>Une ligne du relevé, telle qu'elle s'affiche.</summary>
public sealed record LedgerEntryView(
    Guid Id,
    LedgerEntryKind Kind,
    LedgerDirection Direction,
    long AmountXof,
    Guid? DeliveryId,
    string DeliveryReference,
    Guid? PayoutId,
    DateTimeOffset OccurredAt);

/// <summary>
/// Le compte d'un livreur.
///
/// LES TROIS TOTAUX NE DEPENDENT PAS DES LIGNES RENDUES, et c'est tout
/// l'interet de ce relevé. L'ancien ecran « Gains » additionnait les
/// remunerations des vingt-cinq dernieres courses que l'application avait
/// sous la main : un chiffre qui bougeait avec la taille d'une page, et qui
/// devait donc avouer qu'il n'etait pas un solde. Ici, « du » est une dette.
///
/// LE RESTE A VERSER EST CALCULE PAR LE SERVICE, pas par le client. Deux
/// applications qui referaient la soustraction chacune de leur cote
/// finiraient par ne pas tomber d'accord, et c'est le genre de desaccord qui
/// se decouvre devant un livreur.
/// </summary>
public sealed record DriverStatementView(
    string DriverId,
    long EarnedXof,
    long PaidOutXof,
    long DueXof,
    IReadOnlyList<LedgerEntryView> Entries,
    long TotalEntries);

public interface IDriverStatementReader
{
    Task<DriverStatementView> ReadAsync(string driverId, int limit, CancellationToken cancellationToken);
}
