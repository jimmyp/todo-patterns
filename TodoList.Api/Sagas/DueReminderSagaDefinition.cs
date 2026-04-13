// TodoList.Api/Sagas/DueReminderSagaDefinition.cs
using TodoList.Domain.Commands;
using TodoList.Domain.Sagas;

namespace TodoList.Api.Sagas;

/// <summary>
/// Implements ISagaDefinition so the Blazor client can detect saga-initiating commands
/// (e.g. to show "this operation may take a while" UI).
/// </summary>
public class DueReminderSagaDefinition : ISagaDefinition
{
    public Type InitiatingCommandType => typeof(SetDueDateCommand);
    public string Description => "Sends a reminder notification 24 hours before a todo's due date.";
}
