using FileBackupper.Db;
using System.IO;

namespace FileBackupper.Models
{
    class TempPath
    {
        public int Id { get; set; }

        public string OPath { get; set; }

        public string VPath { get; set; }

        public string BPath { get; set; }

        public byte[] Md5 { get; set; }

        public string Md5Str { get; set; }

        public FileInfo File { get; set; }

        public ItemInfo Item { get; set; }
    }
}
