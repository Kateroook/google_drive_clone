using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClientApp.Tests
{
    public class FileIgnoreTests
    {
        [Theory]
        [InlineData("~$document.docx", true)] // Тимчасові файли Word
        [InlineData(".gitignore", true)] // Приховані файли
        [InlineData("file.tmp", true)] // Тимчасові файли
        [InlineData("download.crdownload", true)] // Chrome incomplete
        [InlineData("regular.txt", false)] // Звичайний файл
        [InlineData("photo.jpg", false)] // Зображення
        public void ShouldIgnore_VariousFiles_ReturnsExpectedResult(string fileName, bool shouldIgnore)
        {
            // Act
            var result = ShouldIgnoreFile(fileName);

            // Assert
            Assert.Equal(shouldIgnore, result);
        }

        private bool ShouldIgnoreFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return true;
            if (fileName.StartsWith("~$")) return true;
            if (fileName.StartsWith(".")) return true;

            var ext = Path.GetExtension(fileName)?.ToLower();
            if (ext == ".tmp" || ext == ".temp" || ext == ".part" || ext == ".crdownload")
                return true;

            return false;
        }
    }
}
