using System.Data.Entity;
using System.Data.SqlServerCe;

namespace FileBackupper.Db
{
    class BackupContext : DbContext
    {
        static BackupContext()
        {
            Database.SetInitializer(new DropCreateDatabaseIfModelChanges<BackupContext>());
        }

        public BackupContext(string cs) : base(cs) { }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Snapshot>().HasRequired(t => t.Target).WithMany(t => t.Snapshots);
            modelBuilder.Entity<PathInfo>().HasRequired(t => t.Target).WithMany(t => t.Paths);
            modelBuilder.Entity<PathInfo>().HasOptional(t => t.Item).WithMany(t => t.Paths);
            modelBuilder.Entity<PathInfo>().HasOptional(t => t.Directory).WithMany(t => t.Children);
        }

        public DbSet<Target> Targets { get; set; }

        public DbSet<Snapshot> Snapshots { get; set; }

        public DbSet<PathInfo> Paths { get; set; }

        public DbSet<ItemInfo> Items { get; set; }

        public static BackupContext Create(string path)
        {
            var sb = new SqlCeConnectionStringBuilder();
            sb.DataSource = path;
            return new BackupContext(sb.ConnectionString);
        }
    }
}
