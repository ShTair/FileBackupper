using FileBackupper.Db;
using Newtonsoft.Json;
using ShComp;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using M = FileBackupper.Models;

namespace FileBackupper
{
    class Program
    {
        private static MD5 _md5;
        private static M.StartInfo _settings;

        private static List<string> _vaults = new List<string>();
        private static List<string> _errSubVaults = new List<string>();
        private static string _logPath;

        private static M.LogItem _log = new M.LogItem
        {
            Exceptions = new List<string>(),
            NewItems = new List<string>(),
            UpdatedItems = new List<string>(),
            SkipItems = new List<string>(),
            LostItems = new List<string>(),
        };

        private static Dictionary<string, PathInfo> _oldPathInfos;
        private static List<PathInfo> _updatedPathInfos;

        static void Main(string[] args)
        {
            if (CheckParamater(args.Length > 0 ? args[0] : null))
            {
                _log.Start = DateTime.Now;
                _md5 = MD5.Create();

                var vaultLogPaths = _vaults.Select(vp => Path.Combine(vp, "Log")).ToList();
                vaultLogPaths.ForEach(dd => Directory.CreateDirectory(dd));

                var dbPath = Path.Combine(_logPath = vaultLogPaths[0], "db.sdf");
                using (var context = BackupContext.Create(dbPath))
                {
                    {
                        // 野良ターゲットの削除
                        context.Targets.RemoveRange(from en in context.Targets
                                                    where !en.Snapshots.Any()
                                                    select en);
                        context.SaveChanges();
                    }

                    var shots = new List<Snapshot>();
                    foreach (var targetPath in _settings.TargetPaths)
                    {
                        var target = (from en in context.Targets
                                      where en.Path == targetPath
                                      select en).FirstOrDefault();

                        if (target == null)
                        {
                            // ターゲットが存在しなかった場合生成
                            target = new Target { Path = targetPath };
                            context.Targets.Add(target);
                            context.SaveChanges();
                        }

                        {
                            Console.WriteLine("データベースの整合性を確認しています。");

                            // 迷子のFIがあるか
                            var ri = (from en in context.Items
                                      where !en.Paths.Any()
                                      select en).ToList();

                            ri.ForEach(t => t.Md5 = null,
                                () => Console.WriteLine("不明なファイルを削除しています。"),
                                () => context.SaveChanges());

                            // 最終SnapShotより新しいPathがあるか
                            var lastShot = (from en in context.Snapshots
                                            where en.Target.Id == target.Id
                                            orderby en.Creation descending
                                            select en).FirstOrDefault()?.Creation;

                            if (lastShot == null || (from en in context.Paths
                                                     where en.Target.Id == target.Id
                                                     where en.RemoveDate == null
                                                     where en.RegisterDate > lastShot.Value
                                                     select en).Any())
                            {
                                // 最終SnapShotより新しいPathがある場合、同じパスで削除日がnullのものが複数存在する可能性がある。
                                var notRemovedPaths = (from en in context.Paths
                                                       where en.Target.Id == target.Id
                                                       where en.RemoveDate == null
                                                       select en).Include(t => t.Item).ToList();

                                var errorPaths = notRemovedPaths
                                    .GroupBy(t => t.Path)
                                    .Where(t => t.Count() > 1)
                                    .Select(t => new { V = t.OrderBy(t2 => t2.RegisterDate).ToList() }).ToList();

                                errorPaths.ForEach(errorPath =>
                                {
                                    for (int i = 0; i < errorPath.V.Count - 1; i++)
                                    {
                                        errorPath.V[i].RemoveDate = errorPath.V[i + 1].RegisterDate;
                                    }
                                },
                                () => Console.WriteLine("パスデータを修復しています。"),
                                () => context.SaveChanges());
                            }
                        }

                        Console.WriteLine("データベースをロードしています。");

                        var tp = (from en in context.Paths
                                  where en.Target.Id == target.Id
                                  where en.RemoveDate == null
                                  where en.Parent == null
                                  select en).Include(t => t.Item).ToList();

                        _oldPathInfos = (from en in context.Paths
                                         where en.Target.Id == target.Id
                                         where en.RemoveDate == null
                                         select en).Include(t => t.Item).ToDictionary(t => t.Path);

                        _updatedPathInfos = new List<PathInfo>();

                        // チェック
                        context.Configuration.AutoDetectChangesEnabled = false;
                        _log.ItemCount--; // ルートディレクトリ分マイナス
                        Check(context, target, new DirectoryInfo(targetPath), null, "");
                        context.SaveChanges();
                        context.Configuration.AutoDetectChangesEnabled = true;

                        foreach (var notFoundPath in _oldPathInfos)
                        {
                            notFoundPath.Value.RemoveDate = _log.Start;
                            _log.LostItems.Add(notFoundPath.Value.Path);
                        }

                        foreach (var updatedPath in _updatedPathInfos)
                        {
                            updatedPath.RemoveDate = _log.Start;
                        }
                        context.SaveChanges();

                        var snapshot = new Snapshot { Creation = DateTime.Now, Target = target };
                        context.Snapshots.Add(snapshot);
                        shots.Add(snapshot);
                        context.SaveChanges();
                    }

                    {
                        var mss = new List<M.Snapshot>();
                        foreach (var shot in shots)
                        {
                            var ms = new M.Snapshot
                            {
                                Target = shot.Target.Path,
                                Creation = shot.Creation,
                            };
                            ms.Paths = (from en in context.Paths
                                        where en.Target.Id == shot.Target.Id
                                        where en.RemoveDate == null
                                        select en).ToList().Select(en => new M.PathInfo
                                        {
                                            Id = en.Item == null ? (int?)null : en.Item.Id,
                                            Path = en.Path,
                                            Creation = en.Creation,
                                            LastWrite = en.LastWrite,
                                        }).ToList();
                            mss.Add(ms);
                        }

                        var json = JsonConvert.SerializeObject(mss);
                        vaultLogPaths.Select(dd => Path.Combine(dd, $"{_log.Start:yyyyMMddHHmmss}.json")).ForEach(logn =>
                        {
                            File.WriteAllText(logn, json);
                        });
                    }

                    {
                        var tids = shots.Select(t => t.Target.Id).ToList();
                        _log.PairItems = (from en in context.Items
                                          let b = from en2 in en.Paths
                                                  where en2.RemoveDate == null
                                                  where tids.Contains(en2.Target.Id)
                                                  select en2.Path
                                          let count = b.Count()
                                          where count >= 2
                                          orderby count descending
                                          select new { Id = en.Id, Md5 = en.Md5, Size = en.Size, Paths = b.ToList() }).ToList().Select(en => new M.PairItem { Id = en.Id, Md5 = WriteRemoveAll(en.Md5), Size = en.Size, Paths = en.Paths }).ToList();

                        foreach (var item in _log.PairItems)
                        {
                            Console.WriteLine($"{item.Id} {item.Md5}");
                            foreach (var i2 in item.Paths)
                            {
                                Console.WriteLine("    " + i2);
                            }
                        }
                    }

                    {
                        var limit = _log.Start - _settings.Limit;

                        var rmshot = (from en in context.Snapshots
                                      where en.Creation < limit
                                      select en).ToList();
                        _log.DeleteSnapshotCount = rmshot.Count;
                        context.Snapshots.RemoveRange(rmshot);

                        var rmp = (from en in context.Paths
                                   where en.RemoveDate != null
                                   where en.RemoveDate < limit
                                   select en).ToList();
                        _log.DeletePathInfoCount = rmp.Count;
                        context.Paths.RemoveRange(rmp);

                        context.SaveChanges();

                        var ri = (from en in context.Items
                                  where !en.Paths.Any()
                                  select en).ToList();

                        _log.DeleteFileCount = ri.Count;
                        foreach (var item in ri)
                        {
                            foreach (var vp in _vaults)
                            {
                                var id = item.Id;
                                var dir = Path.Combine(vp, "Data", (id % 100).ToString("00"), (id % 10000).ToString("0000"));
                                Directory.CreateDirectory(dir);
                                var path = Path.Combine(dir, id.ToString());
                                File.Delete(path);
                            }
                        }

                        context.Items.RemoveRange(ri);
                        context.SaveChanges();
                    }

                    {
                        _log.StoredFileCount = context.Items.Count();
                        _log.StoredFileSize = context.Items.Sum(t => t.Size);
                    }

                    {
                        _log.Finish = DateTime.Now;

                        var json = JsonConvert.SerializeObject(_log, Formatting.Indented);
                        vaultLogPaths.Select(dd => Path.Combine(dd, $"{_log.Start:yyyyMMddHHmmss}_log.json")).ForEach(logn =>
                        {
                            File.WriteAllText(logn, json);
                        });
                    }

                    if (_settings.LogMailInfo != null && _settings.LogMailInfo.Port != 0)
                    {
                        var body = new StringBuilder();
                        if (_errSubVaults.Count != 0)
                        {
                            body.AppendLine("****************************************");
                            body.AppendLine("ERROR SubVaults");
                            foreach (var item in _errSubVaults)
                            {
                                body.AppendLine($"  {item}");
                            }
                            body.AppendLine("****************************************");
                        }
                        body.AppendLine($"Start: {_log.Start:yyyy/MM/dd HH:mm:ss.ff}");
                        body.AppendLine($"Elapsed: {_log.Elapsed}");
                        body.AppendLine("━━━━━━━━━━━━━━━━━━━━");
                        body.AppendLine($"New: {_log.NewItemCount:#,##0}");
                        body.AppendLine($"Updated: {_log.UpdatedItemCount:#,##0}");
                        body.AppendLine($"Same: {_log.SkipItemCount:#,##0}");
                        body.AppendLine($"Lost: {_log.LostItemCount:#,##0}");
                        body.AppendLine($"*Count: {_log.ItemCount:#,##0} ({_log.FileCount:#,##0})");
                        body.AppendLine($"Delete: S{_log.DeleteSnapshotCount:#,##0} P{_log.DeletePathInfoCount:#,##0} F{_log.DeleteFileCount:#,##0}");
                        body.AppendLine($"Pairs: {_log.PairItems.Count:#,##0}");
                        body.AppendLine($"StoredSize: {Utils.CalculateUnit(_log.StoredFileSize, "{0:0.00}{1}")} ({_log.StoredFileSize:#,##0}B)");
                        body.AppendLine($"StoredCount: {_log.StoredFileCount:#,##0}Files");
                        body.AppendLine("━━━━━━━━━━━━━━━━━━━━");
                        body.AppendLine($"New:");
                        foreach (var item in _log.NewItems)
                        {
                            body.AppendLine($"    {item}");
                        }
                        body.AppendLine("━━━━━━━━━━━━━━━━━━━━");
                        body.AppendLine($"Updated:");
                        foreach (var item in _log.UpdatedItems)
                        {
                            body.AppendLine($"    {item}");
                        }
                        body.AppendLine("━━━━━━━━━━━━━━━━━━━━");
                        body.AppendLine($"Lost:");
                        foreach (var item in _log.LostItems)
                        {
                            body.AppendLine($"    {item}");
                        }
                        body.AppendLine("━━━━━━━━━━━━━━━━━━━━");
                        body.AppendLine($"Target: {_settings.TargetPaths.Count:#,##0}");
                        foreach (var item in _settings.TargetPaths)
                        {
                            body.AppendLine($"    {item}");
                        }
                        body.AppendLine($"Vaults: {_vaults.Count:#,##0}");
                        foreach (var item in _vaults)
                        {
                            body.AppendLine($"    {item}");
                        }

                        var mi = _settings.LogMailInfo;
                        Mail.Send(mi.FromAddress, "Sheltie Services", mi.ToAddresses, mi.Host, mi.Port, mi.Password, mi.EnableSsl, $"{mi.Name} {_log.NewItemCount},{_log.UpdatedItemCount},{_log.SkipItemCount},{_log.LostItemCount} {_log.Start:yyyy/MM/dd HH:mm}", body.ToString()).Wait();
                    }
                }
            }
        }

