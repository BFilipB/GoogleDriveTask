using GoogleDriveCli.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

#pragma warning disable CS7022

namespace GoogleDriveCli
{
    class Program
    {
        private static readonly string ClientSecretsPath = GetClientSecretsPath();

        private static string GetClientSecretsPath()
        {
            // Try 1: Current working directory (where user runs the command)
            var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "client_secret.json");
            if (File.Exists(cwdPath))
                return cwdPath;

            // Try 2: Project root (when running with dotnet run)
            var projectRoot = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Parent?.Parent?.FullName;
            if (!string.IsNullOrEmpty(projectRoot))
            {
                var projectPath = Path.Combine(projectRoot, "client_secret.json");
                if (File.Exists(projectPath))
                    return projectPath;
            }

            // Try 3: Executable directory (fallback)
            return Path.Combine(AppContext.BaseDirectory, "client_secret.json");
        }

        static async Task Main(string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    PrintUsage();
                    return;
                }

                var command = args[0].ToLower();

                switch (command)
                {
                    case "sync":
                        await HandleSync();
                        break;
                    case "search":
                        await HandleSearch(args);
                        break;
                    case "upload":
                        await HandleUpload(args);
                        break;
                    case "help":
                    case "--help":
                    case "-h":
                        PrintUsage();
                        break;
                    default:
                        Console.WriteLine($"Unknown command: {command}");
                        PrintUsage();
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }

        static async Task HandleSync()
        {
            Console.WriteLine("Starting Google Drive sync...\n");

            try
            {
                var authService = new AuthService(ClientSecretsPath);
                var credential = await authService.AuthenticateAsync();

                var driveService = new DriveService(credential);
                var fileService = new FileService("Downloads");

                Console.WriteLine("Fetching file list from Google Drive...");
                var allFiles = await driveService.ListFilesAsync();

                if (allFiles.Count == 0)
                {
                    Console.WriteLine("No files found in Google Drive.");
                    return;
                }

                Console.WriteLine($"Found {allFiles.Count} items. Starting parallel downloads...\n");

                var stats = await fileService.DownloadFilesInParallelAsync(
                    allFiles,
                    async (fileId, localPath) => await driveService.DownloadFileAsync(fileId, localPath),
                    maxConcurrency: 5);

                PrintSyncStatistics(stats);
            }
            catch (FileNotFoundException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }

        static async Task HandleSearch(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: search <query>");
                return;
            }

            var query = string.Join(" ", args.Skip(1));
            Console.WriteLine($"Searching for: '{query}'\n");

            try
            {
                var authService = new AuthService(ClientSecretsPath);
                var credential = await authService.AuthenticateAsync();

                var driveService = new DriveService(credential);
                var fileService = new FileService("Downloads");

                var results = await driveService.SearchFilesAsync(query);

                if (results.Count == 0)
                {
                    Console.WriteLine("No files found matching the search query.");
                    return;
                }

                var downloadedFiles = fileService.GetDownloadedFileNames();

                Console.WriteLine($"Found {results.Count} result(s):\n");
                Console.WriteLine("{0,-40} {1,-15} {2,-20}", "Name", "Type", "Status");
                Console.WriteLine(new string('-', 75));

                foreach (var file in results)
                {
                    var type = file.IsFolder ? "Folder" : "File";
                    var isDownloaded = fileService.IsFileDownloaded(file.Name);
                    var status = isDownloaded ? "[Downloaded]" : "[Not Downloaded]";

                    Console.WriteLine("{0,-40} {1,-15} {2,-20}", 
                        TruncateString(file.Name, 38), 
                        type, 
                        status);
                }
            }
            catch (FileNotFoundException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }

        static async Task HandleUpload(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: upload <local_path> [drive_path]");
                return;
            }

            var localPath = args[1];
            var drivePath = args.Length > 2 ? args[2] : null;

            if (!File.Exists(localPath))
            {
                Console.WriteLine($"Error: Local file not found: {localPath}");
                return;
            }

            try
            {
                var authService = new AuthService(ClientSecretsPath);
                var credential = await authService.AuthenticateAsync();

                var driveService = new DriveService(credential);

                string? parentFolderId = null;
                if (!string.IsNullOrEmpty(drivePath))
                {
                    Console.WriteLine($"Finding or creating folder path: {drivePath}");
                    parentFolderId = await driveService.FindOrCreateFolderAsync(drivePath);

                    if (string.IsNullOrEmpty(parentFolderId))
                    {
                        Console.WriteLine("Failed to find or create the target folder.");
                        return;
                    }
                }

                Console.WriteLine($"Uploading {Path.GetFileName(localPath)}...");
                bool success = await driveService.UploadFileAsync(localPath, parentFolderId);

                if (success)
                {
                    Console.WriteLine("Upload completed successfully.");
                }
                else
                {
                    Console.WriteLine("Upload failed.");
                }
            }
            catch (FileNotFoundException ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(1);
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine(@"
Google Drive CLI Manager
========================

Usage: GoogleDriveCli <command> [arguments]

Commands:
  sync              Synchronize all files from Google Drive to local Downloads directory
                    Performs parallel downloads with progress tracking.

  search <query>    Search for files in Google Drive by name
                    Results show [Downloaded] or [Not Downloaded] status

  upload <path>     Upload a file to Google Drive
  upload <path> <drive_path>
                    Upload a file to a specific folder in Google Drive
                    If the folder path doesn't exist, it will be created

  help              Display this help message

Examples:
  GoogleDriveCli sync
  GoogleDriveCli search photos
  GoogleDriveCli upload C:\myfile.txt
  GoogleDriveCli upload C:\myfile.txt MyFolder/SubFolder
");
        }

        static void PrintSyncStatistics(SyncStatistics stats)
        {
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("SYNC STATISTICS");
            Console.WriteLine(new string('=', 60));
            Console.WriteLine($"Total Items:             {stats.TotalFiles}");
            Console.WriteLine($"Successful Downloads:    {stats.SuccessfulDownloads}");
            Console.WriteLine($"Failed Downloads:        {stats.FailedDownloads}");
            Console.WriteLine($"Skipped (Already Exist): {stats.SkippedFiles}");
            Console.WriteLine($"Total Bytes Downloaded:  {FormatBytes(stats.TotalBytesDownloaded)}");
            Console.WriteLine($"Time Elapsed:            {stats.ElapsedTime:hh\\:mm\\:ss}");
            Console.WriteLine(new string('=', 60));
        }

        static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        static string TruncateString(string str, int maxLength)
        {
            if (str.Length <= maxLength)
                return str;
            return str.Substring(0, maxLength - 3) + "...";
        }
    }
}
