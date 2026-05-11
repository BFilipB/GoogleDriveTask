using GoogleDriveCli.Services;
using Spectre.Console;
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
        private static readonly string ClientSecretsPath = Path.Combine(
            AppContext.BaseDirectory,
            "client_secret.json");

        static async Task Main(string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    PrintWelcome();
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
                        PrintWelcome();
                        break;
                    default:
                        ConsoleStatisticsRenderer.RenderError($"Unknown command: {command}");
                        PrintWelcome();
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                ConsoleStatisticsRenderer.RenderWarning("Operation cancelled by user");
                Environment.Exit(130);
            }
            catch (Exception ex)
            {
                ConsoleStatisticsRenderer.RenderError($"Unexpected error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    ConsoleStatisticsRenderer.RenderError($"Details: {ex.InnerException.Message}");
                }
                Environment.Exit(1);
            }
        }

        static async Task HandleSync()
        {
            AnsiConsole.MarkupLine("[bold cyan]═══════════════════════════════════════════════════════[/]");
            AnsiConsole.MarkupLine("[bold cyan]              Google Drive Sync Starting[/]");
            AnsiConsole.MarkupLine("[bold cyan]═══════════════════════════════════════════════════════[/]");
            AnsiConsole.MarkupLine("");

            try
            {
                AnsiConsole.MarkupLine("[yellow]Authenticating with Google Drive...[/]");
                var authService = new AuthService(ClientSecretsPath);
                var credential = await authService.AuthenticateAsync();
                AnsiConsole.MarkupLine("");

                var driveService = new DriveService(credential);
                var fileService = new FileService("Downloads");

                AnsiConsole.MarkupLine("[yellow]Fetching file list from Google Drive...[/]");
                var allFiles = await driveService.ListFilesAsync();
                AnsiConsole.MarkupLine("");

                if (allFiles.Count == 0)
                {
                    ConsoleStatisticsRenderer.RenderWarning("No files found in Google Drive.");
                    return;
                }

                var fileCount = allFiles.Count(f => !f.IsFolder);
                AnsiConsole.MarkupLine("[cyan]Found [bold]{0}[/] files to download[/]", fileCount);
                AnsiConsole.MarkupLine("[yellow]Starting parallel downloads with max concurrency: 5[/]");
                AnsiConsole.MarkupLine("");

                var stats = await fileService.DownloadFilesInParallelAsync(
                    allFiles,
                    async (fileId, localPath) => await driveService.DownloadFileAsync(fileId, localPath),
                    maxConcurrency: 5);

                ConsoleStatisticsRenderer.RenderSyncStatistics(stats);
            }
            catch (FileNotFoundException ex)
            {
                ConsoleStatisticsRenderer.RenderError(ex.Message);
                Environment.Exit(1);
            }
        }

        static async Task HandleSearch(string[] args)
        {
            if (args.Length < 2)
            {
                ConsoleStatisticsRenderer.RenderWarning("Usage: search <query>");
                return;
            }

            var query = string.Join(" ", args.Skip(1));
            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine("[bold cyan]Searching for: [yellow]\"{0}\"[/][/]", query.EscapeMarkup());
            AnsiConsole.MarkupLine("");

            try
            {
                var authService = new AuthService(ClientSecretsPath);
                var credential = await authService.AuthenticateAsync();
                AnsiConsole.MarkupLine("");

                var driveService = new DriveService(credential);
                var fileService = new FileService("Downloads");

                var results = await driveService.SearchFilesAsync(query);

                if (results.Count == 0)
                {
                    ConsoleStatisticsRenderer.RenderWarning("No files found matching the search query.");
                    return;
                }

                var downloadedFiles = fileService.GetDownloadedFileNames();
                AnsiConsole.MarkupLine("");

                ConsoleStatisticsRenderer.RenderSearchResults(results, downloadedFiles);
            }
            catch (FileNotFoundException ex)
            {
                ConsoleStatisticsRenderer.RenderError(ex.Message);
                Environment.Exit(1);
            }
        }

        static async Task HandleUpload(string[] args)
        {
            if (args.Length < 2)
            {
                ConsoleStatisticsRenderer.RenderWarning("Usage: upload <local_path> [drive_path]");
                AnsiConsole.MarkupLine("Example: upload \"C:\\path\\to\\file.txt\" \"Folder/SubFolder\"");
                return;
            }

            var localPath = args[1];
            var drivePath = args.Length > 2 ? args[2] : null;

            if (!File.Exists(localPath))
            {
                ConsoleStatisticsRenderer.RenderError($"Local file not found: {localPath}");
                return;
            }

            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine("[bold cyan]File Upload[/]");
            AnsiConsole.MarkupLine("[dim]File: {0}[/]", new FileInfo(localPath).Name.EscapeMarkup());
            if (!string.IsNullOrEmpty(drivePath))
            {
                AnsiConsole.MarkupLine("[dim]Target Path: {0}[/]", drivePath.EscapeMarkup());
            }
            else
            {
                AnsiConsole.MarkupLine("[dim]Target: Google Drive Root[/]");
            }
            AnsiConsole.MarkupLine("");

            try
            {
                var authService = new AuthService(ClientSecretsPath);
                var credential = await authService.AuthenticateAsync();
                AnsiConsole.MarkupLine("");

                var driveService = new DriveService(credential);

                string? parentFolderId = null;
                if (!string.IsNullOrEmpty(drivePath))
                {
                    AnsiConsole.MarkupLine("[yellow]Finding or creating folder path: {0}[/]", drivePath.EscapeMarkup());
                    parentFolderId = await driveService.FindOrCreateFolderAsync(drivePath);

                    if (string.IsNullOrEmpty(parentFolderId))
                    {
                        ConsoleStatisticsRenderer.RenderError("Failed to find or create the target folder.");
                        return;
                    }

                    AnsiConsole.MarkupLine("[green]✓ Folder path ready[/]");
                }

                AnsiConsole.MarkupLine("");
                AnsiConsole.MarkupLine("[yellow]Uploading file...[/]");
                bool success = await driveService.UploadFileAsync(localPath, parentFolderId);

                if (success)
                {
                    AnsiConsole.MarkupLine("");
                    ConsoleStatisticsRenderer.RenderSuccess("Upload completed successfully!");
                }
                else
                {
                    AnsiConsole.MarkupLine("");
                    ConsoleStatisticsRenderer.RenderError("Upload failed. Please check the logs above for more details.");
                }
            }
            catch (FileNotFoundException ex)
            {
                ConsoleStatisticsRenderer.RenderError(ex.Message);
                Environment.Exit(1);
            }
        }

        static void PrintWelcome()
        {
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[bold cyan]╔════════════════════════════════════════════════════════╗[/]");
            AnsiConsole.MarkupLine("[bold cyan]║[/]    [bold]Google Drive CLI Manager[/]    [bold cyan]║[/]");
            AnsiConsole.MarkupLine("[bold cyan]║[/]  Powerful command-line tool for Google Drive  [bold cyan]║[/]");
            AnsiConsole.MarkupLine("[bold cyan]╚════════════════════════════════════════════════════════╝[/]");
            AnsiConsole.MarkupLine("");

            AnsiConsole.MarkupLine("[bold yellow]Usage:[/]");
            AnsiConsole.MarkupLine("  GoogleDriveCli <command> [arguments]");
            AnsiConsole.MarkupLine("");

            AnsiConsole.MarkupLine("[bold cyan]Commands:[/]");

            var table = new Table()
                .BorderStyle(new Style(Color.Grey50))
                .AddColumn(new TableColumn("").Width(20))
                .AddColumn(new TableColumn("").Width(40));

            table.AddRow("[yellow]sync[/]", "Synchronize all files from Google Drive to local Downloads\n  directory using parallel processing");
            table.AddRow("[yellow]search <query>[/]", "Search for files in Google Drive by name\n  Shows [Downloaded] or [Not Downloaded] status");
            table.AddRow("[yellow]upload <path>[/]", "Upload a file to Google Drive root directory");
            table.AddRow("[yellow]upload <path> <drive_path>[/]", "Upload a file to a specific folder in Google Drive\n  Creates the folder path if it doesn't exist");
            table.AddRow("[yellow]help[/]", "Display this help message");

            AnsiConsole.Write(table);

            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine("[bold cyan]Examples:[/]");
            AnsiConsole.MarkupLine("  [dim]GoogleDriveCli sync[/]");
            AnsiConsole.MarkupLine("  [dim]GoogleDriveCli search \"photos\"[/]");
            AnsiConsole.MarkupLine("  [dim]GoogleDriveCli upload \"C:\\myfile.txt\"[/]");
            AnsiConsole.MarkupLine("  [dim]GoogleDriveCli upload \"C:\\myfile.txt\" \"MyFolder/SubFolder\"[/]");

            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine("[bold cyan]Setup Instructions:[/]");
            AnsiConsole.MarkupLine("  1. Create a Google Cloud project and enable Google Drive API");
            AnsiConsole.MarkupLine("  2. Create OAuth 2.0 credentials (Desktop app)");
            AnsiConsole.MarkupLine("  3. Download [yellow]client_secret.json[/] and place it in:");
            AnsiConsole.MarkupLine("     [green]{0}[/]", AppContext.BaseDirectory.EscapeMarkup());
            AnsiConsole.MarkupLine("  4. Run any command - you'll be prompted to authenticate");

            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine("[bold cyan]Download Directory:[/]");
            AnsiConsole.MarkupLine("  Files will be downloaded to: [green]{0}[/]", Path.Combine(Directory.GetCurrentDirectory(), "Downloads").EscapeMarkup());

            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine("[bold cyan]More Information:[/]");
            AnsiConsole.MarkupLine("  GitHub:  https://github.com/BFilipB/GoogleDriveTask");
            AnsiConsole.MarkupLine("  Docs:    https://console.cloud.google.com");

            AnsiConsole.MarkupLine("");
        }
    }
}
