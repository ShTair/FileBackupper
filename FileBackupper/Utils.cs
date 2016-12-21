using System;

namespace FileBackupper
{
    static class Utils
    {
        public static string CalculateUnit(long v, string format)
        {
            var units = new[] { "B", "KB", "MB", "GB", "TB", "PB", "EB" };
            int i = 0;
            for (; i < units.Length - 1 && (v >> ((i + 1) * 10)) > 0; i++) ;
            var d = v / Math.Pow(1024, i);
            return string.Format(format, d, units[i]);
        }
    }
}
