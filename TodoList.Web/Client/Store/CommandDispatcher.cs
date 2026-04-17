// TodoList.Web/Client/Store/CommandDispatcher.cs
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using MudBlazor;
using TodoList.Domain.Sagas;
using TodoList.Web.Client.Services;
using Wolverine;

namespace TodoList.Web.Client.Store;

public class CommandDispatcher
{
    private readonly IClientStore _store;
    private readonly IConnectivityService _connectivity;
    private readonly HttpClient _http;
    private readonly OperationPoller _poller;
    private readonly ISnackbar _snackbar;

    // Populated at startup by reflecting over Wolverine.Saga subclasses in TodoList.Domain
    private readonly HashSet<string> _sagaInitiatingTypeNames;

    public CommandDispatcher(
        IClientStore store,
        IConnectivityService connectivity,
        HttpClient http,
        OperationPoller poller,
        ISnackbar snackbar)
    {
        _store = store;
        _connectivity = connectivity;
        _http = http;
        _poller = poller;
        _snackbar = snackbar;

        _sagaInitiatingTypeNames = DiscoverSagaInitiatingTypes();
    }

    /// <summary>
    /// Reflects over Wolverine.Saga subclasses in TodoList.Domain and extracts the first
    /// parameter type name of each saga's static Start method. The first parameter is the
    /// triggering domain event by convention (e.g. <c>TodoDueDateSetEvent</c>).
    ///
    /// Returned names have the <c>Event</c> suffix stripped to match the <c>Type</c> field
    /// of <see cref="ClientEvent"/> (e.g. <c>TodoDueDateSet</c>).
    /// </summary>
    private static HashSet<string> DiscoverSagaInitiatingTypes()
    {
        var sagaBase = typeof(Saga);
        var domainAssembly = typeof(DueReminderSaga).Assembly;

        var results = new HashSet<string>();
        foreach (var sagaType in domainAssembly.GetTypes()
                     .Where(t => t.IsClass && !t.IsAbstract && sagaBase.IsAssignableFrom(t)))
        {
            var startMethod = sagaType.GetMethod(
                "Start",
                BindingFlags.Public | BindingFlags.Static);
            if (startMethod is null) continue;

            var first = startMethod.GetParameters().FirstOrDefault();
            if (first is null) continue;

            var name = first.ParameterType.Name;
            if (name.EndsWith("Event")) name = name[..^"Event".Length];
            results.Add(name);
        }
        return results;
    }

    /// <summary>
    /// Dispatches a command. Returns null on success, or a list of validation errors.
    /// </summary>
    public async Task<IReadOnlyList<ValidationError>?> Dispatch(ClientCommand command, ClientEvent speculativeEvent)
    {
        // 1. Write speculative event immediately
        _store.AppendEvent(speculativeEvent with { State = EventState.Speculative, Source = EventSource.Client });

        // 2. Enqueue command
        _store.EnqueueCommand(command with { Synced = false });

        // 3. Read models rebuild via OnAggregateChanged (already fired by AppendEvent)

        // Match the saga by the speculative event's type — the Start method's first
        // parameter is the triggering domain event (suffix "Event" stripped).
        var startsSaga = _sagaInitiatingTypeNames.Contains(speculativeEvent.Type);

        // 4. Online: dispatch to server
        if (_connectivity.IsOnline)
        {
            if (startsSaga)
                _snackbar.Add("Background work will begin shortly.", Severity.Info);

            await DispatchToServer(command, speculativeEvent.AggregateId);
        }
        else
        {
            if (startsSaga)
                _snackbar.Add("This action will begin when you're back online.", Severity.Info);
            // Command stays queued — SyncService replays on reconnect
        }

        return null; // No client-side validation errors (passed in from caller before Dispatch)
    }

