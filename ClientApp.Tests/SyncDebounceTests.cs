using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClientApp.Tests
{
    public class SyncDebounceTests
    {
        [Fact]
        public void IsRapidDuplicate_WithinDebounceTime_ReturnsTrue()
        {
            // Arrange
            var lastEventAt = new ConcurrentDictionary<string, DateTime>();
            var filePath = "C:\\test\\file.txt";
            var debounceMs = 2000;

            // Перша подія
            lastEventAt[filePath] = DateTime.UtcNow;

            // Act - друга подія через 500ms
            System.Threading.Thread.Sleep(500);
            var now = DateTime.UtcNow;
            var isDuplicate = (now - lastEventAt[filePath]).TotalMilliseconds < debounceMs;

            // Assert
            Assert.True(isDuplicate);
        }

        [Fact]
        public void IsRapidDuplicate_AfterDebounceTime_ReturnsFalse()
        {
            // Arrange
            var lastEventAt = new ConcurrentDictionary<string, DateTime>();
            var filePath = "C:\\test\\file.txt";
            var debounceMs = 2000;

            // Перша подія
            lastEventAt[filePath] = DateTime.UtcNow.AddSeconds(-3);

            // Act - перевірка після 3 секунд
            var now = DateTime.UtcNow;
            var isDuplicate = (now - lastEventAt[filePath]).TotalMilliseconds < debounceMs;

            // Assert
            Assert.False(isDuplicate);
        }
    }
}
