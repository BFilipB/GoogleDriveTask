using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GoogleDriveCli.Services
{
    public class FileService
    {
        private readonly LocalFileManifest _manifest;

        public FileService(string downloadDirectory = "Downloads")
        {
            _manifest = new LocalFileManifest(downloadDirectory);
            _manifest.Initialize();
        }

        /// <summary>
        /// Downloads files in parallel with controlled concurrency using Parallel.ForEachAsync.
        /// Returns statistics about the sync operation.
        /// </summary>
        public async Task<SyncStatistics> DownloadFilesInParallelAsync(
            List<DriveFileInfo> files,
            Func<string, string, Task<bool>> downloadFunc,
            int maxConcurrency = 5)
        {
            var stats = new SyncStatisticsCollector();

            // Ensure directory exists
            var downloadDir = _manifest.GetDownloadDirectory();
            if (!Directory.Exists(downloadDir))
            {
                Directory.CreateDirectory(downloadDir);
            }

            // Filter out folders and files already downloaded
            var filesToDownload = files
                .Where(f => !f.IsFolder)
                .ToList();

            var totalFiles = filesToDownload.Count;

            // Use Parallel.ForEachAsync with controlled concurrency
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrency,
                CancellationToken = CancellationToken.None
            };

            await Parallel.ForEachAsync(filesToDownload, options, async (file, ct) =>
            {
                var localPath = Path.Combine(downloadDir, file.Name);

                // Check if file already exists
                if (_manifest.IsFileDownloaded(file.Name))
                {
                    stats.RecordSkipped(file.Name);
                    ConsoleStatisticsRenderer.RenderFileProgress(file.Name, "skipped");
                    return;
                }

                try
                {
                    ConsoleStatisticsRenderer.RenderFileProgress(file.Name, "downloading");
                    bool success = await downloadFunc(file.Id, localPath);

                    if (success)
                    {
                        long fileSize = file.Size ?? 0;
                        _manifest.RecordDownload(file.Id, file.Name, fileSize);
                        stats.RecordSuccess(file.Name, fileSize);
                        ConsoleStatisticsRenderer.RenderFileProgress(file.Name, "success");
                    }
                    else
                    {
                        stats.RecordFailure(file.Name, "Download returned false");
                        ConsoleStatisticsRenderer.RenderFileProgress(file.Name, "failed");
                    }
                }
                catch (Exception ex)
                {
                    stats.RecordFailure(file.Name, ex.Message);
                    ConsoleStatisticsRenderer.RenderFileProgress(file.Name, "failed");
                }
            });

            // Save manifest to persist downloaded files
            await _manifest.SaveAsync();

            return stats.Complete(totalFiles);
        }


        /// <summary>
        /// Gets the list of files already present in the download directory.
        /// </summary>
        public List<string> GetDownloadedFileNames()
        {
            return _manifest.GetDownloadedFileNames();
        }

        /// <summary>
        /// Checks if a file is downloaded locally.
        /// </summary>
        public bool IsFileDownloaded(string fileName)
        {
            return _manifest.IsFileDownloaded(fileName);
        }

        /// <summary>
        /// Gets the path to the download directory.
        /// </summary>
        public string GetDownloadDirectory()
        {
            return _manifest.GetDownloadDirectory();
        }
    }
}