        private static byte[] CalculateMd5(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                return _md5.ComputeHash(stream);
            }
        }

        private static void Check(BackupContext context, Target target, DirectoryInfo dir, PathInfo parent, string ap)
        {
            Console.WriteLine($"{++_log.ItemCount} D        {ap}");

            foreach (var info in dir.GetDirectories())
            {
                try
                {
                    var name = info.Name;
                    var bp = Path.Combine(ap, name);
                    var pc = info.CreationTimeUtc.ToBinary();
                    var pm = info.LastWriteTimeUtc.ToBinary();
                    var key = (parent?.Id ?? 0) + "\\" + name;

                    PathInfo pi;
                    if (_oldPathInfos.TryGetValue(key, out pi) && pi.Item == null)
                    {
                        _oldPathInfos.Remove(key);

                        if (pi.Creation == pc && pi.LastWrite == pm)
                        {
                            // 完全に同一
                            _log.SameItemCount++;
                        }
                        else
                        {
                            // update
                            _updatedPathInfos.Add(pi);

                            pi = new PathInfo
                            {
                                Parent = parent,
                                Name = name,
                                Target = target,
                                Creation = pc,
                                LastWrite = pm,
                                RegisterDate = _log.Start
                            };
                            context.Paths.Add(pi);
                            context.SaveChanges();
                            _log.UpdatedItems.Add(bp);
                        }
                    }
                    else
                    {
                        // 新しい
                        pi = new PathInfo
                        {
                            Parent = parent,
                            Name = name,
                            Target = target,
                            Creation = pc,
                            LastWrite = pm,
                            RegisterDate = _log.Start
                        };
                        context.Paths.Add(pi);
                        context.SaveChanges();
                        _log.NewItems.Add(bp);
                    }

                    Check(context, target, info, pi, bp);
                }
                catch (Exception exp)
                {
                    Console.WriteLine(exp);
                    _log.Exceptions.Add(exp.ToString());
                }
            }

            foreach (var info in dir.GetFiles())
            {
                try
                {
                    _log.FileCount++;

                    var name = info.Name;
                    var bp = Path.Combine(ap, name);
                    var pc = info.CreationTimeUtc.ToBinary();
                    var pm = info.LastWriteTimeUtc.ToBinary();
                    var ps = info.Length;
                    var key = (parent?.Id ?? 0) + "\\" + name;

                    PathInfo pi;
                    var existsList = false;
                    if (_oldPathInfos.TryGetValue(key, out pi) && pi.Item != null)
                    {
                        _oldPathInfos.Remove(key);

                        if (pi.Creation == pc && pi.LastWrite == pm && pi.Item.Size == ps)
                        {
                            // 完全に同一
                            Console.WriteLine($"{++_log.ItemCount} Z {WriteRemove(pi.Item.Md5)} {bp}");
                            _log.SameItemCount++;
                            continue;
                        }

                        _updatedPathInfos.Add(pi);
                        existsList = true;
                    }

                    var md5 = CalculateMd5(info.FullName);

                    context.SaveChanges();
                    var ii = (from en in context.Items
                              where en.Md5 == md5
                              where en.Size == ps
                              select en).FirstOrDefault();

                    if (ii == null)
                    {
                        ii = new ItemInfo { Md5 = md5, Size = ps };
                        context.Items.Add(ii);
                        context.SaveChanges();
                        Copy(info.FullName, ii.Id);
                        Console.WriteLine($"{++_log.ItemCount} C {WriteRemove(ii.Md5)} {bp}");
                        if (existsList) _log.UpdatedItems.Add(bp);
                        else _log.NewItems.Add(bp);
                    }
                    else
                    {
                        Console.WriteLine($"{++_log.ItemCount} S {WriteRemove(ii.Md5)} {bp}");
                        _log.SkipItems.Add(bp);
                    }

                    var pd = new PathInfo
                    {
                        Parent = parent,
                        Name = name,
                        Target = target,
                        Item = ii,
                        Creation = pc,
                        LastWrite = pm,
                        RegisterDate = _log.Start,
                    };
                    context.Paths.Add(pd);
                }
                catch (Exception exp)
                {
                    Console.WriteLine($"E {info.FullName}");
                    Console.WriteLine(exp);
                    _log.Exceptions.Add(exp.ToString());
                }
            }
        }

