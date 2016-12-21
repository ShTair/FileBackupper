using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace FileBackupper.Db
{
    class Target
    {
        public int Id { get; set; }

        [Index]
        public string Path { get; set; }

        public ICollection<Snapshot> Snapshots { get; set; }

        public ICollection<PathInfo> Paths { get; set; }
    }
}
