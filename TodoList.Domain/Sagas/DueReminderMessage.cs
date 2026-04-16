// TodoList.Domain/Sagas/DueReminderMessage.cs
namespace TodoList.Domain.Sagas;

public record DueReminderMessage(Guid TodoId, string UserId, DateTimeOffset DueDate);
