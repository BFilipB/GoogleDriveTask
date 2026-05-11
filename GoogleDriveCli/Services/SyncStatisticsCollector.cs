using System;
using System.Collections.Concurrent;
using System.Threading;

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

    public class DownloadEvent
    {
        public string FileName { get; set; } = string.Empty;
        public DownloadEventType EventType { get; set; }
        public long BytesDownloaded { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public enum DownloadEventType
    {
        Started,
        Success,
        Failed,
        Skipped
    }

    /// <summary>
    /// Thread-safe statistics collector for sync operations.
    /// Uses lock-free patterns with Interlocked operations for maximum performance.
    /// </summary>
    public class SyncStatisticsCollector
    {
        private int _successCount;
        private int _failureCount;
        private int _skipCount;
        private long _totalBytes;
        private DateTime _startTime;
        private DateTime _endTime;

        private readonly ConcurrentBag<DownloadEvent> _downloadEvents;

        public SyncStatisticsCollector()
        {
            _successCount = 0;
            _failureCount = 0;
            _skipCount = 0;
            _totalBytes = 0L;
            _startTime = DateTime.UtcNow;
            _downloadEvents = new ConcurrentBag<DownloadEvent>();
        }

        /// <summary>
        /// Records a successful download.
        /// </summary>
        public void RecordSuccess(string fileName, long bytes)
        {
            Interlocked.Increment(ref _successCount);
            Interlocked.Add(ref _totalBytes, bytes);
            _downloadEvents.Add(new DownloadEvent
            {
                FileName = fileName,
                EventType = DownloadEventType.Success,
                BytesDownloaded = bytes
            });
        }

        /// <summary>
        /// Records a failed download.
        /// </summary>
        public void RecordFailure(string fileName, string? errorMessage = null)
        {
            Interlocked.Increment(ref _failureCount);
            _downloadEvents.Add(new DownloadEvent
            {
                FileName = fileName,
                EventType = DownloadEventType.Failed,
                ErrorMessage = errorMessage
            });
        }

        /// <summary>
        /// Records a skipped file (already exists).
        /// </summary>
        public void RecordSkipped(string fileName)
        {
            Interlocked.Increment(ref _skipCount);
            _downloadEvents.Add(new DownloadEvent
            {
                FileName = fileName,
                EventType = DownloadEventType.Skipped
            });
        }

        /// <summary>
        /// Completes the statistics collection and returns the final result.
        /// </summary>
        public SyncStatistics Complete(int totalFiles)
        {
            _endTime = DateTime.UtcNow;

            return new SyncStatistics
            {
                TotalFiles = totalFiles,
                SuccessfulDownloads = _successCount,
                FailedDownloads = _failureCount,
                SkippedFiles = _skipCount,
                TotalBytesDownloaded = _totalBytes,
                ElapsedTime = _endTime - _startTime
            };
        }

        /// <summary>
        /// Gets current statistics without finishing.
        /// </summary>
        public SyncStatistics GetCurrent(int totalFiles)
        {
            return new SyncStatistics
            {
                TotalFiles = totalFiles,
                SuccessfulDownloads = _successCount,
                FailedDownloads = _failureCount,
                SkippedFiles = _skipCount,
                TotalBytesDownloaded = _totalBytes,
                ElapsedTime = DateTime.UtcNow - _startTime
            };
        }

        /// <summary>
        /// Gets all recorded download events.
        /// </summary>
        public ConcurrentBag<DownloadEvent> GetDownloadEvents()
        {
            return _downloadEvents;
        }
    }
}
