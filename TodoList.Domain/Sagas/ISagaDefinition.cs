// TodoList.Domain/Sagas/ISagaDefinition.cs
namespace TodoList.Domain.Sagas;

/// <summary>
/// Marker interface for saga definitions. Implementations declare which command type
/// initiates the saga. The Blazor client reflects over these at startup to detect
/// saga-initiating commands and show appropriate offline toasts.
/// </summary>
public interface ISagaDefinition
{
    Type InitiatingCommandType { get; }
    string Description { get; }
}
