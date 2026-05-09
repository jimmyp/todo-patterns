// TodoList.Web/Client/Services/ConnectivityService.cs
using Microsoft.JSInterop;

namespace TodoList.Web.Client.Services;

public class ConnectivityService : IConnectivityService, IAsyncDisposable
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private DotNetObjectReference<ConnectivityService>? _dotNetRef;

    public bool IsOnline { get; private set; } = true;
    public event Action<bool> OnConnectivityChanged = delegate { };

    public ConnectivityService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task InitializeAsync()
    {
        _module = await _js.InvokeAsync<IJSObjectReference>(
            "import", "./js/connectivity.js");
        _dotNetRef = DotNetObjectReference.Create(this);
        IsOnline = await _module.InvokeAsync<bool>("initialize", _dotNetRef);
    }

    [JSInvokable]
    public void OnConnectivityChangedJs(bool isOnline)
    {
        IsOnline = isOnline;
        OnConnectivityChanged(isOnline);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("dispose");
            await _module.DisposeAsync();
        }
        _dotNetRef?.Dispose();
    }
}
