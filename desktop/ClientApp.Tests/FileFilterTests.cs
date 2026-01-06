using ClientApp.Models;

namespace ClientApp.Tests
{
    public class FileFilterTests
    {
        [Fact]
        public void ApplyFilter_ImageFilter_ReturnsOnlyImages()
        {
            // Arrange
            var files = new List<FileItem>
            {
                new FileItem { Name = "photo.png" },
                new FileItem { Name = "document.pdf" },
                new FileItem { Name = "image.png" },
                new FileItem { Name = "script." }
            };

            // Act
            var filtered = files.Where(f => f.IsImage).ToList();

            // Assert
            Assert.Equal(2, filtered.Count);
            Assert.All(filtered, f => Assert.True(f.IsImage));
        }

        [Fact]
        public void ApplyFilter_CodeFilter_ReturnsOnlyCodeFiles()
        {
            // Arrange
            var files = new List<FileItem>
            {
                new FileItem { Name = "script.js" },
                new FileItem { Name = "app.js" },
                new FileItem { Name = "photo.png" },
                new FileItem { Name = "index.js" }
            };

            // Act
            var filtered = files.Where(f => f.IsCode).ToList();

            // Assert
            Assert.Equal(3, filtered.Count);
            Assert.All(filtered, f => Assert.True(f.IsCode));
        }
    }
}