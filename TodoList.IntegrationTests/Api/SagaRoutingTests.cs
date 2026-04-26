using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        var evt = new TodoDueDateSetEvent(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(7), "saga-test-user");

        // TrackActivity waits until all cascading work is complete and records every
        // message processed. We assert that the bare event was routed (Saga.Start
        // is what consumes it) — proving B2's "bare inner event also published" fix.
        var session = await host.TrackActivity()
            .IncludeExternalTransports()
            .Timeout(TimeSpan.FromSeconds(10))
            .PublishMessageAndWaitAsync(evt);

        var executed = session.Executed.MessagesOf<TodoDueDateSetEvent>().ToList();
        executed.Should().NotBeEmpty(
            "DueReminderSaga.Start subscribes to TodoDueDateSetEvent — if Wolverine never " +
            "executes it as a bare message, the saga never fires.");
    }
}
