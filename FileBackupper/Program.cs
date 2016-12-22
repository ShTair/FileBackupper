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

        private static M.LogItem _log = new M.LogItem
        {
            Exceptions = new List<string>(),
            NewItems = new List<string>(),
            UpdatedItems = new List<string>(),
            SkipItems = new List<string>(),
            LostItems = new List<string>(),
        };

        private static Dictionary<string, PathInfo> _tempInfo;
        private static List<PathInfo> _udInfo;

        private static string _logPath;

        static void Main(string[] args)
        {
            if (CheckParamater(args.Length > 0 ? args[0] : null))
            {
                _log.Start = DateTime.Now;
                _md5 = MD5.Create();

                var dds = _vaults.Select(vp => Path.Combine(vp, "Log")).ToList();
                dds.ForEach(dd => Directory.CreateDirectory(dd));

                var dbn = Path.Combine(_logPath = dds[0], "db.sdf");
                using (var context = BackupContext.Create(dbn))
                {
                    {
                        // 野良ターゲットの削除
                        context.Targets.RemoveRange(from en in context.Targets
                                                    where !en.Snapshots.Any()
                                                    select en);
                        context.SaveChanges();
                    }

                    var shots = new List<Snapshot>();
                    foreach (var target in _settings.TargetPaths)
                    {
                        var tp = (from en in context.Targets
                                  where en.Path == target
                                  select en).FirstOrDefault();

                        if (tp == null)
                        {
                            tp = new Target { Path = target };
                            context.Targets.Add(tp);

                            context.Configuration.AutoDetectChangesEnabled = false;
                            if (!FirstStore(context, tp, new DirectoryInfo(target))) return;
                            context.Configuration.AutoDetectChangesEnabled = true;
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

                            // 最終Snapより新しいPathがあるか
                            var lasts = (from en in context.Snapshots
                                         where en.Target.Id == tp.Id
                                         orderby en.Creation descending
                                         select en).FirstOrDefault()?.Creation;

                            if (lasts == null || (from en in context.Paths
                                                  where en.Target.Id == tp.Id
                                                  where en.RemoveDate == null
                                                  where en.RegisterDate > lasts.Value
                                                  select en).Any())
                            {
                                var tv1 = (from en in context.Paths
                                           where en.Target.Id == tp.Id
                                           where en.RemoveDate == null
                                           select en).Include(t => t.Item).ToList();
                                var tv2 = tv1.GroupBy(t => t.Path)
                                    .Where(t => t.Count() > 1)
                                    .Select(t => new { V = t.OrderBy(t2 => t2.RegisterDate).ToList() }).ToList();
                                tv2.ForEach(tv3 =>
                                {
                                    for (int i = 0; i < tv3.V.Count - 1; i++)
                                    {
                                        tv3.V[i].RemoveDate = tv3.V[i + 1].RegisterDate;
                                    }
                                },
                                () => Console.WriteLine("パスデータを修復しています。"),
                                () => context.SaveChanges());
                            }
                        }

                        Console.WriteLine("データベースをロードしています。");
                        _tempInfo = (from en in context.Paths
                                     where en.Target.Id == tp.Id
                                     where en.RemoveDate == null
                                     select en).Include(t => t.Item).ToDictionary(t => t.Path);

                        _udInfo = new List<PathInfo>();

                        context.Configuration.AutoDetectChangesEnabled = false;
                        _log.ItemCount--; // ルートディレクトリ分マイナス
                        Check(context, tp, new DirectoryInfo(target), "");
                        context.SaveChanges();
                        context.Configuration.AutoDetectChangesEnabled = true;

                        foreach (var ti in _tempInfo)
                        {
                            ti.Value.RemoveDate = _log.Start;
                            _log.LostItems.Add(ti.Value.Path);
                        }
                        foreach (var ti in _udInfo)
                        {
                            ti.RemoveDate = _log.Start;
                        }
                        context.SaveChanges();

                        var snapshot = new Snapshot { Creation = DateTime.Now, Target = tp };
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
                                        select new M.PathInfo
                                        {
                                            Id = en.Item == null ? (int?)null : en.Item.Id,
                                            Path = en.Path,
                                            Creation = en.Creation,
                                            LastWrite = en.LastWrite,
                                        }).ToList();
                            mss.Add(ms);
                        }

                        var json = JsonConvert.SerializeObject(mss);
                        dds.Select(dd => Path.Combine(dd, $"{_log.Start:yyyyMMddHHmmss}.json")).ForEach(logn =>
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
                        dds.Select(dd => Path.Combine(dd, $"{_log.Start:yyyyMMddHHmmss}_log.json")).ForEach(logn =>
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

        private static void Check(BackupContext context, Target target, DirectoryInfo pdir, string bpath)
        {
            Console.WriteLine($"{++_log.ItemCount} D        {bpath}");
            foreach (var dir in pdir.GetDirectories())
            {
                var name = dir.Name;
                var bp = Path.Combine(bpath, name);
                try
                {
                    var pc = dir.CreationTimeUtc.ToBinary();
                    var pm = dir.LastWriteTimeUtc.ToBinary();

                    PathInfo tpi;
                    if (_tempInfo.TryGetValue(bp, out tpi) && tpi.Item == null)
                    {
                        _tempInfo.Remove(bp);
                        if (tpi.Creation == pc && tpi.LastWrite == pm)
                        {
                            // 完全に同一
                            _log.SameItemCount++;
                        }
                        else
                        {
                            _udInfo.Add(tpi);
                            // 更新（日付だけ違う）
                            var pd = new PathInfo { Path = bp, Target = target, Creation = pc, LastWrite = pm, RegisterDate = _log.Start };
                            context.Paths.Add(pd);
                            _log.UpdatedItems.Add(bp);
                        }
                    }
                    else
                    {
                        // 新しい
                        var pd = new PathInfo { Path = bp, Target = target, Creation = pc, LastWrite = pm, RegisterDate = _log.Start };
                        context.Paths.Add(pd);
                        _log.NewItems.Add(bp);
                    }
                    Check(context, target, dir, bp);
                }
                catch (Exception exp)
                {
                    Console.WriteLine(exp);
                    _log.Exceptions.Add(exp.ToString());
                }
            }

            foreach (var file in pdir.GetFiles())
            {
                try
                {
                    _log.FileCount++;

                    var name = file.Name;
                    var bp = Path.Combine(bpath, name);

                    var pc = file.CreationTimeUtc.ToBinary();
                    var pm = file.LastWriteTimeUtc.ToBinary();
                    var ps = file.Length;


                    PathInfo tpi;
                    var existsList = false;
                    if (_tempInfo.TryGetValue(bp, out tpi) && tpi.Item != null)
                    {
                        _tempInfo.Remove(bp);
                        if (tpi.Creation == pc && tpi.LastWrite == pm && tpi.Item.Size == ps)
                        {
                            Console.WriteLine($"{++_log.ItemCount} Z {WriteRemove(tpi.Item.Md5)} {bp}");
                            _log.SameItemCount++;
                            continue;
                        }

                        _udInfo.Add(tpi);
                        existsList = true;
                    }

                    var md5 = CalculateMd5(file.FullName);
                    var size = file.Length;

                    context.SaveChanges();
                    var ii = (from en in context.Items
                              where en.Md5 == md5
                              where en.Size == size
                              select en).FirstOrDefault();

                    if (ii == null)
                    {
                        ii = new ItemInfo { Md5 = md5, Size = size };
                        context.Items.Add(ii);
                        context.SaveChanges();
                        Copy(file.FullName, ii.Id);
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
                        Path = bp,
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
                    Console.WriteLine($"E {file}");
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

        private static bool FirstStore(BackupContext context, Target target, DirectoryInfo pdir)
        {
            Console.WriteLine("新しいターゲットです。初期格納を実行します。");
            var tfp = Path.Combine(_logPath, "Temp");
            int count = 0;

            foreach (var item1 in Sample(GetFiles(pdir, ""), 10000))
            {
                if (Directory.Exists(tfp))
                {
                    Console.WriteLine("一時格納フォルダが存在します。古い一時格納フォルダの名前を変更します。");
                    var cdt = Directory.GetCreationTime(tfp);
                    Directory.Move(tfp, Path.Combine(_logPath, $"Temp_{cdt:yyyyMMddHHmmss}"));
                }

                var tps = new List<M.TempPath>();

                foreach (var item in item1)
                {
                    try
                    {
                        Console.WriteLine($"{count} {item.Value.FullName}");
                        var dir = Path.Combine(tfp, (count % 100).ToString("00"), (count % 10000).ToString("0000"));
                        Directory.CreateDirectory(dir);
                        var path = Path.Combine(dir, count.ToString());
                        item.Value.CopyTo(path);
                        tps.Add(new M.TempPath
                        {
                            Id = count,
                            OPath = item.Value.FullName,
                            VPath = path,
                            BPath = item.Key,
                            File = item.Value,
                        });

                        count++;
                    }
                    catch { }
                }

                foreach (var tp in tps)
                {
                    tp.Md5 = CalculateMd5(tp.VPath);
                    tp.Md5Str = WriteRemoveAll(tp.Md5);
                    Console.WriteLine($"{tp.Id} {tp.Md5Str}");
                }

                Console.WriteLine("ファイルデータを生成しています。");
                var iis = context.Items.Where(t => t.Md5 != null).AsEnumerable().ToDictionary(t =>
                {
                    Console.WriteLine($"{t.Md5} {t.Paths}");
                    return WriteRemoveAll(t.Md5);
                });
                var ps = tps.GroupBy(t => t.Md5Str).ToDictionary(t => t.Key, t => t.ToList());

                var niis = new List<Tuple<M.TempPath, ItemInfo>>();

                foreach (var g in ps)
                {
                    ItemInfo ii;
                    if (!iis.TryGetValue(g.Key, out ii))
                    {
                        context.Items.Add(ii = new ItemInfo { Md5 = g.Value[0].Md5, Size = g.Value[0].File.Length });
                        niis.Add(new Tuple<M.TempPath, ItemInfo>(g.Value[0], ii));
                    }

                    g.Value.ForEach(t => t.Item = ii);
                }

                Console.WriteLine("データベースに書き込んでいます。（1/2）");
                context.SaveChanges();

                {
                    Console.WriteLine($"ファイルを格納しています。");
                    var ve = _vaults.GetEnumerator();
                    ve.MoveNext();
                    var ve0 = ve.Current;
                    Console.WriteLine();
                    var icount = 0;
                    foreach (var ii in niis)
                    {
                        Console.CursorTop--;
                        Console.WriteLine($"{++icount}/{niis.Count}");
                        var dir = Path.Combine(ve.Current, "Data", (ii.Item2.Id % 100).ToString("00"), (ii.Item2.Id % 10000).ToString("0000"));
                        Directory.CreateDirectory(dir);
                        var path = Path.Combine(dir, ii.Item2.Id.ToString());
                        File.Move(ii.Item1.VPath, path);
                    }

                    while (ve.MoveNext())
                    {
                        Console.WriteLine($"ファイルをサブ領域に格納しています。");
                        Console.WriteLine($"{ve.Current}");
                        Console.WriteLine();
                        icount = 0;
                        foreach (var ii in niis)
                        {
                            Console.CursorTop--;
                            Console.WriteLine($"{++icount}/{niis.Count}");
                            var dir = Path.Combine(ve.Current, "Data", (ii.Item2.Id % 100).ToString("00"), (ii.Item2.Id % 10000).ToString("0000"));
                            Directory.CreateDirectory(dir);
                            var path = Path.Combine(dir, ii.Item2.Id.ToString());

                            var ve0p = Path.Combine(ve0, "Data", (ii.Item2.Id % 100).ToString("00"), (ii.Item2.Id % 10000).ToString("0000"), ii.Item2.Id.ToString());

                            File.Copy(ve0p, path);
                        }
                    }
                }

                Console.WriteLine("パスデータを生成しています。");
                foreach (var g in ps)
                {
                    foreach (var p in g.Value)
                    {
                        context.Paths.Add(new PathInfo
                        {
                            Path = p.BPath,
                            Target = target,
                            Item = p.Item,
                            Creation = p.File.CreationTimeUtc.ToBinary(),
                            LastWrite = p.File.LastWriteTimeUtc.ToBinary(),
                            RegisterDate = _log.Start,
                        });
                    }
                }
                Console.WriteLine("データベースに書き込んでいます。（2/2）");
                context.SaveChanges();

                Console.WriteLine("一時ファイルを削除しています。");
                try
                {
                    Directory.Delete(tfp, true);
                }
                catch { }
            }

            return true;
        }

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

        private static void CheckValutFiles(BackupContext context)
        {
            // check
        }

        #region utils

        private static IEnumerable<KeyValuePair<string, FileInfo>> GetFiles(DirectoryInfo pdir, string bpath)
        {
            foreach (var dir in pdir.GetDirectories())
            {
                var name = dir.Name;
                var bp = Path.Combine(bpath, name);

                foreach (var file in GetFiles(dir, bp))
                {
                    yield return file;
                }
            }

            foreach (var file in pdir.GetFiles())
            {
                var name = file.Name;
                var bp = Path.Combine(bpath, name);

                yield return new KeyValuePair<string, FileInfo>(bp, file);
            }
        }

        #endregion
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
