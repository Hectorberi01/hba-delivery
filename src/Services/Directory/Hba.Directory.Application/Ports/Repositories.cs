using Hba.Directory.Domain.Customers;
using Hba.Directory.Domain.Merchants;

namespace Hba.Directory.Application.Ports;

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Identifiant de la fiche portant ce telephone, s'il en existe une.
    /// </summary>
    ///
    /// <remarks>
    /// UNE REGLE DE DOMAINE ETAIT TENUE PAR UN INDEX SEUL, ET PAR PERSONNE
    /// D'AUTRE. « IX_customers_phone » est unique : deux fiches ne peuvent pas
    /// porter le meme numero. Aucun code ne le savait, donc la creation
    /// partait, Postgres refusait, et le service rendait « Erreur interne » —
    /// ou, cote consommateur Kafka, echouait sans que rien ne le dise.
    ///
    /// ELLE NE REND QUE L'IDENTIFIANT, PAS LA FICHE. Rendre le profil complet
    /// ferait de cette methode une recherche par telephone accessible hors du
    /// back-office, ce que SearchAsync reserve explicitement a celui-ci.
    /// </remarks>
    Task<Guid?> FindIdByPhoneAsync(string phone, CancellationToken cancellationToken);

    /// <summary>
    /// Recherche dans l'annuaire, sur le nom ou le telephone.
    ///
    /// LA RECHERCHE PAR TELEPHONE EST LA RAISON D'ETRE DE CETTE METHODE, et
    /// aussi ce qui la rend sensible : c'est elle qui transforme un numero en
    /// nom et en adresses. Elle n'existe que pour le back-office, et le
    /// handler le verifie — pas la passerelle.
    /// </summary>
    Task<IReadOnlyList<Customer>> SearchAsync(
        string? query,
        int pageSize,
        int offset,
        CancellationToken cancellationToken);

    Task<int> CountAsync(string? query, CancellationToken cancellationToken);

    void Add(Customer customer);

    /// <summary>
    /// Retire la fiche et tout ce qu'elle possède.
    /// </summary>
    ///
    /// <remarks>
    /// LE SEUL APPELANT EST L'EFFACEMENT D'UN COMPTE, et il vient d'Identity
    /// par événement. Aucune route ne l'expose : une fiche client ne se
    /// supprime pas depuis le back-office, elle disparaît avec le compte de
    /// son titulaire.
    ///
    /// LES ADRESSES FAVORITES PARTENT AVEC ELLE, sans rien de plus à écrire :
    /// elles sont un type possédé (OwnsMany), donc EF les supprime en cascade.
    /// C'est important — elles contiennent les noms et les téléphones de TIERS
    /// que le client avait enregistrés.
    /// </remarks>
    void Remove(Customer customer);
}

public interface IMerchantRepository
{
    Task<Merchant?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Recherche le commerçant propriétaire d'un point de collecte.</summary>
    Task<Merchant?> GetByPickupPointIdAsync(Guid pickupPointId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Merchant>> SearchAsync(
        string? query,
        bool onlyActive,
        int pageSize,
        int offset,
        CancellationToken cancellationToken);

    Task<int> CountAsync(string? query, bool onlyActive, CancellationToken cancellationToken);

    void Add(Merchant merchant);
}
