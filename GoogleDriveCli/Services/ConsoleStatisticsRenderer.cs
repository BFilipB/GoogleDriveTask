using Spectre.Console;
using System;

namespace GoogleDriveCli.Services
{
    public class ConsoleStatisticsRenderer
    {
        /// <summary>
        /// Renders sync statistics in a beautiful table format using Spectre.Console.
        /// </summary>
        public static void RenderSyncStatistics(SyncStatistics stats)
        {
            AnsiConsole.MarkupLine("");
            AnsiConsole.MarkupLine("[bold blue]═════════════════════════════════════════════════════[/]");
            AnsiConsole.MarkupLine("[bold cyan]           SYNC STATISTICS[/]");
            AnsiConsole.MarkupLine("[bold blue]═════════════════════════════════════════════════════[/]");

            var table = new Table()
                .BorderStyle(new Style(Color.Grey50))
                .Title("[bold cyan]Download Summary[/]");

            table.AddColumn(new TableColumn("[bold]Metric[/]").Width(30));
            table.AddColumn(new TableColumn("[bold]Value[/]").Width(20));

            table.AddRow("[yellow]Total Items[/]", $"[cyan]{stats.TotalFiles}[/]");
            table.AddRow(
                "[green]Successful Downloads[/]",
                $"[green]{stats.SuccessfulDownloads}[/] [dim]({GetPercentage(stats.SuccessfulDownloads, stats.TotalFiles)}%)[/]");
            table.AddRow(
                "[red]Failed Downloads[/]",
                $"[red]{stats.FailedDownloads}[/] [dim]({GetPercentage(stats.FailedDownloads, stats.TotalFiles)}%)[/]");
            table.AddRow(
                "[yellow]Skipped (Existing)[/]",
                $"[yellow]{stats.SkippedFiles}[/] [dim]({GetPercentage(stats.SkippedFiles, stats.TotalFiles)}%)[/]");
            table.AddRow("[cyan]Total Data Transferred[/]", $"[cyan]{FormatBytes(stats.TotalBytesDownloaded)}[/]");
            table.AddRow("[magenta]Time Elapsed[/]", $"[magenta]{stats.ElapsedTime:hh\\:mm\\:ss}[/]");

            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("[bold blue]═════════════════════════════════════════════════════[/]");
            AnsiConsole.MarkupLine("");
        }

        /// <summary>
        /// Renders search results in a formatted table.
        /// </summary>
        public static void RenderSearchResults(System.Collections.Generic.List<DriveFileInfo> results, System.Collections.Generic.List<string> downloadedFiles)
        {
            var table = new Table()
                .BorderStyle(new Style(Color.Grey50))
                .Title($"[bold cyan]Search Results ({results.Count})[/]");

            table.AddColumn(new TableColumn("[bold cyan]Name[/]").Width(40));
            table.AddColumn(new TableColumn("[bold cyan]Type[/]").Width(12));
            table.AddColumn(new TableColumn("[bold cyan]Size[/]").Width(12));
            table.AddColumn(new TableColumn("[bold cyan]Status[/]").Width(20));

            foreach (var file in results)
            {
                var type = file.IsFolder ? "[yellow]Folder[/]" : "[cyan]File[/]";
                var size = file.Size.HasValue ? $"[dim]{FormatBytes(file.Size.Value)}[/]" : "[dim]—[/]";
                var isDownloaded = downloadedFiles.Contains(file.Name);
                var status = isDownloaded
                    ? "[green]✓ Downloaded[/]"
                    : "[yellow]⚠ Not Downloaded[/]";

                var displayName = file.Name.Length > 38
                    ? file.Name.Substring(0, 35) + "..."
                    : file.Name;

                table.AddRow(displayName, type, size, status);
            }

            AnsiConsole.Write(table);
        }

        /// <summary>
        /// Renders an error message with proper formatting.
        /// </summary>
        public static void RenderError(string message)
        {
            AnsiConsole.MarkupLine("[bold red]ERROR:[/] [red]{0}[/]", message.EscapeMarkup());
        }

        /// <summary>
        /// Renders a success message.
        /// </summary>
        public static void RenderSuccess(string message)
        {
            AnsiConsole.MarkupLine("[green]✓ {0}[/]", message.EscapeMarkup());
        }

        /// <summary>
        /// Renders a warning message.
        /// </summary>
        public static void RenderWarning(string message)
        {
            AnsiConsole.MarkupLine("[yellow]⚠ {0}[/]", message.EscapeMarkup());
        }

        /// <summary>
        /// Renders a progress indicator for a file download.
        /// </summary>
        public static void RenderFileProgress(string fileName, string status)
        {
            var statusMarkup = status switch
            {
                "downloading" => "[cyan]⬇ Downloading...[/]",
                "success" => "[green]✓ Success[/]",
                "failed" => "[red]✗ Failed[/]",
                "skipped" => "[yellow]- Skipped[/]",
                _ => $"[white]{status}[/]"
            };

            var displayName = fileName.Length > 45
                ? fileName.Substring(0, 42) + "..."
                : fileName;

            AnsiConsole.MarkupLine("  [cyan]{0,-50}[/] {1}", displayName, statusMarkup);
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024.0;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        private static int GetPercentage(int part, int total)
        {
            if (total == 0) return 0;
            return (int)((double)part / total * 100);
        }
    }
}
