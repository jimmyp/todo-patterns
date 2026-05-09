// TodoList.Web/Client/Store/IClientStore.cs
namespace TodoList.Web.Client.Store;

public interface IClientStore
{
    // Event log
    void AppendEvent(ClientEvent evt);
    IReadOnlyList<ClientEvent> GetEventsFor(string aggregateId);
    IReadOnlyList<ClientEvent> GetAllEvents();           // ordered by (aggregateId, aggregateVersion)
    void ReplaceSpeculative(string aggregateId, IReadOnlyList<ClientEvent> serverEvents);
    void MarkConflicted(string aggregateId, IReadOnlyList<ValidationError> errors);
    void DiscardSpeculative(string aggregateId);

    // Command queue
    IReadOnlyList<ClientCommand> GetUnsyncedCommands();
    void EnqueueCommand(ClientCommand command);
    void MarkSynced(string commandId);

    // Query helpers for UI state
    bool HasUnsyncedCommand(string aggregateId);
    bool HasConflictedEvents(string aggregateId);

    // Change notifications
    event Action<string> OnAggregateChanged; // fires with aggregateId after every mutation
}

public record ValidationError(string Field, string Message);
