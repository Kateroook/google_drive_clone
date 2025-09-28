using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClientApp.Models
{
    public class FileItem
    {
        public string Name { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
        public string UploadedBy { get; set; }
        public string EditedBy { get; set; }
        public string FileType { get; set; }
    }

}