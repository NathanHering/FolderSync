using System.ComponentModel.DataAnnotations;

namespace FolderSync.Lib;

public class SyncPlan
{
   [Key]
   public int Id { get; set; }

   public string Action { get; set; } = string.Empty;

   public string Type { get; set; } = string.Empty;

   public string FromFilePath { get; set; } = string.Empty;

   public string ToFilePath { get; set; } = string.Empty;

   public string DirectoryPath { get; set; } = string.Empty;

   public bool? IsSuccess { get; set; }

   public string Message { get; set; } = string.Empty;

   internal class Actions
   {
      public const string DELETE = "delete";
      public const string COPY = "copy";
      public const string CREATE = "create";
      public const string UPDATE = "update";
   }

   internal class Types
   {
      public const string DIRECTORY = "directory";
      public const string FILE = "file";
   }
}