using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GoogleDriveCli.Services
{
    public class ManifestEntry
    {
        public string FileId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime DownloadedAt { get; set; }
        public string Checksum { get; set; } = string.Empty;
    }

    public class LocalFileManifest
    {
        private readonly string _downloadDirectory;
        private readonly string _manifestPath;
        private Dictionary<string, ManifestEntry> _entries;
        private object _lockObject = new object();

        private const string ManifestFileName = ".downloads-manifest.json";

        public LocalFileManifest(string downloadDirectory = "Downloads")
        {
            _downloadDirectory = downloadDirectory;
            _manifestPath = Path.Combine(_downloadDirectory, ManifestFileName);
            _entries = new Dictionary<string, ManifestEntry>();
        }

        /// <summary>
        /// Initializes the manifest by loading from disk or creating new.
        /// </summary>
        public void Initialize()
        {
            lock (_lockObject)
            {
                if (!Directory.Exists(_downloadDirectory))
                {
                    Directory.CreateDirectory(_downloadDirectory);
                }

                if (File.Exists(_manifestPath))
                {
                    try
                    {
                        var json = File.ReadAllText(_manifestPath);
                        var entries = JsonSerializer.Deserialize<List<ManifestEntry>>(json) ?? new List<ManifestEntry>();
                        _entries = entries.ToDictionary(e => e.FileName, StringComparer.OrdinalIgnoreCase);

                        // Clean up manifest entries for files that no longer exist
                        CleanUpDeletedFilesLocked();
                    }
                    catch (Exception ex)
                    {
                        System.Console.WriteLine($"Warning: Could not load manifest: {ex.Message}. Starting fresh.");
                        _entries = new Dictionary<string, ManifestEntry>();
                    }
                }
                else
                {
                    // Scan existing files and add them to manifest
                    ScanExistingFilesLocked();
                }
            }
        }

        /// <summary>
        /// Records a file as downloaded.
        /// </summary>
        public void RecordDownload(string fileId, string fileName, long size)
        {
            lock (_lockObject)
            {
                _entries[fileName] = new ManifestEntry
                {
                    FileId = fileId,
                    FileName = fileName,
                    Size = size,
                    DownloadedAt = DateTime.UtcNow,
                    Checksum = string.Empty // Could be enhanced with actual checksum
                };
            }
        }

        /// <summary>
        /// Checks if a file is recorded in the manifest.
        /// </summary>
        public bool IsFileDownloaded(string fileName)
        {
            lock (_lockObject)
            {
                if (_entries.TryGetValue(fileName, out var entry))
                {
                    var filePath = Path.Combine(_downloadDirectory, fileName);
                    return File.Exists(filePath);
                }

                // Also check if file exists even if not in manifest
                var actualPath = Path.Combine(_downloadDirectory, fileName);
                return File.Exists(actualPath);
            }
        }

        /// <summary>
        /// Gets all recorded file names.
        /// </summary>
        public List<string> GetDownloadedFileNames()
        {
            lock (_lockObject)
            {
                return _entries.Keys.ToList();
            }
        }

        /// <summary>
        /// Gets all manifest entries.
        /// </summary>
        public List<ManifestEntry> GetAllEntries()
        {
            lock (_lockObject)
            {
                return _entries.Values.ToList();
            }
        }

        /// <summary>
        /// Removes a file from the manifest and deletes it from disk.
        /// </summary>
        public bool RemoveFile(string fileName)
        {
            lock (_lockObject)
            {
                try
                {
                    var filePath = Path.Combine(_downloadDirectory, fileName);
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }

                    _entries.Remove(fileName);
                    return true;
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"Error removing file '{fileName}': {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Saves the manifest to disk.
        /// </summary>
        public async Task SaveAsync()
        {
            lock (_lockObject)
            {
                try
                {
                    var entries = _entries.Values.ToList();
                    var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_manifestPath, json);
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"Warning: Could not save manifest: {ex.Message}");
                }
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Gets the path to the download directory.
        /// </summary>
        public string GetDownloadDirectory()
        {
            return Path.GetFullPath(_downloadDirectory);
        }

        /// <summary>
        /// Gets manifest statistics.
        /// </summary>
        public (int TotalFiles, long TotalBytes) GetStatistics()
        {
            lock (_lockObject)
            {
                var totalFiles = _entries.Count;
                var totalBytes = _entries.Values.Sum(e => e.Size);
                return (totalFiles, totalBytes);
            }
        }

        private void CleanUpDeletedFilesLocked()
        {
            var keysToRemove = new List<string>();

            foreach (var kvp in _entries)
            {
                var filePath = Path.Combine(_downloadDirectory, kvp.Key);
                if (!File.Exists(filePath))
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _entries.Remove(key);
            }
        }

        private void ScanExistingFilesLocked()
        {
            if (!Directory.Exists(_downloadDirectory))
                return;

            var files = Directory.GetFiles(_downloadDirectory)
                .Where(f => Path.GetFileName(f) != ManifestFileName)
                .ToList();

            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                var fileInfo = new FileInfo(file);

                _entries[fileName] = new ManifestEntry
                {
                    FileId = string.Empty,
                    FileName = fileName,
                    Size = fileInfo.Length,
                    DownloadedAt = fileInfo.LastWriteTimeUtc,
                    Checksum = string.Empty
                };
            }
        }
    }
}