    internal async Task DispatchToServer(ClientCommand command, string aggregateId)
    {
        const int maxRetries = 3;
        int[] retryDelaysMs = [200, 400, 800];

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var method = new HttpMethod(command.HttpMethod);
                var request = new HttpRequestMessage(method, command.ApiEndpoint)
                {
                    Content = command.Payload is { } payload ? JsonContent.Create(payload) : null
                };
                request.Headers.Add("X-Expected-Version", command.ExpectedVersion.ToString());

                var response = await _http.SendAsync(request);

                // Retry on 5xx — transient server error
                var statusCode = (int)response.StatusCode;
                if (statusCode >= 500 && attempt < maxRetries)
                {
                    await Task.Delay(retryDelaysMs[attempt]);
                    continue;
                }

                switch (response.StatusCode)
                {
                case HttpStatusCode.Accepted: // 202
                {
                    var location = response.Headers.Location?.ToString()
                        ?? response.Headers.GetValues("Location").FirstOrDefault();
                    if (location is null) break;
                    var operationId = location.Split('/').Last();
                    var result = await _poller.PollAsync(operationId);

                    if (result.IsSuccess && result.Event is not null)
                    {
                        _store.ReplaceSpeculative(aggregateId, [result.Event]);
                        _store.MarkSynced(command.Id);
                    }
                    else
                    {
                        _snackbar.Add($"Sync failed: {result.ErrorMessage}", Severity.Error);
                    }
                    break;
                }
                case HttpStatusCode.Conflict: // 409
                {
                    var body = await response.Content.ReadFromJsonAsync<ConflictResponse>();
                    if (body?.ServerEvents is not null)
                    {
                        _store.ReplaceSpeculative(aggregateId, body.ServerEvents);
                        _store.MarkSynced(command.Id);
                        _snackbar.Add(
                            $"Your change was overridden by a newer update.",
                            Severity.Warning,
                            config =>
                            {
                                config.Action = "Redo";
                                config.ActionColor = Color.Primary;
                                config.OnClick = snackbar =>
                                {
                                    // Re-queue with updated ExpectedVersion
                                    var currentVersion = body.ServerEvents.Max(e => e.AggregateVersion);
                                    _ = DispatchToServer(command with { ExpectedVersion = currentVersion }, aggregateId);
                                    return Task.CompletedTask;
                                };
                            });
                    }
                    break;
                }
                case HttpStatusCode.UnprocessableEntity: // 422
                {
                    var body = await response.Content.ReadFromJsonAsync<ValidationConflictResponse>();
                    if (body?.Errors is not null)
                    {
                        _store.MarkConflicted(aggregateId, body.Errors
                            .Select(e => new ValidationError(e.Field, e.Message))
                            .ToList());
                        _snackbar.Add(
                            $"Couldn't be saved — {body.Errors.FirstOrDefault()?.Message}",
                            Severity.Warning,
                            config =>
                            {
                                config.Action = "Review";
                                config.ActionColor = Color.Warning;
                                // Review action navigates to the item — caller can listen to OnReviewRequested
                            });
                    }
                    break;
                }
                case HttpStatusCode.NotFound: // 404 on delete/complete = treat as success
                {
                    _store.DiscardSpeculative(aggregateId);
                    _store.MarkSynced(command.Id);
                    break;
                }
                case HttpStatusCode.Unauthorized: // 401
                {
                    // Let ConnectivityBanner or auth redirect handle this
                    break;
                }
                default:
                {
                    if (statusCode >= 500)
                    {
                        // All retries exhausted — leave in unsynced state
                        break;
                    }
                    // Other 4xx — discard speculative, show error
                    _store.DiscardSpeculative(aggregateId);
                    _store.MarkSynced(command.Id);
                    _snackbar.Add($"Command failed ({statusCode})", Severity.Error);
                    break;
                }
            }

            // If we handled the response, break out of retry loop
            break;
        }
        catch (HttpRequestException)
        {
            if (attempt < maxRetries)
            {
                await Task.Delay(retryDelaysMs[attempt]);
                continue;
            }
            // Network error — leave in unsynced state, SyncService replays on reconnect
        }
        } // end for
    }
}

public record ConflictResponse
{
    public string CommandId { get; init; } = "";
    public string AggregateId { get; init; } = "";
    public IReadOnlyList<ClientEvent>? ServerEvents { get; init; }
}

public record ValidationConflictResponse
{
    public string CommandId { get; init; } = "";
    public IReadOnlyList<ValidationErrorDto>? Errors { get; init; }
}

public record ValidationErrorDto
{
    public string Field { get; init; } = "";
    public string Message { get; init; } = "";
}
