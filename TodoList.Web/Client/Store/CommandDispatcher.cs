// TodoList.Web/Client/Store/CommandDispatcher.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MudBlazor;
using TodoList.Domain;
using TodoList.Domain.Sagas;
using TodoList.Web.Client.Services;

namespace TodoList.Web.Client.Store;

public class CommandDispatcher
{
    private readonly IClientStore _store;
    private readonly IConnectivityService _connectivity;
    private readonly HttpClient _http;
    private readonly OperationPoller _poller;
    private readonly ISnackbar _snackbar;

    // Populated at startup by scanning TodoList.Domain for types marked [SagaInitiator]
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
    /// Scans TodoList.Domain for types decorated with <see cref="SagaInitiatorAttribute"/>.
    /// The attribute is applied to the domain event that triggers a saga (e.g.
    /// <c>TodoDueDateSetEvent</c>).
    ///
    /// Returned names have the <c>Event</c> suffix stripped to match the <c>Type</c> field
    /// of <see cref="ClientEvent"/> (e.g. <c>TodoDueDateSet</c>).
    ///
    /// Scanning Domain (not Api) keeps the client assembly free of the WolverineFx
    /// dependency — the actual saga implementation lives server-side.
    /// </summary>
    private static HashSet<string> DiscoverSagaInitiatingTypes()
    {
        var domainAssembly = typeof(IDomainEvent).Assembly;

        var results = new HashSet<string>();
        foreach (var type in domainAssembly.GetTypes()
                     .Where(t => t.GetCustomAttributes(typeof(SagaInitiatorAttribute), inherit: false).Length > 0))
        {
            var name = type.Name;
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

    private static readonly int[] RetryDelaysMs = [200, 400, 800];
    private const int MaxRetries = 3;

    internal async Task DispatchToServer(ClientCommand command, string aggregateId)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            HttpResponseMessage? response;
            try
            {
                response = await SendAsync(command);
            }
            catch (HttpRequestException)
            {
                // Network error — retry, or leave queued for SyncService on final attempt
                if (attempt < MaxRetries) { await Task.Delay(RetryDelaysMs[attempt]); continue; }
                return;
            }

            var statusCode = (int)response.StatusCode;
            if (statusCode >= 500 && attempt < MaxRetries)
            {
                await Task.Delay(RetryDelaysMs[attempt]);
                continue;
            }

            await HandleResponse(response, command, aggregateId);
            return;
        }
    }

    private Task<HttpResponseMessage> SendAsync(ClientCommand command)
    {
        var request = new HttpRequestMessage(new HttpMethod(command.HttpMethod), command.ApiEndpoint)
        {
            Content = command.Payload is { } payload ? JsonContent.Create(payload) : null
        };
        request.Headers.Add("X-Expected-Version", command.ExpectedVersion.ToString());
        return _http.SendAsync(request);
    }

    /// <summary>
    /// Decides what the client store should do based on the server response.
    /// One responsibility: response → store mutation. The retry loop is the caller's job.
    /// </summary>
    private async Task HandleResponse(HttpResponseMessage response, ClientCommand command, string aggregateId)
    {
        var statusCode = (int)response.StatusCode;
        switch (response.StatusCode)
        {
            case HttpStatusCode.Accepted: // 202 — async operation; poll until terminal
                await HandleAcceptedAsync(response, command, aggregateId);
                return;

            case HttpStatusCode.UnprocessableEntity: // 422 — validation errors on the synchronous reject path
                await HandleValidationConflictAsync(response, aggregateId);
                return;

            case HttpStatusCode.NotFound: // 404 on delete/complete = treat as success
                _store.DiscardSpeculative(aggregateId);
                _store.MarkSynced(command.Id);
                return;

            case HttpStatusCode.Unauthorized: // 401 — auth redirect handles it
                return;

            default:
                if (statusCode >= 500)
                    return; // Retries exhausted; leave queued for SyncService
                // Other 4xx — discard speculative, show error
                _store.DiscardSpeculative(aggregateId);
                _store.MarkSynced(command.Id);
                _snackbar.Add($"Command failed ({statusCode})", Severity.Error);
                return;
        }
    }

    private async Task HandleAcceptedAsync(HttpResponseMessage response, ClientCommand command, string aggregateId)
    {
        var location = response.Headers.Location?.ToString()
            ?? response.Headers.GetValues("Location").FirstOrDefault();
        if (location is null) return;

        var operationId = location.Split('/').Last();
        var result = await _poller.PollAsync(operationId);

        if (result.IsSuccess)
        {
            // The authoritative event will arrive via SignalR push; mark the command
            // synced so UI stops showing "unsynced". The speculative event remains in
            // place until the server event replaces it via ReplaceSpeculative.
            _store.MarkSynced(command.Id);
            return;
        }

        // Failed: branch on machine-readable FailureCode (no substring matching).
        if (result.FailureCode == FailureCodes.VersionConflict)
        {
            _store.DiscardSpeculative(aggregateId);
            _store.MarkSynced(command.Id);
            _snackbar.Add("Your change was overridden by a newer update.", Severity.Warning);
        }
        else
        {
            _store.DiscardSpeculative(aggregateId);
            _store.MarkSynced(command.Id);
            _snackbar.Add($"Sync failed: {result.FailureReason}", Severity.Error);
        }
    }

    private async Task HandleValidationConflictAsync(HttpResponseMessage response, string aggregateId)
    {
        var body = await response.Content.ReadFromJsonAsync<ValidationConflictResponse>();
        if (body?.Errors is null) return;

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
            });
    }
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
