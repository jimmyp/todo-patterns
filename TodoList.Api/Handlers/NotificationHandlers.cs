// TodoList.Api/Handlers/NotificationHandlers.cs
using TodoList.Api.Sagas;
using Wolverine.Attributes;

namespace TodoList.Api.Handlers;

[WolverineHandler]
public class NotificationHandlers(ILogger<NotificationHandlers> logger)
{
    public Task Handle(DueReminderMessage msg)
    {
        logger.LogInformation(
            "Due reminder: Todo {TodoId} for user {UserId} is due at {DueDate}",
            msg.TodoId, msg.UserId, msg.DueDate);
        return Task.CompletedTask;
    }
}
