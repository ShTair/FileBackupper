using System;

namespace FileBackupper.Models
{
    class PathInfo
    {
        public string Path { get; set; }

        public int? Id { get; set; }

        public long Creation { get; set; }

        public long LastWrite { get; set; }
    }
}
