using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace FileBackupper.Db
{
    class ItemInfo
    {
        public int Id { get; set; }

        public long Size { get; set; }

        [Index]
        public byte[] Md5 { get; set; }

        public ICollection<PathInfo> Paths { get; set; }
    }
}
