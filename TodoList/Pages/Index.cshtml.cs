using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TodoList.Services;
using TodoList.Models;
using System.Collections.Generic;

namespace TodoList.Pages;

public class IndexModel : PageModel
{
    private readonly TodoService _todoService;

    public IndexModel(TodoService todoService)
    {
        _todoService = todoService;
    }

    public List<TodoItem> TodoItems { get; set; } = new List<TodoItem>();

    [BindProperty]
    public string NewTodoTitle { get; set; } = string.Empty;

    public void OnGet()
    {
        TodoItems = _todoService.GetItems();
    }

    public IActionResult OnPostAdd()
    {
        if (!string.IsNullOrWhiteSpace(NewTodoTitle))
        {
            _todoService.AddItem(NewTodoTitle);
        }
        return RedirectToPage();
    }

    public IActionResult OnPostComplete(int id)
    {
        _todoService.MarkComplete(id);
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(int id)
    {
        _todoService.DeleteItem(id);
        return RedirectToPage();
    }
}
