using Hba.BuildingBlocks.Application.Abstractions;
using Hba.BuildingBlocks.Application.Messaging;
using Hba.BuildingBlocks.Domain;
using Hba.BuildingBlocks.Security;
using Hba.Notification.Application.Ports;
using Hba.Notification.Domain.Messages;

namespace Hba.Notification.Application.Journal;

/// <summary>
/// Une ligne du journal, telle que le back-office a le droit de la voir.
/// </summary>
///
/// <remarks>
/// LE DESTINATAIRE EST MASQUE DES CETTE COUCHE, et non à l'écran. Masquer dans
/// le navigateur revient à envoyer le numéro complet sur le réseau puis à
/// s'excuser de l'afficher : ce qui ne sort pas du service ne peut pas fuir.
/// </remarks>
public sealed record LigneDuJournalVue
{
    public required Guid Id { get; init; }
    public required NotificationChannel Canal { get; init; }
    public required string DestinataireMasque { get; init; }
    public required string ModeleId { get; init; }
    public required NotificationStatus Statut { get; init; }
    public string? Operateur { get; init; }
    public string? ReferenceOperateur { get; init; }
    public string? Erreur { get; init; }
    public int Tentatives { get; init; }
    public string? CorrelationId { get; init; }
    public DateTimeOffset CreeLe { get; init; }
    public DateTimeOffset? TermineLe { get; init; }
}

public sealed record PageDuJournalVue(IReadOnlyList<LigneDuJournalVue> Lignes, int Total);

/// <summary>
/// Le journal des envois : ce qui est parti, à qui, et ce que l'opérateur en a
/// dit.
/// </summary>
public sealed record ListerLeJournalQuery(
    int Limite,
    int Decalage,
    NotificationChannel? Canal,
    NotificationStatus? Statut,
    string? FinDuDestinataire,
    string? ModeleId) : IQuery<PageDuJournalVue>;

public sealed class ListerLeJournalHandler(
    ISentNotificationRepository notifications,
    ICallerContext caller) : IQueryHandler<ListerLeJournalQuery, PageDuJournalVue>
{
    private const int ParDefaut = 50;
    private const int Plafond = 200;

    public async Task<PageDuJournalVue> HandleAsync(
        ListerLeJournalQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // admin, ops ET support — PAS finance, décidé le 2 octobre 2026.
        //
        // MEME DECOUPE QUE L'ANNUAIRE DES CLIENTS, et pour la même raison : le
        // support est celui qui reçoit « je n'ai pas reçu mon code », tandis que
        // rapprocher une facture d'opérateur se fait sur des totaux, pas sur la
        // liste des personnes écrites. La vérification est ici parce que la
        // politique de la passerelle ne protège rien (ADR 0007).
        if (!caller.IsInRole(HbaRoles.Admin)
            && !caller.IsInRole(HbaRoles.Ops)
            && !caller.IsInRole(HbaRoles.Support))
        {
            throw new ForbiddenException("Réservé aux rôles admin, ops et support.");
        }

        var limite = query.Limite <= 0 ? ParDefaut : Math.Min(query.Limite, Plafond);

        var page = await notifications.ListerAsync(
            new FiltreDuJournal(
                query.Canal,
                query.Statut,
                query.FinDuDestinataire,
                query.ModeleId,
                limite,
                query.Decalage),
            cancellationToken).ConfigureAwait(false);

        return new PageDuJournalVue([.. page.Lignes.Select(Vers)], page.Total);
    }

    private static LigneDuJournalVue Vers(SentNotification ligne) => new()
    {
        Id = ligne.Id,
        Canal = ligne.Channel,
        DestinataireMasque = MasqueDuDestinataire.Appliquer(ligne.Recipient),
        ModeleId = ligne.TemplateId,
        Statut = ligne.Status,
        Operateur = ligne.Provider,
        ReferenceOperateur = ligne.ProviderMessageId,
        Erreur = ligne.Error,
        Tentatives = ligne.Attempts,
        CorrelationId = ligne.CorrelationId,
        CreeLe = ligne.CreatedAt,
        TermineLe = ligne.CompletedAt,
    };
}

/// <summary>
/// Ne garde que les quatre derniers caractères.
///
/// QUATRE, PARCE QU'ILS SUFFISENT A RECONNAITRE et pas à composer. C'est ce
/// qu'un opérateur compare au numéro qu'un client vient de lui donner au
/// téléphone. Même règle que MasqueTelephone dans Directory — recopiée et non
/// partagée, parce qu'un service ne prend pas de dépendance sur la couche
/// applicative d'un autre.
///
/// LE DESTINATAIRE N'EST PAS TOUJOURS UN NUMERO : c'est un identifiant de
/// compte pour le push, une adresse pour le courriel. Le masque vaut pour les
/// trois — on ne rend jamais de quoi écrire à quelqu'un.
/// </summary>
public static class MasqueDuDestinataire
{
    public static string Appliquer(string destinataire)
    {
        if (string.IsNullOrWhiteSpace(destinataire))
        {
            return string.Empty;
        }

        var net = destinataire.Trim();

        return net.Length <= 4 ? new string('•', net.Length) : $"••••{net[^4..]}";
    }
}
