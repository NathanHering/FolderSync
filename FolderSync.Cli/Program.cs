using System.Diagnostics;
using FolderSync.Lib;

internal class Program
{
   private static void Main(string[] args)
   {
      Stopwatch stopwatch = new();
      stopwatch.Start();

      MainAsync(args).GetAwaiter().GetResult();

      stopwatch.Stop();
      TimeSpan ts = stopwatch.Elapsed;
      Console.WriteLine($"{ts}");
   }

   private static async Task MainAsync(string[] args)
   {
      FolderSyncService service = new("E:", "F:")
      {
         CutoffTime = DateTime.Now.AddMinutes(3)
      };
      await service.CreateSyncPlanAsync();
      await service.SyncFoldersAsync();
   }
}