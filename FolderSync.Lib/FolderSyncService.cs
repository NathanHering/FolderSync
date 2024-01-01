using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace FolderSync.Lib;
public class FolderSyncService
{
   /// <summary>
   /// Gets or sets the local time at which the sync process
   /// should not begin new processes after. Processess started
   /// before the cutoff time will continue until finished.
   /// </summary>
   public DateTime CutoffTime { get; set; }

   private string DbName { get; set; } = string.Empty;
   private List<SyncPlan> SyncPlans = new();
   private readonly List<string> IgnoreDirectories = new() { "found.000", "$RECYCLE.BIN", "System Volume Information" };
   private readonly string SourceRoot = string.Empty;
   private readonly string BackupRoot = string.Empty;

   public FolderSyncService(string sourceRoot, string backupRoot)
   {
      InitDb();
      SourceRoot = sourceRoot;
      BackupRoot = backupRoot;
   }

   private void InitDb()
   {
      DbName = $"FolderSync_{DateTime.Now:yyyy-MM-dd-HH-mm}.db";
      FolderSyncDbContext db = new(DbName);
      db.Database.EnsureCreated();
   }

   #region Create Sync Plan

   public async Task CreateSyncPlanAsync()
   {
      RecurseSourceDirectory(SourceRoot);
      RecurseBackupDirectory(BackupRoot);
      await SaveSyncPlansAsync();
   }

   private void RecurseSourceDirectory(string directoryPath)
   {
      string? dir = GetDirectoryName(directoryPath);
      if (!string.IsNullOrEmpty(dir) && IgnoreDirectories.Contains(dir)) return;

      GetCreateDirectoryPlan(directoryPath);

      var filePaths = Directory.EnumerateFiles(directoryPath);
      foreach (string filePath in filePaths)
      {
         GetCopyOrUpdateFilePlan(filePath);
      }

      var directories = Directory.EnumerateDirectories(directoryPath);
      foreach (string directory in directories)
      {
         RecurseSourceDirectory(directory);
      }
   }

   private void GetCreateDirectoryPlan(string directoryPath)
   {
      var name = GetDirectoryName(directoryPath);
      if (!string.IsNullOrEmpty(name) && !IgnoreDirectories.Contains(name))
      {
         string equivalentPath = directoryPath.Replace(SourceRoot, BackupRoot);
         if (!Directory.Exists(equivalentPath))
         {
            SyncPlan sp = new()
            {
               Action = SyncPlan.Actions.CREATE,
               Type = RecordTypes.DIRECTORY,
               DirectoryPath = equivalentPath
            };
            SyncPlans.Add(sp);
         }
      }
   }

   private void GetCopyOrUpdateFilePlan(string filePath)
   {
      string equivalentPath = filePath.Replace(SourceRoot, BackupRoot);
      if (File.Exists(equivalentPath))
      {
         FileInfo sourceInfo = new(filePath);
         FileInfo backupInfo = new(equivalentPath);
         if (sourceInfo.LastWriteTime != backupInfo.LastWriteTime)
         {
            SyncPlan sp = new()
            {
               Action = SyncPlan.Actions.UPDATE,
               Type = RecordTypes.FILE,
               FromFilePath = filePath,
               ToFilePath = equivalentPath
            };
            SyncPlans.Add(sp);
         }
      }
      else
      {
         SyncPlan sp = new()
         {
            Action = SyncPlan.Actions.COPY,
            Type = RecordTypes.FILE,
            FromFilePath = filePath,
            ToFilePath = equivalentPath
         };
         SyncPlans.Add(sp);
      }
   }

   private void RecurseBackupDirectory(string directoryPath)
   {
      string? dir = GetDirectoryName(directoryPath);
      if (!string.IsNullOrEmpty(dir) && IgnoreDirectories.Contains(dir)) return;

      GetDeleteDirectoryPlan(directoryPath);

      var filePaths = Directory.EnumerateFiles(directoryPath);
      foreach (string filePath in filePaths)
      {
         GetDeleteFilePlan(filePath);
      }

      var directories = Directory.EnumerateDirectories(directoryPath);
      foreach (string directory in directories)
      {
         RecurseBackupDirectory(directory);
      }
   }

   private void GetDeleteDirectoryPlan(string directoryPath)
   {
      var name = GetDirectoryName(directoryPath);
      if (!string.IsNullOrEmpty(name) && !IgnoreDirectories.Contains(name))
      {
         string equivalentPath = directoryPath.Replace(BackupRoot, SourceRoot);
         if (!Directory.Exists(equivalentPath))
         {
            SyncPlan sp = new()
            {
               Action = SyncPlan.Actions.DELETE,
               Type = RecordTypes.DIRECTORY,
               DirectoryPath = directoryPath
            };
            SyncPlans.Add(sp);
         }
      }
   }

   private static string? GetDirectoryName(string directoryPath)
   {
      if (string.IsNullOrEmpty(directoryPath)) return null;

      string? result = null;
      var nodes = directoryPath.Split('\\');
      if (nodes.Length > 1) result = nodes[^1];
      return result;
   }

   private void GetDeleteFilePlan(string filePath)
   {
      string equivalentPath = filePath.Replace(BackupRoot, SourceRoot);
      if (!File.Exists(equivalentPath))
      {
         SyncPlan sp = new()
         {
            Action = SyncPlan.Actions.DELETE,
            Type = RecordTypes.FILE,
            FromFilePath = filePath
         };
         SyncPlans.Add(sp);
      }
   }

