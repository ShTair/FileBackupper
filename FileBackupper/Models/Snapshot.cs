using System;
using System.Collections.Generic;

namespace FileBackupper.Models
{
    class Snapshot
    {
        public DateTime Creation { get; set; }

        public string Target { get; set; }

        public IList<PathInfo> Paths { get; set; }
    }
}
