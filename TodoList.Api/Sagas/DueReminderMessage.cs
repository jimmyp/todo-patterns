// TodoList.Api/Sagas/DueReminderMessage.cs
using Wolverine.Persistence.Sagas;

namespace TodoList.Api.Sagas;

// [SagaIdentity] tags TodoId as the saga state id so DueReminderSaga.Handle(DueReminderMessage)
// can resolve the existing saga. Without this Wolverine throws IndeterminateSagaStateIdException
// because none of the property names match its conventions (Id, SagaId, DueReminderSagaId).
public record DueReminderMessage([property: SagaIdentity] Guid TodoId, string UserId, DateTimeOffset DueDate);
