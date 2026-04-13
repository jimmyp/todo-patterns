// TodoList.Api/Sagas/DueReminderMessage.cs
namespace TodoList.Api.Sagas;

public record DueReminderMessage(Guid TodoId, string UserId, DateTimeOffset DueDate);
