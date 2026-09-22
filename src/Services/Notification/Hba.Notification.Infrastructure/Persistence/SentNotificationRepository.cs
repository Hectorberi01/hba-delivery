using Hba.Notification.Application.Ports;
using Hba.Notification.Domain.Messages;

namespace Hba.Notification.Infrastructure.Persistence;

internal sealed class SentNotificationRepository(NotificationDbContext context) : ISentNotificationRepository
{
    public void Add(SentNotification notification) => context.SentNotifications.Add(notification);
}
