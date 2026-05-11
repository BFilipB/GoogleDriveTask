using Google.Apis.Drive.v3;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GoogleDriveCli.Services
{
    public class DriveFileInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public string? MimeType { get; set; }
        public long? Size { get; set; }
        public DateTimeOffset? ModifiedTime { get; set; }
    }

    public class DriveService
    {
        private readonly Google.Apis.Drive.v3.DriveService _service;
        private readonly RetryPolicy _retryPolicy;

        public DriveService(UserCredential credential)
        {
            _service = new Google.Apis.Drive.v3.DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "GoogleDriveCli"
            });
            _retryPolicy = new RetryPolicy(maxAttempts: 5, initialDelayMs: 200, backoffMultiplier: 2.0);
        }

        /// <summary>
        /// Lists all files in the root of Google Drive with retry logic.
        /// </summary>
        public async Task<List<DriveFileInfo>> ListFilesAsync(string? parentFolderId = null)
        {
            var files = new List<DriveFileInfo>();

            var result = await _retryPolicy.ExecuteAsync(async () =>
            {
                var fileList = new List<DriveFileInfo>();
                string? pageToken = null;

                do
                {
                    var request = _service.Files.List();
                    request.Spaces = "drive";
                    request.Fields = "files(id, name, mimeType, size, modifiedTime), nextPageToken";
                    request.PageSize = 1000;
                    request.PageToken = pageToken;

                    if (string.IsNullOrEmpty(parentFolderId))
                    {
                        request.Q = "'root' in parents and trashed=false";
                    }
                    else
                    {
                        request.Q = $"'{parentFolderId}' in parents and trashed=false";
                    }

                    var fileResult = await request.ExecuteAsync();
                    if (fileResult.Files != null)
                    {
                        foreach (var file in fileResult.Files)
                        {
                            fileList.Add(new DriveFileInfo
                            {
                                Id = file.Id,
                                Name = file.Name,
                                IsFolder = file.MimeType == "application/vnd.google-apps.folder",
                                MimeType = file.MimeType,
                                Size = file.Size,
                                ModifiedTime = file.ModifiedTimeDateTimeOffset
                            });
                        }
                    }
                    pageToken = fileResult.NextPageToken;
                } while (!string.IsNullOrEmpty(pageToken));

                return fileList;
            }, "ListFiles");

            if (result.Success && result.Data != null)
            {
                return result.Data;
            }

            if (result.LastException != null)
            {
                ConsoleStatisticsRenderer.RenderError($"Failed to list files after {result.Attempts} attempts: {result.LastException.Message}");
            }

            return files;
        }

        /// <summary>
        /// Searches for files by name in Google Drive with retry logic.
        /// </summary>
        public async Task<List<DriveFileInfo>> SearchFilesAsync(string query)
        {
            var result = await _retryPolicy.ExecuteAsync(async () =>
            {
                var files = new List<DriveFileInfo>();
                string? pageToken = null;

                do
                {
                    var request = _service.Files.List();
                    request.Spaces = "drive";
                    request.Fields = "files(id, name, mimeType, size, modifiedTime), nextPageToken";
                    request.Q = $"name contains '{EscapeQuery(query)}' and trashed=false";
                    request.PageSize = 1000;
                    request.PageToken = pageToken;

                    var fileResult = await request.ExecuteAsync();
                    if (fileResult.Files != null)
                    {
                        foreach (var file in fileResult.Files)
                        {
                            files.Add(new DriveFileInfo
                            {
                                Id = file.Id,
                                Name = file.Name,
                                IsFolder = file.MimeType == "application/vnd.google-apps.folder",
                                MimeType = file.MimeType,
                                Size = file.Size,
                                ModifiedTime = file.ModifiedTimeDateTimeOffset
                            });
                        }
                    }
                    pageToken = fileResult.NextPageToken;
                } while (!string.IsNullOrEmpty(pageToken));

                return files;
            }, "SearchFiles");

            if (result.Success && result.Data != null)
            {
                return result.Data;
            }

            if (result.LastException != null)
            {
                ConsoleStatisticsRenderer.RenderError($"Search failed after {result.Attempts} attempts: {result.LastException.Message}");
            }

            return new List<DriveFileInfo>();
        }

        /// <summary>
        /// Downloads a file by its ID to the specified local path with retry logic.
        /// Returns true if successful, false otherwise.
        /// </summary>
        public async Task<bool> DownloadFileAsync(string fileId, string localPath)
        {
            var result = await _retryPolicy.ExecuteAsync(async () =>
            {
                var request = _service.Files.Get(fileId);
                using (var stream = new FileStream(localPath, FileMode.Create, FileAccess.Write))
                {
                    await request.DownloadAsync(stream);
                }
                return true;
            }, $"DownloadFile:{fileId}");

            if (!result.Success)
            {
                if (File.Exists(localPath))
                {
                    try { File.Delete(localPath); } catch { }
                }
                ConsoleStatisticsRenderer.RenderWarning($"Failed to download '{Path.GetFileName(localPath)}' after {result.Attempts} attempts");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Uploads a file from local file system to a folder in Google Drive with retry logic.
        /// If parentFolderId is null, uploads to root.
        /// </summary>
        public async Task<bool> UploadFileAsync(string localPath, string? parentFolderId = null)
        {
            if (!File.Exists(localPath))
            {
                ConsoleStatisticsRenderer.RenderError($"Local file not found: {localPath}");
                return false;
            }

            var result = await _retryPolicy.ExecuteAsync(async () =>
            {
                var fileMetadata = new Google.Apis.Drive.v3.Data.File()
                {
                    Name = Path.GetFileName(localPath)
                };

                if (!string.IsNullOrEmpty(parentFolderId))
                {
                    fileMetadata.Parents = new List<string> { parentFolderId };
                }

                using (var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read))
                {
                    var request = _service.Files.Create(fileMetadata, stream, "application/octet-stream");
                    request.Fields = "id";
                    var uploadResult = await request.UploadAsync();

                    if (uploadResult.Status == Google.Apis.Upload.UploadStatus.Completed)
                    {
                        return true;
                    }
                    else
                    {
                        throw new Exception($"Upload status: {uploadResult.Status}");
                    }
                }
            }, $"UploadFile:{Path.GetFileName(localPath)}");

            if (result.Success)
            {
                ConsoleStatisticsRenderer.RenderSuccess($"File uploaded successfully: {Path.GetFileName(localPath)}");
                return true;
            }
            else
            {
                ConsoleStatisticsRenderer.RenderError($"Upload failed after {result.Attempts} attempts: {result.LastException?.Message}");
                return false;
            }
        }

        /// <summary>
        /// Finds or creates a folder by path (e.g., "Folder1/Subfolder2") with retry logic.
        /// Returns the folder ID, or null if creation fails.
        /// </summary>
        public async Task<string?> FindOrCreateFolderAsync(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                return null;

            var parts = folderPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            string? currentParentId = null;

            foreach (var part in parts)
            {
                var result = await _retryPolicy.ExecuteAsync(async () =>
                {
                    var request = _service.Files.List();
                    request.Spaces = "drive";
                    request.Fields = "files(id, name, mimeType)";
                    request.PageSize = 1000;
                    request.Q = string.IsNullOrEmpty(currentParentId)
                        ? $"name = '{EscapeQuery(part)}' and mimeType = 'application/vnd.google-apps.folder' and trashed=false and 'root' in parents"
                        : $"name = '{EscapeQuery(part)}' and mimeType = 'application/vnd.google-apps.folder' and trashed=false and '{currentParentId}' in parents";

                    var searchResult = await request.ExecuteAsync();
                    return searchResult;
                }, $"FindFolder:{part}");

                if (!result.Success)
                {
                    ConsoleStatisticsRenderer.RenderWarning($"Failed to search for folder '{part}'");
                    return null;
                }

                var searchResult = result.Data;
                if (searchResult?.Files?.Count > 0)
                {
                    currentParentId = searchResult.Files[0].Id;
                }
                else
                {
                    // Create the folder
                    var createResult = await _retryPolicy.ExecuteAsync(async () =>
                    {
                        var folderMetadata = new Google.Apis.Drive.v3.Data.File()
                        {
                            Name = part,
                            MimeType = "application/vnd.google-apps.folder"
                        };

                        if (!string.IsNullOrEmpty(currentParentId))
                        {
                            folderMetadata.Parents = new List<string> { currentParentId };
                        }
                        else
                        {
                            folderMetadata.Parents = new List<string> { "root" };
                        }

                        var createRequest = _service.Files.Create(folderMetadata);
                        createRequest.Fields = "id";
                        return await createRequest.ExecuteAsync();
                    }, $"CreateFolder:{part}");

                    if (!createResult.Success || createResult.Data == null)
                    {
                        ConsoleStatisticsRenderer.RenderWarning($"Failed to create folder '{part}'");
                        return null;
                    }

                    currentParentId = createResult.Data.Id;
                }
            }

            return currentParentId;
        }

        private static string EscapeQuery(string query)
        {
            return query.Replace("'", "\\'");
        }
    }
}