   private async Task SaveSyncPlansAsync()
   {
      Console.WriteLine("Saving Sync Plans.");
      using FolderSyncDbContext db = new(DbName);
      await db.SyncPlan.AddRangeAsync(SyncPlans);
      await db.SaveChangesAsync();
   }

   #endregion

   #region Sync Folders

   public async Task SyncFoldersAsync()
   {
      await LoadSyncPlansAsync();
      DeleteDirectories();
      CreateDirectories();
      DeleteFiles();
      await UpdateFilesAsync();
      await CopyFilesAsync();
      await UpdateSyncPlansAsync();
   }

   private async Task LoadSyncPlansAsync()
   {
      SyncPlans.Clear();
      using FolderSyncDbContext db = new(DbName);
      SyncPlans = await db.SyncPlan.ToListAsync();
   }

   private void DeleteDirectories()
   {
      if (!KeepGoing) return;

      IEnumerable<SyncPlan> toDelete = SyncPlans.Where(sp => sp.Type == SyncPlan.Types.DIRECTORY && sp.Action == SyncPlan.Actions.DELETE);
      Console.WriteLine($"Deleting {toDelete.Count()} directories from backup.");
      foreach (SyncPlan dir in toDelete)
      {
         try
         {
            if (KeepGoing && Directory.Exists(dir.DirectoryPath))
            {
               Directory.Delete(dir.DirectoryPath, true);
               dir.IsSuccess = true;
            }
         }
         catch (Exception e)
         {
            dir.IsSuccess = false;
            dir.Message = e.ToString();
         }
      }
   }

   private void CreateDirectories()
   {
      if (!KeepGoing) return;

      IEnumerable<SyncPlan> toCreate = SyncPlans.Where(sp => sp.Type == SyncPlan.Types.DIRECTORY && sp.Action == SyncPlan.Actions.CREATE);
      Console.WriteLine($"Creating {toCreate.Count()} directories in backup.");
      foreach (SyncPlan dir in toCreate)
      {
         if (KeepGoing && !Directory.Exists(dir.DirectoryPath))
         {
            try
            {
               Directory.CreateDirectory(dir.DirectoryPath);
               dir.IsSuccess = true;
            }
            catch (Exception e)
            {
               dir.IsSuccess = false;
               dir.Message = e.ToString();
            }
         }
      }
   }

   private void DeleteFiles()
   {
      if (!KeepGoing) return;

      IEnumerable<SyncPlan> toDelete = SyncPlans.Where(sp => sp.Type == SyncPlan.Types.FILE && sp.Action == SyncPlan.Actions.DELETE);
      Console.WriteLine($"Deleting {toDelete.Count()} files in backup.");
      foreach (SyncPlan file in toDelete)
      {
         if (KeepGoing && File.Exists(file.FromFilePath))
         {
            try
            {
               File.Delete(file.FromFilePath);
               file.IsSuccess = true;
            }
            catch (Exception e)
            {
               file.IsSuccess = false;
               file.Message = e.ToString();
            }
         }
      }
   }

   private async Task UpdateFilesAsync()
   {
      if (!KeepGoing) return;

      IEnumerable<SyncPlan> toUpdate = SyncPlans.Where(sp => sp.Type == SyncPlan.Types.FILE && sp.Action == SyncPlan.Actions.UPDATE);
      Console.WriteLine($"Updating {toUpdate.Count()} files in backup.");
      foreach (SyncPlan file in toUpdate)
      {
         if (KeepGoing && File.Exists(file.FromFilePath) && File.Exists(file.ToFilePath))
         {
            try
            {
               Console.WriteLine($"Updating file: {file.ToFilePath}");
               File.Delete(file.FromFilePath);
               await CopyFileAsync(file.FromFilePath, file.ToFilePath);
               file.IsSuccess = true;
            }
            catch (Exception e)
            {
               file.IsSuccess = false;
               file.Message = e.ToString();
            }
         }
      }
   }

   private async Task CopyFilesAsync()
   {
      if (!KeepGoing) return;

      IEnumerable<SyncPlan> toCopy = SyncPlans.Where(sp => sp.Type == SyncPlan.Types.FILE && sp.Action == SyncPlan.Actions.COPY);
      Console.WriteLine($"Copying {toCopy.Count()} files to backup.");
      foreach (SyncPlan file in toCopy)
      {
         if (KeepGoing && File.Exists(file.FromFilePath) && !File.Exists(file.ToFilePath))
         {
            try
            {
               Console.WriteLine($"Copying file: {file.FromFilePath}");
               await CopyFileAsync(file.FromFilePath, file.ToFilePath);
               file.IsSuccess = true;
            }
            catch (Exception e)
            {
               file.IsSuccess = false;
               file.Message = e.ToString();
            }
         }
      }
   }

   private static async Task CopyFileAsync(string sourcePath, string backupPath)
   {
      using (FileStream sourceStream = File.Open(sourcePath, FileMode.Open))
      {
         using (FileStream destinationStream = File.Create(backupPath))
         {
            await sourceStream.CopyToAsync(destinationStream);
         }
      }
      FileInfo info = new(sourcePath);
      File.SetLastWriteTime(backupPath, info.LastWriteTime);
   }

   private bool KeepGoing
   {
      get
      {
         return CutoffTime > DateTime.Now;
      }
   }

   private async Task UpdateSyncPlansAsync()
   {
      Console.WriteLine("Updating Sync Plans.");
      using FolderSyncDbContext db = new(DbName);
      db.SyncPlan.UpdateRange(SyncPlans);
      await db.SaveChangesAsync();
   }

   #endregion
}
