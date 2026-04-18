// TodoList.Api/Sagas/DueReminderSaga.cs
using TodoList.Domain.Events;
using Wolverine;

namespace TodoList.Api.Sagas;

/// <summary>
/// Saga that fires a reminder when a todo's due date is within 24 hours.
///
/// Lifecycle:
///   1. Starts on TodoDueDateSetEvent — schedules the reminder 24h before the due date
///   2. Updates the schedule if the due date changes
///   3. Completes when the reminder fires OR when the due date is cleared/todo deleted
///
/// Lives in TodoList.Api because it depends on WolverineFx. The triggering event
/// (TodoDueDateSetEvent) is marked with [SagaInitiator] in TodoList.Domain so the
/// client can show an appropriate toast without taking a WolverineFx dependency.
/// </summary>
public class DueReminderSaga : Saga
{
    // Wolverine requires a public Id property — correlates saga to todo
    public Guid Id { get; set; }
    public string UserId { get; set; } = "";
    public DateTimeOffset DueDate { get; set; }
    public bool ReminderFired { get; set; }

    /// <summary>
    /// Starts the saga when a due date is set. Returns the saga and a scheduled
    /// reminder message as a cascading message.
    /// </summary>
    public static (DueReminderSaga, DeliveryMessage<DueReminderMessage>) Start(TodoDueDateSetEvent evt)
    {
        var saga = new DueReminderSaga
        {
            Id = evt.TodoId,
            UserId = evt.UserId ?? "anonymous",
            DueDate = evt.DueDate
        };

        var reminderTime = evt.DueDate.AddHours(-24);
        var delay = reminderTime - DateTimeOffset.UtcNow;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

        var scheduled = new DeliveryMessage<DueReminderMessage>(
            new DueReminderMessage(saga.Id, saga.UserId, saga.DueDate),
            new DeliveryOptions { ScheduleDelay = delay });

        return (saga, scheduled);
    }

    /// <summary>
    /// Updates the saga when the due date changes. Reschedules the reminder.
    /// </summary>
    public DeliveryMessage<DueReminderMessage> Handle(TodoDueDateSetEvent evt)
    {
        DueDate = evt.DueDate;
        UserId = evt.UserId ?? UserId;
        ReminderFired = false;

        var reminderTime = DueDate.AddHours(-24);
        var delay = reminderTime - DateTimeOffset.UtcNow;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;

        return new DeliveryMessage<DueReminderMessage>(
            new DueReminderMessage(Id, UserId, DueDate),
            new DeliveryOptions { ScheduleDelay = delay });
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
    public void Handle(TodoDueDateClearedEvent evt) => MarkCompleted();
}
