// TodoList.Api/Handlers/NotificationHandlers.cs
using TodoList.Domain.Sagas;

namespace TodoList.Api.Handlers;

/// <summary>
/// Handles DueReminderMessage — the final delivery step from the saga.
/// Currently logs. Replace with SendGrid email in a future plan.
/// </summary>
public class NotificationHandlers(ILogger<NotificationHandlers> logger)
{
    public Task HandleAsync(DueReminderMessage msg)
    {
        logger.LogInformation(
            "Due reminder: Todo {TodoId} for user {UserId} is due at {DueDate}",
            msg.TodoId, msg.UserId, msg.DueDate);
        return Task.CompletedTask;
    }
}
