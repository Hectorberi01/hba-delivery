using Hba.Notification.Application.Ports;
using Hba.Notification.Domain.Messages;
using Microsoft.EntityFrameworkCore;

namespace Hba.Notification.Infrastructure.Persistence;

internal sealed class SentNotificationRepository(NotificationDbContext context) : ISentNotificationRepository
{
    public void Add(SentNotification notification) => context.SentNotifications.Add(notification);

    /// <summary>
    /// DEUX REQUETES, ET LA SECONDE N'EST PAS DU LUXE. Compter séparément coûte
    /// un aller-retour ; sans lui, l'écran ne peut pas dire si les cinquante
    /// lignes affichées sont tout ce qu'il y a ou le début d'un millier.
    ///
    /// LE FILTRE SUR LE DESTINATAIRE PORTE SUR LA FIN DU NUMERO, parce que
    /// c'est tout ce que la liste montre : chercher sur ce qu'on n'affiche pas
    /// transformerait l'écran en annuaire interrogeable.
    ///
    /// AUCUN INDEX NE SERT CE « LIKE '%...' » — et c'est acceptable tant que le
    /// support cherche un numéro précis dans un journal de quelques dizaines de
    /// milliers de lignes. Au-delà, il faudra une colonne portant les quatre
    /// derniers chiffres, indexée.
    /// </summary>
    public async Task<PageDuJournal> ListerAsync(
        FiltreDuJournal filtre,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filtre);

        var requete = context.SentNotifications.AsNoTracking().AsQueryable();

        if (filtre.Canal is { } canal)
        {
            requete = requete.Where(n => n.Channel == canal);
        }

        if (filtre.Statut is { } statut)
        {
            requete = requete.Where(n => n.Status == statut);
        }

        if (!string.IsNullOrWhiteSpace(filtre.ModeleId))
        {
            requete = requete.Where(n => n.TemplateId == filtre.ModeleId);
        }

        if (!string.IsNullOrWhiteSpace(filtre.FinDuDestinataire))
        {
            var fin = filtre.FinDuDestinataire.Trim();
            requete = requete.Where(n => EF.Functions.Like(n.Recipient, "%" + fin));
        }

        var total = await requete.CountAsync(cancellationToken).ConfigureAwait(false);

        var lignes = await requete
            .OrderByDescending(n => n.CreatedAt)
            .Skip(filtre.Decalage < 0 ? 0 : filtre.Decalage)
            .Take(filtre.Limite < 1 ? 1 : filtre.Limite)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PageDuJournal(lignes, total);
    }
}
