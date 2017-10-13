using FileBackupper.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace FileRestorer
{
    class Program
    {
        static void Main(string[] args)
        {
            var json = File.ReadAllText(args[0]);
            var snaps = JsonConvert.DeserializeObject<List<Snapshot>>(json);

            var srcDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(args[0])), "Data");
            var dir = args[1];

            foreach (var snap in snaps)
            {
                foreach (var path in snap.Paths)
                {
                    var fullName = Path.Combine(dir, path.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullName));

                    var srcName = Path.Combine(srcDir, ((path.Id ?? 0) % 100).ToString("00"), ((path.Id ?? 0) % 10000).ToString("0000"), path.Id.ToString());

                    File.Copy(srcName, fullName);
                    File.SetCreationTime(fullName, DateTime.FromBinary(path.Creation));
                    File.SetLastWriteTime(fullName, DateTime.FromBinary(path.LastWrite));

                    Console.WriteLine(path.Path);
                }
            }
        }
    }
}
