using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace FileBackupper.Db
{
    class PathInfo
    {
        public int Id { get; set; }

        public Target Target { get; set; }

        [Index]
        public string Name { get; set; }

        public PathInfo Path { get; set; }

        [ForeignKey("Path")]
        public int PathId { get; set; }

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
