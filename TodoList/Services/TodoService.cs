using System.Collections.Generic;
using System.Linq;
using TodoList.Models;

namespace TodoList.Services
{
    public class TodoService
    {
        private readonly List<TodoItem> _items = new List<TodoItem>();
        private int _nextId = 1;

        public List<TodoItem> GetItems()
        {
            return _items;
        }

        public void AddItem(string title)
        {
            var item = new TodoItem
            {
                Id = _nextId++,
                Title = title,
                IsCompleted = false
            };
            _items.Add(item);
        }

        public void MarkComplete(int id)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item != null)
            {
                item.IsCompleted = true;
            }
        }

        public void DeleteItem(int id)
        {
            var item = _items.FirstOrDefault(x => x.Id == id);
            if (item != null)
            {
                _items.Remove(item);
            }
        }
    }
}
