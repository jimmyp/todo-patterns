// TodoList.Tests/Sagas/DueReminderSagaTests.cs
using TodoList.Api.Sagas;
using TodoList.Domain.Events;
using Wolverine;

namespace TodoList.Tests.Sagas;

public class DueReminderSagaTests
{
    [Fact]
    public void Start_creates_saga_with_correct_state()
    {
        var todoId = Guid.NewGuid();
        var due = DateTimeOffset.UtcNow.AddDays(3);
        var evt = new TodoDueDateSetEvent(todoId, due, "user-1");

        var (saga, _) = DueReminderSaga.Start(evt);

        saga.Id.Should().Be(todoId);
        saga.UserId.Should().Be("user-1");
        saga.DueDate.Should().Be(due);
        saga.ReminderFired.Should().BeFalse();
    }

    [Fact]
    public void Start_cascades_a_scheduled_reminder_message()
    {
        var todoId = Guid.NewGuid();
        var due = DateTimeOffset.UtcNow.AddDays(3);
        var evt = new TodoDueDateSetEvent(todoId, due, "user-1");

        var (_, scheduled) = DueReminderSaga.Start(evt);

        scheduled.Should().NotBeNull();
        scheduled.Message.TodoId.Should().Be(todoId);
        scheduled.Message.DueDate.Should().Be(due);
        scheduled.Options.ScheduleDelay.Should().BeGreaterThan(TimeSpan.Zero);
    }

    [Fact]
    public void Handle_DueDateCleared_marks_saga_completed()
    {
        var saga = new DueReminderSaga
        {
            Id = Guid.NewGuid(),
            UserId = "user-1",
            DueDate = DateTimeOffset.UtcNow.AddDays(2)
        };

        saga.Handle(new TodoDueDateClearedEvent(saga.Id));

        saga.IsCompleted().Should().BeTrue();
    }

    [Fact]
    public void Handle_DueReminderMessage_marks_reminder_fired_and_completes()
    {
        var todoId = Guid.NewGuid();
        var saga = new DueReminderSaga
        {
            Id = todoId,
            UserId = "user-1",
            DueDate = DateTimeOffset.UtcNow.AddHours(12)
        };

        saga.Handle(new DueReminderMessage(todoId, "user-1", saga.DueDate));

        saga.ReminderFired.Should().BeTrue();
        saga.IsCompleted().Should().BeTrue();
    }

    [Fact]
    public void Handle_DueDateSet_within_24h_yields_immediate_reminder()
    {
        var todoId = Guid.NewGuid();
        var saga = new DueReminderSaga
        {
            Id = todoId,
            UserId = "user-1",
            DueDate = DateTimeOffset.UtcNow.AddHours(12)
        };
        // Due date already within 24h — reminder fires immediately (delay clamped to zero)
        var newDue = DateTimeOffset.UtcNow.AddHours(6);
        var evt = new TodoDueDateSetEvent(todoId, newDue, "user-1");

        var scheduled = saga.Handle(evt);

        scheduled.Should().NotBeNull();
        scheduled.Options.ScheduleDelay.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Handle_DueDateSet_far_future_yields_scheduled_delivery()
    {
        var todoId = Guid.NewGuid();
        var saga = new DueReminderSaga
        {
            Id = todoId,
            UserId = "user-1",
            DueDate = DateTimeOffset.UtcNow.AddDays(5)
        };
        var evt = new TodoDueDateSetEvent(todoId, DateTimeOffset.UtcNow.AddDays(5), "user-1");

        var scheduled = saga.Handle(evt);

        scheduled.Should().NotBeNull();
        scheduled.Options.ScheduleDelay.Should().BeGreaterThan(TimeSpan.FromDays(3));
    }
}
