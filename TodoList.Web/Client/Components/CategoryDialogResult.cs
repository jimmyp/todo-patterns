namespace TodoList.Web.Client.Components;

public record CategoryDialogResult
{
    public string Name { get; init; } = "";
    public string Color { get; init; } = "#8B5CF6";
    public string Icon { get; init; } = "";
}
