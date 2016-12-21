using System;
using System.Collections.Generic;

namespace FileBackupper.Models
{
    class LogItem
    {
        public int ItemCount { get; set; }

        public int FileCount { get; set; }

        public int SameItemCount { get; set; }

        public int NewItemCount => NewItems.Count;

        public int SkipItemCount => SkipItems.Count;

        public int UpdatedItemCount => UpdatedItems.Count;

        public int LostItemCount => LostItems.Count;

        public int DeleteSnapshotCount { get; set; }

        public int DeletePathInfoCount { get; set; }

        public int DeleteFileCount { get; set; }

        public int StoredFileCount { get; set; }

        public long StoredFileSize { get; set; }

        public DateTime Start { get; set; }

        public DateTime Finish { get; set; }

        public TimeSpan Elapsed => Finish - Start;

        public IList<string> Exceptions { get; set; }

        public IList<string> NewItems { get; set; }

        public IList<string> UpdatedItems { get; set; }

        public IList<string> SkipItems { get; set; }

        public IList<string> LostItems { get; set; }

        public IList<PairItem> PairItems { get; set; }
    }

    class PairItem
    {
        public int Id { get; set; }

        public string Md5 { get; set; }

        public long Size { get; set; }

        public IList<string> Paths { get; set; }
    }
}
