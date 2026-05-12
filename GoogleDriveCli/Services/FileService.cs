using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GoogleDriveCli.Services
{
    public class SyncStatistics
    {
        public int TotalFiles { get; set; }
        public int SuccessfulDownloads { get; set; }
        public int FailedDownloads { get; set; }
        public int SkippedFiles { get; set; }
        public long TotalBytesDownloaded { get; set; }
        public TimeSpan ElapsedTime { get; set; }
    }

    public class FileService
    {
        private readonly string _downloadDirectory;
        private int _successCount;
        private int _failureCount;
        private int _skipCount;
        private long _totalBytes;

        public FileService(string downloadDirectory = "Downloads")
        {
            _downloadDirectory = downloadDirectory;
            _successCount = 0;
            _failureCount = 0;
            _skipCount = 0;
            _totalBytes = 0;
        }

        /// <summary>
        /// Ensures the download directory exists.
        /// </summary>
        public void EnsureDownloadDirectory()
        {
            if (!Directory.Exists(_downloadDirectory))
            {
                Directory.CreateDirectory(_downloadDirectory);
            }
        }

        /// <summary>
        /// Downloads files in parallel with controlled concurrency.
        /// Returns statistics about the sync operation.
        /// </summary>
        public async Task<SyncStatistics> DownloadFilesInParallelAsync(
            List<DriveFileInfo> files,
            Func<string, string, Task<bool>> downloadFunc,
            int maxConcurrency = 5)
        {
            EnsureDownloadDirectory();

            var startTime = DateTime.UtcNow;
            _successCount = 0;
            _failureCount = 0;
            _skipCount = 0;
            _totalBytes = 0;

            // Filter out folders and files already downloaded
            var filesToDownload = files
                .Where(f => !f.IsFolder)
                .ToList();

            var totalFiles = filesToDownload.Count;

            using (var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency))
            {
                var tasks = filesToDownload.Select(async file =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var localPath = Path.Combine(_downloadDirectory, file.Name);

                        // Check if file already exists
                        if (File.Exists(localPath))
                        {
                            Interlocked.Increment(ref _skipCount);
                            Console.WriteLine($"[SKIPPED] {file.Name} (already exists)");
                            return;
                        }

                        Console.WriteLine($"[DOWNLOADING] {file.Name}...");
                        bool success = await downloadFunc(file.Id, localPath);

                        if (success)
                        {
                            Interlocked.Increment(ref _successCount);
                            if (file.Size.HasValue)
                            {
                                Interlocked.Add(ref _totalBytes, file.Size.Value);
                            }
                            Console.WriteLine($"[SUCCESS] {file.Name}");
                        }
                        else
                        {
                            Interlocked.Increment(ref _failureCount);
                            Console.WriteLine($"[FAILED] {file.Name}");
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            }

            var elapsed = DateTime.UtcNow - startTime;

            return new SyncStatistics
            {
                TotalFiles = totalFiles,
                SuccessfulDownloads = _successCount,
                FailedDownloads = _failureCount,
                SkippedFiles = _skipCount,
                TotalBytesDownloaded = _totalBytes,
                ElapsedTime = elapsed
            };
        }

        /// <summary>
        /// Gets the list of files already present in the download directory.
        /// </summary>
        public List<string> GetDownloadedFileNames()
        {
            if (!Directory.Exists(_downloadDirectory))
                return new List<string>();

            return Directory.GetFiles(_downloadDirectory)
                .Select(Path.GetFileName)
                .Where(f => !string.IsNullOrEmpty(f))
                .Cast<string>()
                .ToList();
        }

        /// <summary>
        /// Checks if a file is downloaded locally.
        /// </summary>
        public bool IsFileDownloaded(string fileName)
        {
            var localPath = Path.Combine(_downloadDirectory, fileName);
            return File.Exists(localPath);
        }

        /// <summary>
        /// Gets the path to the download directory.
        /// </summary>
        public string GetDownloadDirectory()
        {
            return Path.GetFullPath(_downloadDirectory);
        }
    }
}
