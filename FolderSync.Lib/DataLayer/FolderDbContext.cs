using System.IO;
using Microsoft.EntityFrameworkCore;

namespace FolderSync.Lib;

public class FolderSyncDbContext : DbContext
{
   private string? FileName { get; set; }

   public DbSet<SyncPlan> SyncPlan { get; set; }

   public FolderSyncDbContext(string fileName)
   {
      FileName = fileName;
      SyncPlan = Set<SyncPlan>();
   }

   protected override void OnConfiguring(DbContextOptionsBuilder options)
   {
      options.UseSqlite($"FileName={FileName}");
   }
}