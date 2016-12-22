using FileBackupper.Db;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace FileBackupper
{
    class FileChangeChecker
    {
        private static MD5 _md5;

        private Dictionary<string, PathInfo> _oldPaths;

        private List<PathInfo> _updatePaths;

        public FileChangeChecker()
        {

        }

        private void Check(DirectoryInfo dir, PathInfo ppath)
        {
            foreach (var info in dir.EnumerateDirectories())
            {
                var name = info.Name;
                var pc = info.CreationTimeUtc.ToBinary();
                var pm = info.LastWriteTimeUtc.ToBinary();
                var key = ppath.Id + "\\" + name;

                PathInfo pi;
                if (_oldPaths.TryGetValue(key, out pi) && pi.Item == null)
                {
                    if (pi.Creation != pc || pi.LastWrite != pm)
                    {
                        _updatePaths.Add(pi);
                        // update
                    }

                    // exists

                    _oldPaths.Remove(key);
                }

                Check(info, pi);
            }

            foreach (var info in dir.EnumerateFiles())
            {
                var name = info.Name;
                var pc = info.CreationTimeUtc.ToBinary();
                var pm = info.LastWriteTimeUtc.ToBinary();
                var key = ppath.Id + "\\" + name;

                PathInfo pi;
                if (_oldPaths.TryGetValue(key, out pi) && pi.Item == null)
                {
                    if (pi.Creation != pc || pi.LastWrite != pm)
                    {
                        _updatePaths.Add(pi);
                        // update
                    }

                    // exists

                    _oldPaths.Remove(key);
                }
            }
        }

        #region utils

        private static byte[] CalculateMd5(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                return _md5.ComputeHash(stream);
            }
        }

        #endregion
    }
}
