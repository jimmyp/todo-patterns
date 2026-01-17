using Xunit;
using TodoList.Services;
using TodoList.Models;
using System.Linq;

namespace TodoList.Tests
{
    public class TodoServiceTests
    {
        [Fact]
        public void AddItem_ShouldAddItem()
        {
            var service = new TodoService();
            service.AddItem("Test Item");
            Assert.Single(service.GetItems());
            Assert.Equal("Test Item", service.GetItems().First().Title);
        }

        [Fact]
        public void MarkComplete_ShouldMarkItemAsComplete()
        {
            var service = new TodoService();
            service.AddItem("Test Item");
            var item = service.GetItems().First();
            service.MarkComplete(item.Id);
            Assert.True(item.IsCompleted);
        }

        [Fact]
        public void DeleteItem_ShouldRemoveItem()
        {
            var service = new TodoService();
            service.AddItem("Test Item");
            var item = service.GetItems().First();
            service.DeleteItem(item.Id);
            Assert.Empty(service.GetItems());
        }
    }
}
