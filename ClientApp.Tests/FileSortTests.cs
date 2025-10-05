using ClientApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClientApp.Tests
{
    public class FileSortTests
    {
        [Fact]
        public void SortByExtension_Ascending_SortsCorrectly()
        {
            // Arrange
            var files = new List<FileItem>
            {
                new FileItem { Name = "photo.png" },
                new FileItem { Name = "script.py" },
                new FileItem { Name = "app.cs"   },
                new FileItem { Name = "document.docx"  },
                new FileItem { Name = "no_ext" }
            };

            // Act
            var sorted = files
                .OrderBy(f => f.Extension ?? string.Empty)
                .ThenBy(f => f.Name)
                .ToList();

            // Assert
            Assert.Equal("", sorted[0].Extension); // Файли без розширення першими
            Assert.Equal(".cs", sorted[1].Extension);
            Assert.Equal(".docx", sorted[2].Extension);
            Assert.Equal(".png", sorted[3].Extension);
            Assert.Equal(".py", sorted[4].Extension);
        }

        [Fact]
        public void SortByExtension_Descending_SortsCorrectly()
        {
            // Arrange
            var files = new List<FileItem>
            {
                new FileItem { Name = "photo.png" },
                new FileItem { Name = "script.py" },
                new FileItem { Name = "app.cs" },
                new FileItem { Name = "no_ext" }
            };

            // Act
            var sorted = files
                .OrderByDescending(f => f.Extension ?? string.Empty)
                .ThenBy(f => f.Name)
                .ToList();

            // Assert
            Assert.Equal(".py", sorted[0].Extension);
            Assert.Equal(".png", sorted[1].Extension);
            Assert.Equal(".cs", sorted[2].Extension);
            Assert.Equal("", sorted[3].Extension); // Файли без розширення останніми
        }

        [Fact]
        public void SortByExtension_WithSameExtension_SortsByName()
        {
            // Arrange
            var files = new List<FileItem>
            {
                new FileItem { Name = "zebra.txt" },
                new FileItem { Name = "apple.txt" },
                new FileItem { Name = "banana.txt" }
            };

            // Act
            var sorted = files
                .OrderBy(f => f.Extension ?? string.Empty)
                .ThenBy(f => f.Name)
                .ToList();

            // Assert
            Assert.Equal("apple.txt", sorted[0].Name);
            Assert.Equal("banana.txt", sorted[1].Name);
            Assert.Equal("zebra.txt", sorted[2].Name);
        }
    }
}
