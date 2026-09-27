namespace Hba.BuildingBlocks.Application.Abstractions;

/// <summary>Nature de la lecture consignée.</summary>
public enum PersonalDataReadKind
{
    /// <summary>Ouverture de la fiche d'un client : identité, et selon le rôle e-mail et adresses.</summary>
    CustomerFile = 1,

    /// <summary>Lecture du cumul facturé d'un client.</summary>
    CustomerBilling = 2,

    /// <summary>
    /// Ouverture du dossier KYC d'un livreur par le back-office : carte
    /// d'identité, permis, carte grise, photos, chacun derrière une URL
    /// signée.
    ///
    /// PLUS SENSIBLE QU'UNE FICHE CLIENT, et consigné pour cette raison. Un
    /// livreur qui consulte SON PROPRE dossier n'est pas consigné : ce n'est
    /// pas un accès à la donnée d'autrui, et journaliser chaque ouverture de
    /// l'application noierait les lectures qui comptent.
    /// </summary>
    DriverApplication = 3,
}

/// <summary>
/// Journal des LECTURES de données personnelles.
///
/// POURQUOI UN JOURNAL SEPARE DES EVENEMENTS DE DOMAINE. « Toute action
/// sensible est auditée » du référentiel est réalisé par les événements :
/// ils portent l'acteur, la date et le motif, et partent dans l'Outbox. Mais
/// une lecture n'est pas une action — elle ne change rien, donc elle ne
/// produit aucun événement — et jusqu'ici rien ne gardait trace de qui avait
/// regardé quoi. C'est ce trou que ce port ferme.
///
/// UNE TABLE PAR SERVICE, ET NON UNE TABLE CENTRALE. La règle du dépôt est
/// qu'aucun service ne touche la base d'un autre, même en lecture. La fiche
/// client se lit dans Directory, le cumul facturé dans Delivery : une table
/// unique aurait obligé l'un à écrire chez l'autre. La forme est donc définie
/// une seule fois ici, et posée dans le schéma de chaque service — comme
/// l'Outbox, l'Inbox et les clés d'idempotence.
///
/// PAS DE MOTIF. Décidé le 27 septembre 2026 : le journal répond à « qui a
/// regardé quoi, quand ». Exiger une justification avant chaque fiche
/// ajouterait une boîte de dialogue pendant un appel client, et produirait en
/// pratique « litige » à chaque ligne — un champ qui ne dit plus rien.
/// </summary>
public interface IPersonalDataReadLog
{
    /// <summary>
    /// Consigne une lecture qui a REUSSI. Un refus n'en produit aucune : il
    /// n'y a rien eu à voir, et une ligne dirait le contraire.
    /// </summary>
    Task RecordAsync(
        PersonalDataReadKind kind,
        string subjectId,
        CancellationToken cancellationToken);
}

/// <summary>Une trace d'accès, telle qu'on la relit.</summary>
public sealed record PersonalDataReadEntry(
    PersonalDataReadKind Kind,
    string ReaderId,
    string ReaderRoles,
    DateTimeOffset ReadAt);

/// <summary>
/// Relit le journal des lectures.
///
/// SEPARE DE L'ECRITURE, ET PAS PAR GOUT DE LA SYMETRIE. Ecrire est un effet
/// de bord de presque toutes les lectures sensibles ; relire est un geste
/// rare, reserve, et qui n'a aucune raison d'etre injecte partout ou l'on
/// ecrit.
/// </summary>
public interface IPersonalDataReadReader
{
    Task<IReadOnlyList<PersonalDataReadEntry>> ListForSubjectAsync(
        string subjectId,
        int limit,
        CancellationToken cancellationToken);
}
