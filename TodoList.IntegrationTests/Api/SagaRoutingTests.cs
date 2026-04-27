using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TodoList.Api.Sagas;
using TodoList.Domain.Events;
using Wolverine;
using Wolverine.Tracking;

namespace TodoList.IntegrationTests.Api;

/// <summary>
/// Verifies that bare domain events (not just the UserScopedEvent envelope) reach
/// handlers that subscribe by domain type — most importantly DueReminderSaga.Start
/// which takes TodoDueDateSetEvent. Wolverine routes by message type, so without
/// re-publishing the inner event the saga would never start.
/// </summary>
[Trait("Category", "Integration")]
[Collection(ApiCollection.Name)]
public class SagaRoutingTests(ApiFixture fixture)
{
    [Fact]
    public async Task Bare_TodoDueDateSetEvent_reaches_DueReminderSaga()
    {
        var host = fixture.Services.GetRequiredService<IHost>();

        // Due date in the past — Saga.Start clamps a negative delay to zero, so the
        // cascading DueReminderMessage executes inline rather than being scheduled
        // hours/days out (the realistic case). Lets us observe the saga firing
        // synchronously in the tracking session.
        var evt = new TodoDueDateSetEvent(Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(-1), "saga-test-user");

        // Assert on the saga's CASCADING output (DueReminderMessage) rather than the
        // input event — Executed includes the published event itself, so asserting
        // MessagesOf<TodoDueDateSetEvent>() would pass even if no handler consumed it.
        // DueReminderMessage is only emitted by DueReminderSaga.Start, which proves
        // B2's "bare inner event reaches the saga" fix.
        var session = await host.TrackActivity()
            .IncludeExternalTransports()
            .Timeout(TimeSpan.FromSeconds(10))
            .PublishMessageAndWaitAsync(evt);

        var reminders = session.Executed.MessagesOf<DueReminderMessage>().ToList();
        reminders.Should().NotBeEmpty(
            "DueReminderSaga.Start emits a DueReminderMessage when it receives " +
            "TodoDueDateSetEvent. If the bare event never reaches the saga, no reminder " +
            "is cascaded and this list is empty.");
    }
}
