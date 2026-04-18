// TodoList.Web/Client/Services/OperationPoller.cs
using System.Net.Http.Json;
using System.Text.Json;

namespace TodoList.Web.Client.Services;

public class OperationPoller
{
    private readonly HttpClient _http;

    public OperationPoller(HttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Polls GET /todos/operations/{id} until terminal status.
    /// Returns the operation result on success, failure reason on failure.
    /// </summary>
    public async Task<OperationResult> PollAsync(string operationId, CancellationToken ct = default)
    {
        var delays = new[] { 200, 400, 800, 1600, 3200 };
        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var response = await _http.GetAsync($"/todos/operations/{operationId}", ct);

                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadFromJsonAsync<OperationResponse>(ct);
                    if (body is null) return OperationResult.Failed("Empty response");

                    return body.Status switch
                    {
                        "complete" => OperationResult.Success(body.Result),
                        "failed" => OperationResult.Failed(body.FailureReason ?? "Operation failed"),
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
    public JsonElement? Result { get; init; }
    public string? FailureReason { get; init; }
    public bool IsRetryable { get; init; }
}

public record OperationResult
{
    public bool IsSuccess { get; init; }
    public JsonElement? Result { get; init; }
    public string? FailureReason { get; init; }

    public static OperationResult Success(JsonElement? result) =>
        new() { IsSuccess = true, Result = result };
    public static OperationResult Failed(string reason) =>
        new() { IsSuccess = false, FailureReason = reason };
}
