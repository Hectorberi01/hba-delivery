using Hba.Payment.Domain.Earnings;
using Hba.Payment.Domain.Payments;

namespace Hba.Payment.Application.Common.Interfaces;

public interface IDriverLedgerRepository
{
    /// <summary>
    /// Le credit deja ecrit pour cette mission DANS CE GENRE, s'il existe.
    ///
    /// DEUXIEME GARDE-FOU, APRES L'INBOX. L'Inbox ecarte deja le meme
    /// evenement livre deux fois ; celle-ci ecarte le meme FAIT arrive par un
    /// autre chemin — un rattrapage depuis l'origine du sujet apres une purge
    /// de l'Inbox, ou un evenement republie sous un nouvel identifiant. Payer
    /// deux fois la meme course est le genre d'erreur qu'on ne decouvre qu'au
    /// moment ou l'argent manque.
    /// </summary>
    ///
    /// LE GENRE EST DANS LA CLE DEPUIS LE 2 OCTOBRE 2026, et sans lui la methode
    /// serait fausse : une remuneration et un dedommagement portent le MEME
    /// identifiant de mission, et le second serait pris pour un rejeu du premier.
    /// Les deux ne peuvent pas coexister en pratique — une course livree n'est pas
    /// annulee — mais s'en remettre a cette impossibilite reviendrait a faire
    /// dependre le grand livre d'une invariante ecrite ailleurs.
    Task<DriverLedgerEntry?> FindMissionEntryAsync(
        LedgerEntryKind kind,
        ServiceType serviceType,
        Guid referenceId,
        CancellationToken cancellationToken);

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
