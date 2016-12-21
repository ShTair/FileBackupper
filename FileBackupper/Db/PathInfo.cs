using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FileBackupper.Db
{
    class PathInfo
    {
        public int Id { get; set; }

        public Target Target { get; set; }

        [Index]
        public string Path { get; set; }

        /// <summary>
        /// Itemがnullならディレクトリ
        /// </summary>
        public ItemInfo Item { get; set; }

        public long Creation { get; set; }

        public long LastWrite { get; set; }

        [Index]
        public DateTime RegisterDate { get; set; }

        [Index]
        public DateTime? RemoveDate { get; set; }
    }
}
