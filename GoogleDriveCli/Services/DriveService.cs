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

        public DriveService(UserCredential credential)
        {
            _service = new Google.Apis.Drive.v3.DriveService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "GoogleDriveCli"
            });
        }

        /// <summary>
        /// Lists all files in the root of Google Drive.
        /// </summary>
        public async Task<List<DriveFileInfo>> ListFilesAsync(string? parentFolderId = null)
        {
            var files = new List<DriveFileInfo>();
            string? pageToken = null;
            
            try
            {
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

                    var result = await request.ExecuteAsync();
                    if (result.Files != null)
                    {
                        foreach (var file in result.Files)
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
                    pageToken = result.NextPageToken;
                } while (!string.IsNullOrEmpty(pageToken));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error listing files: {ex.Message}");
            }

            return files;
        }

        /// <summary>
        /// Searches for files by name in Google Drive.
        /// </summary>
        public async Task<List<DriveFileInfo>> SearchFilesAsync(string query)
        {
            var files = new List<DriveFileInfo>();

            try
            {
                var request = _service.Files.List();
                request.Spaces = "drive";
                request.Fields = "files(id, name, mimeType, size, modifiedTime), nextPageToken";
                request.Q = $"name contains '{query}' and trashed=false";
                request.PageSize = 1000;

                var result = await request.ExecuteAsync();
                if (result.Files != null)
                {
                    foreach (var file in result.Files)
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error searching files: {ex.Message}");
            }

            return files;
        }

        /// <summary>
        /// Downloads a file by its ID to the specified local path.
        /// Returns true if successful, false otherwise.
        /// </summary>
        public async Task<bool> DownloadFileAsync(string fileId, string localPath)
        {
            try
            {
                var request = _service.Files.Get(fileId);
                using (var stream = new FileStream(localPath, FileMode.Create, FileAccess.Write))
                {
                    await request.DownloadAsync(stream);
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error downloading file {fileId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Uploads a file from local file system to a folder in Google Drive.
        /// If parentFolderId is null, uploads to root.
        /// </summary>
        public async Task<bool> UploadFileAsync(string localPath, string? parentFolderId = null)
        {
            if (!File.Exists(localPath))
            {
                Console.WriteLine($"Local file not found: {localPath}");
                return false;
            }

            try
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
                    var result = await request.UploadAsync();

                    if (result.Status == Google.Apis.Upload.UploadStatus.Completed)
                    {
                        Console.WriteLine($"File uploaded successfully: {fileMetadata.Name}");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error uploading file: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Finds or creates a folder by path (e.g., "Folder1/Subfolder2").
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
                // Search for folder with this name in current parent
                var request = _service.Files.List();
                request.Spaces = "drive";
                request.Fields = "files(id, name, mimeType)";
                request.PageSize = 1000;

                if (string.IsNullOrEmpty(currentParentId))
                {
                    request.Q = $"name = '{part}' and mimeType = 'application/vnd.google-apps.folder' and trashed=false and 'root' in parents";
                }
                else
                {
                    request.Q = $"name = '{part}' and mimeType = 'application/vnd.google-apps.folder' and trashed=false and '{currentParentId}' in parents";
                }

                try
                {
                    var result = await request.ExecuteAsync();
                    if (result.Files?.Count > 0)
                    {
                        currentParentId = result.Files[0].Id;
                    }
                    else
                    {
                        // Create the folder
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
                        var createdFolder = await createRequest.ExecuteAsync();
                        currentParentId = createdFolder.Id;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error finding/creating folder '{part}': {ex.Message}");
                    return null;
                }
            }

            return currentParentId;
        }
    }
}


