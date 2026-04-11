// TodoList.Web/Client/Services/IConnectivityService.cs
namespace TodoList.Web.Client.Services;

public interface IConnectivityService
{
    bool IsOnline { get; }
    event Action<bool> OnConnectivityChanged;
}