        private static string WriteRemove(byte[] md5)
        {
            return md5[0].ToString("x2") + md5[1].ToString("x2") + md5[2].ToString("x2");
        }

        private static string WriteRemoveAll(byte[] md5)
        {
            return string.Join("", md5.Select(t => t.ToString("x2")));
        }

        private static void Copy(string target, int id)
        {
            foreach (var vp in _vaults)
            {
                var dir = Path.Combine(vp, "Data", (id % 100).ToString("00"), (id % 10000).ToString("0000"));
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, id.ToString());
                File.Copy(target, path);
            }
        }

        #region initialize

        private static bool CheckParamater(string path)
        {
            if (path == null || !File.Exists(path))
            {
                Console.WriteLine("設定ファイルが存在しません。param.jsonファイルを変更してください。");
                if (!File.Exists("param.json"))
                {
                    _settings = new M.StartInfo
                    {
                        Limit = TimeSpan.FromDays(365),
                        TargetPaths = new List<string> { "Target" },
                        VaultPath = "Vault",
                        SubVaultPaths = new List<string>(),
                        LogMailInfo = new M.LogMailInfo
                        {
                            Name = "Backupped",
                            FromAddress = "from@address",
                            Password = "password",
                            ToAddresses = new List<string> { "to@address" },
                            Host = "host",
                            Port = 0,
                            EnableSsl = true,
                        },
                    };
                    var json = JsonConvert.SerializeObject(_settings, Formatting.Indented);
                    File.WriteAllText("param.json", json);
                }
                Console.ReadLine();
                return false;
            }

            try
            {
                var json = File.ReadAllText(path);
                _settings = JsonConvert.DeserializeObject<M.StartInfo>(json);
                if (_settings == null) throw new Exception();
            }
            catch
            {
                Console.WriteLine("指定した設定ファイルの内容が不正です。");
                Console.ReadLine();
                return false;
            }


            if ((_settings.TargetPaths?.Count ?? 0) == 0)
            {
                Console.WriteLine($"ターゲットが空です。");
                Console.ReadLine();
                return false;
            }

            foreach (var target in _settings.TargetPaths)
            {
                if (!Directory.Exists(target))
                {
                    Console.WriteLine($"{target} は存在しません。");
                    Console.ReadLine();
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(_settings.VaultPath))
            {
                Console.WriteLine($"保存場所は存在しません。");
                Console.ReadLine();
                return false;
            }

            if (!Directory.Exists(_settings.VaultPath))
            {
                Console.WriteLine($"保存場所 {_settings.VaultPath} は存在しません。");
                Console.ReadLine();
                return false;
            }

            _vaults.Add(_settings.VaultPath);

            if (_settings.SubVaultPaths != null)
            {
                foreach (var svp in _settings.SubVaultPaths)
                {
                    if (Directory.Exists(svp))
                    {
                        _vaults.Add(svp);
                    }
                    else
                    {
                        Console.WriteLine($"保存場所 {svp} は存在しません。");
                        _errSubVaults.Add(svp);
                    }
                }
            }

            return true;
        }

        #endregion

        private static IEnumerable<IEnumerable<KeyValuePair<string, FileInfo>>> Sample(IEnumerable<KeyValuePair<string, FileInfo>> src, int max)
        {
            var e = src.GetEnumerator();
            while (e.MoveNext())
            {
                yield return InSample(e, max);
            }
        }

        private static IEnumerable<KeyValuePair<string, FileInfo>> InSample(IEnumerator<KeyValuePair<string, FileInfo>> e, int max)
        {
            int count = 0;
            do
            {
                yield return e.Current;
                count++;
                if (count >= max) break;
            }
            while (e.MoveNext());
        }
    }

    static class Extensions
    {
        public static void ForEach<T>(this IEnumerable<T> src, Action<T> action)
        {
            foreach (var item in src) action(item);
        }

        public static void ForEach<T>(this IEnumerable<T> src, Action<T> action, Action start, Action final)
        {
            var e = src.GetEnumerator();

            if (!e.MoveNext()) return;

            start?.Invoke();

            action(e.Current);

            while (e.MoveNext()) action(e.Current);

            final?.Invoke();
        }
    }
}
