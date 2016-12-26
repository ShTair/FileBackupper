using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace FileBackupper.Db
{
    class PathInfo
    {
        public int Id { get; set; }

        public Target Target { get; set; }

        [Index]
        public string Name { get; set; }

        public PathInfo Directory { get; set; }

        [ForeignKey("Path")]
        public int? DirectoryId { get; set; }

        public ICollection<PathInfo> Children { get; set; }

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

        [NotMapped]
        public string Path => DirectoryId + "\\" + Name;
    }
}
