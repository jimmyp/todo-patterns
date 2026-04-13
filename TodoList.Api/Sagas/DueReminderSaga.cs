// TodoList.Api/Sagas/DueReminderSaga.cs
using TodoList.Domain.Events;
using Wolverine;
using Wolverine.Persistence.Sagas;

namespace TodoList.Api.Sagas;

/// <summary>
/// Saga that fires a reminder when a todo's due date is within 24 hours.
///
/// Lifecycle:
///   1. Starts on TodoDueDateSetEvent (or updates if already running)
///   2. Schedules a DueReminderMessage to be delivered 24 hours before DueDate
///   3. Completes when the reminder fires OR when the due date is cleared/todo deleted
/// </summary>
public class DueReminderSaga : Wolverine.Saga
{
    // Wolverine requires a public Id property — correlates saga to todo
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public DateTimeOffset DueDate { get; set; }
    public bool ReminderFired { get; set; }

    /// <summary>
    /// Starts the saga when a due date is set.
    /// </summary>
    public static DueReminderSaga Start(TodoDueDateSetEvent evt) =>
        new()
        {
            Id = evt.TodoId,
            UserId = evt.UserId ?? "anonymous",
            DueDate = evt.DueDate
        };

    /// <summary>
    /// Updates the saga if the due date changes while it is already running.
    /// Reschedules the reminder.
    /// </summary>
    public IEnumerable<object> Handle(TodoDueDateSetEvent evt)
    {
        DueDate = evt.DueDate;
        UserId = evt.UserId ?? UserId;
        ReminderFired = false;

        var reminderTime = DueDate.AddHours(-24);
        var delay = reminderTime - DateTimeOffset.UtcNow;

        if (delay > TimeSpan.Zero)
        {
            // Schedule reminder 24h before due date
            yield return new DeliveryMessage<DueReminderMessage>(
                new DueReminderMessage(Id, UserId, DueDate),
                new DeliveryOptions { ScheduleDelay = delay });
        }
        else
        {
            // Due date is already within 24 hours — fire immediately
            yield return new DueReminderMessage(Id, UserId, DueDate);
        }
    }

    /// <summary>
    /// Fires when the scheduled reminder arrives.
    /// </summary>
    public void Handle(DueReminderMessage msg)
    {
        ReminderFired = true;
        MarkCompleted();
    }

    /// <summary>
    /// Cancel the saga if the due date is cleared.
    /// </summary>
    public void Handle(TodoDueDateClearedEvent evt)
    {
        MarkCompleted();
    }
}
