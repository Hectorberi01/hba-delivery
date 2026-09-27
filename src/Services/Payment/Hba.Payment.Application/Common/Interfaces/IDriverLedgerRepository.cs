using Hba.Payment.Domain.Earnings;

namespace Hba.Payment.Application.Common.Interfaces;

public interface IDriverLedgerRepository
{
    /// <summary>
    /// Le credit deja ecrit pour cette course, s'il existe.
    ///
    /// DEUXIEME GARDE-FOU, APRES L'INBOX. L'Inbox ecarte deja le meme
    /// evenement livre deux fois ; celle-ci ecarte le meme FAIT arrive par un
    /// autre chemin — un rattrapage depuis l'origine du sujet apres une purge
    /// de l'Inbox, ou un evenement republie sous un nouvel identifiant. Payer
    /// deux fois la meme course est le genre d'erreur qu'on ne decouvre qu'au
    /// moment ou l'argent manque.
    /// </summary>
    Task<DriverLedgerEntry?> FindDeliveryEarningAsync(Guid deliveryId, CancellationToken cancellationToken);

    /// <summary>
    /// Ce qui est RETIRABLE aujourd'hui : les remunerations anterieures a
    /// <paramref name="creditsBefore"/>, moins tout ce qui a deja ete verse.
    ///
    /// CE N'EST PAS LE « RESTE DU » DU RELEVE, et la difference est le delai
    /// de carence. Le releve montre ce que HBA doit ; celui-ci montre ce
    /// qu'on peut demander maintenant. Sans carence configuree les deux
    /// coincident, et c'est le cas par defaut.
    ///
    /// LES VERSEMENTS NE SUBISSENT AUCUNE CARENCE : un versement deja fait
    /// est fait, quelle que soit sa date.
    /// </summary>
    Task<long> DueForPayoutAsync(
        string driverId,
        DateTimeOffset creditsBefore,
        CancellationToken cancellationToken);

    void Add(DriverLedgerEntry entry);
}
