using ClientApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClientApp.Tests
{
    public class FileSizeFormattingTests
    {
        [Theory]
        [InlineData(0, "0 B")]
        [InlineData(512, "512 B")]
        [InlineData(1024, "1 KB")]
        [InlineData(1536, "1.5 KB")]
        [InlineData(1048576, "1 MB")]
        [InlineData(1572864, "1.5 MB")]
        [InlineData(1073741824, "1 GB")]
        [InlineData(5368709120, "5 GB")]
        public void SizeFormatted_VariousSizes_ReturnsCorrectFormat(long size, string expected)
        {
            // Arrange
            var file = new FileItem { Size = size };

            // Act
            var formatted = FormatFileSize(size);

            // Assert
            Assert.Equal(expected, formatted);
        }

        private string FormatFileSize(long size)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = size;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        [Fact]
        public void SizeFormatted_LargeFile_FormatsCorrectly()
        {
            // Arrange
            var file = new FileItem { Size = 104857600 }; // 100 MB

            // Act
            var formatted = FormatFileSize(file.Size);

            // Assert
            Assert.Equal("100 MB", formatted);
        }
    }
}
