// TodoList.Domain/Sagas/SagaInitiatorAttribute.cs
namespace TodoList.Domain.Sagas;

/// <summary>
/// Marks a domain event (or command) as one that initiates a saga.
///
/// Used by the client to decide whether to show a "background work starting" toast when
/// dispatching a command that produces this event speculatively. Scanning by attribute
/// keeps the Domain assembly free of the WolverineFx dependency — the actual saga lives
/// in TodoList.Api/Sagas/.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SagaInitiatorAttribute : Attribute;
