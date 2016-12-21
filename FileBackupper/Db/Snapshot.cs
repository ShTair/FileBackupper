using System;
using System.Collections.Generic;

namespace FileBackupper.Db
{
    class Snapshot
    {
        public int Id { get; set; }

        public DateTime Creation { get; set; }

        public Target Target { get; set; }
    }
}
