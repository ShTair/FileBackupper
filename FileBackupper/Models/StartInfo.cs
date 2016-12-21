using System;
using System.Collections.Generic;

namespace FileBackupper.Models
{
    class StartInfo
    {
        public TimeSpan Limit { get; set; }

        public IList<string> TargetPaths { get; set; }

        public string VaultPath { get; set; }

        public IList<string> SubVaultPaths { get; set; }

        public LogMailInfo LogMailInfo { get; set; }
    }

    class LogMailInfo
    {
        public string Name { get; set; }

        public string FromAddress { get; set; }

        public string Password { get; set; }

        public IList<string> ToAddresses { get; set; }

        public string Host { get; set; }

        public int Port { get; set; }

        public bool EnableSsl { get; set; }
    }
}
