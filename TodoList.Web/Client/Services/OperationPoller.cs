// TodoList.Web/Client/Services/OperationPoller.cs
using System.Net.Http.Json;

namespace TodoList.Web.Client.Services;

public class OperationPoller
{
    private readonly HttpClient _http;

    public OperationPoller(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Polls GET /operations/{id} until terminal status.
    /// Returns the confirmed ClientEvent on success, null on failure/timeout.
    /// </summary>
    public async Task<OperationResult> PollAsync(string operationId, CancellationToken ct = default)
    {
        var delays = new[] { 200, 400, 800, 1600, 3200 };
        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var response = await _http.GetAsync($"/operations/{operationId}", ct);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadFromJsonAsync<OperationResponse>(ct);
                    if (body is null) return OperationResult.Failed("Empty response");

                    return body.Status switch
                    {
                        "complete" => OperationResult.Success(body.Event),
                        "failed" => OperationResult.Failed(body.Error ?? "Operation failed"),
                        "pending" or "processing" => null!, // keep polling
                        _ => OperationResult.Failed($"Unknown status: {body.Status}")
                    };
                }

                if ((int)response.StatusCode >= 500)
                {
                    // Transient — retry
                }
                else
                {
                    return OperationResult.Failed($"HTTP {(int)response.StatusCode}");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { /* network error — retry */ }

            if (attempt >= delays.Length) return OperationResult.Failed("Polling timeout");
            await Task.Delay(delays[attempt++], ct);
        }

        return OperationResult.Failed("Cancelled");
    }
}

public record OperationResponse
{
    public string Status { get; init; } = "";
    public TodoList.Web.Client.Store.ClientEvent? Event { get; init; }
    public string? Error { get; init; }
}

public record OperationResult
{
    public bool IsSuccess { get; init; }
    public TodoList.Web.Client.Store.ClientEvent? Event { get; init; }
    public string? ErrorMessage { get; init; }

    public static OperationResult Success(TodoList.Web.Client.Store.ClientEvent? evt) =>
        new() { IsSuccess = true, Event = evt };
    public static OperationResult Failed(string error) =>
        new() { IsSuccess = false, ErrorMessage = error };
}
