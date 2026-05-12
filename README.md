# Google Drive CLI Manager

A command-line interface (CLI) tool for interacting with Google Drive. This application allows you to authenticate with Google Drive, synchronize files locally using parallel processing, search for files, and upload local files to your Drive.

## Features

- **OAuth 2.0 Authentication**: Securely authenticate with Google Drive. Tokens are stored locally and reused to avoid re-authentication.
- **Parallel Sync**: Download all files from Google Drive to a local directory with configurable concurrency control.
- **Search**: Search for files in Google Drive with status indicators showing which files are already downloaded locally.
- **Upload**: Upload files to Google Drive with support for creating nested folder paths.
- **Statistics**: Detailed sync statistics including success/failure counts, total data transferred, and elapsed time.
- **Thread-Safe**: Uses Interlocked operations and SemaphoreSlim for safe concurrent downloads.

## Prerequisites

- .NET 8.0 SDK or later
- A Google Cloud project with the Google Drive API enabled
- OAuth 2.0 credentials (client_secret.json)

## Setup Instructions

### 1. Create a Google Cloud Project

1. Go to [Google Cloud Console](https://console.cloud.google.com)
2. Create a new project
3. Enable the Google Drive API for your project
4. Create OAuth 2.0 credentials (Desktop application)
5. Download the credentials as client_secret.json

### 2. Place Credentials File

The application uses a robust credential lookup mechanism that checks multiple locations:

1. **Current working directory** (where you run the command)
2. **Project root** (when using `dotnet run` from the project folder)
3. **Executable directory** (when running the compiled binary)

You can place client_secret.json in any of these locations. For development, place it in the project root:

`
GoogleDriveCli/
+-- GoogleDriveCli.csproj
+-- client_secret.json  <-- Place the file here for development
+-- Program.cs
+-- Services/
+-- README.md
`

For production (compiled binary), place it in the same directory as the executable:

`
bin/Release/net8.0/
+-- GoogleDriveCli.exe
+-- client_secret.json  <-- Place the file here for release builds
+-- (other DLL files)
`

The application will automatically discover the credentials file from any of these locations and display a success message upon authentication.

### 3. Build the Application

`ash
cd GoogleDriveCli
dotnet build -c Release
`

### 4. Run the Application

Navigate to the directory containing the compiled executable:

`ash
cd bin/Release/net8.0

# Display help
./GoogleDriveCli help

# Sync all files from Google Drive
./GoogleDriveCli sync

# Search for files
./GoogleDriveCli search "photos"

# Upload a file to the root of Google Drive
./GoogleDriveCli upload "C:\path\to\file.txt"

# Upload a file to a specific folder (creates folder if it doesn't exist)
./GoogleDriveCli upload "C:\path\to\file.txt" "MyFolder/SubFolder"
`

## Command Reference

### sync
Downloads all files from Google Drive to a local Downloads directory.

**Features:**
- Parallel downloads with controlled concurrency (default: 5 concurrent downloads)
- Skips files that already exist locally
- Displays real-time progress for each file
- Generates statistics upon completion

**Usage:**
`ash
GoogleDriveCli sync
`

**Output Example:**
`
Starting Google Drive sync...

Fetching file list from Google Drive...
Found 150 items. Starting parallel downloads...

[DOWNLOADING] Document.pdf...
[SUCCESS] Document.pdf
[SKIPPED] Photo.jpg (already exists)
[DOWNLOADING] Report.xlsx...
[SUCCESS] Report.xlsx
[FAILED] CorruptedFile.txt

============================================================
SYNC STATISTICS
============================================================
Total Items:             150
Successful Downloads:    147
Failed Downloads:        1
Skipped (Already Exist): 2
Total Bytes Downloaded:  2.45 GB
Time Elapsed:            00:05:32
============================================================
`

### search
Searches for files in Google Drive by name and displays their download status.

**Features:**
- Searches all files in Google Drive by name
- Shows whether each file is already downloaded locally
- Displays file type (File or Folder)

**Usage:**
`ash
GoogleDriveCli search "query"
`

**Output Example:**
`
Searching for: 'photos'

Found 5 result(s):

Name                                     Type            Status
---------------------------------------------------------------------------
Vacation_Photos.zip                      File            [Not Downloaded]
photo_album_2024.pdf                     File            [Downloaded]
Photos                                   Folder          [Not Downloaded]
`

### upload
Uploads a file from the local file system to Google Drive.

**Features:**
- Upload to root or nested folder paths
- Automatically creates folder paths if they don't exist
- Graceful error handling for invalid paths

**Usage:**
`ash
# Upload to root
GoogleDriveCli upload "C:\myfile.txt"

# Upload to a specific folder path (creates if needed)
GoogleDriveCli upload "C:\myfile.txt" "Folder1/Folder2"
`

### help
Displays help information and command usage examples.

**Usage:**
`ash
GoogleDriveCli help
`

## Architecture

### Design Principles

#### 1. **Separation of Concerns**
The application is organized into distinct service layers:

- **Program.cs**: CLI parsing and command orchestration
- **AuthService**: OAuth2 authentication and token persistence
- **DriveService**: Google Drive API interactions (list, search, download, upload, folder management)
- **FileService**: Local file system operations and parallel download coordination

#### 2. **Parallel Download Strategy**

The FileService.DownloadFilesInParallelAsync method implements efficient parallel downloading:

`
+---------------------------------------------+
¦        SemaphoreSlim (MaxConcurrency = 5)   ¦
¦  Controls max concurrent operations         ¦
+---------------------------------------------+
         ¦      ¦      ¦      ¦      ¦
    +----------------------------------+
    ¦      ¦      ¦      ¦      ¦      ¦
   Task1  Task2  Task3  Task4  Task5  Task6(waits)
    ¦      ¦      ¦      ¦      ¦      ¦
    +----------------------------------+
         Download Operations
`

- **SemaphoreSlim**: Ensures only 5 files are downloaded simultaneously, preventing resource exhaustion
- **Task.WhenAll**: Waits for all download tasks to complete
- **Interlocked Operations**: Thread-safe updates to success/failure counters

#### 3. **Thread-Safe Statistics**

Counters are updated using Interlocked class methods to prevent race conditions:

`csharp
// Thread-safe increment
Interlocked.Increment(ref _successCount);

// Thread-safe addition for bytes
Interlocked.Add(ref _totalBytes, file.Size.Value);
`

This ensures accurate statistics even with concurrent downloads.

#### 4. **Token Persistence**

- Tokens are stored in: %APPDATA%/GoogleDriveCli/token.json
- The application uses FileDataStore to automatically manage token refresh
- Users only need to authenticate once; subsequent runs reuse the stored token

#### 5. **Error Handling**

- Network errors (rate limiting, connection timeouts) are caught and logged
- File I/O errors are handled gracefully
- Invalid paths are reported clearly to the user
- Application doesn't crash on individual file failures; sync continues

## Concurrency & Thread Safety

### Key Mechanisms

1. **SemaphoreSlim**: Controls the number of concurrent download operations
   - Prevents overwhelming the system or hitting API rate limits
   - Default concurrency: 5 (can be adjusted in FileService.DownloadFilesInParallelAsync)

2. **Interlocked Operations**: All statistics counters use Interlocked methods
   - No locks needed; atomic operations prevent race conditions
   - Better performance than traditional locking mechanisms

3. **Task.WhenAll**: Ensures all downloads complete before displaying statistics

### Example Race Condition Prevention

Without thread-safe operations:
`
Thread 1: read _successCount (value = 5)
Thread 2: read _successCount (value = 5)
Thread 1: increment and write (value = 6)
Thread 2: increment and write (value = 6)  // Should be 7!
`

With Interlocked:
`
Thread 1: Interlocked.Increment (atomically increments to 6)
Thread 2: Interlocked.Increment (atomically increments to 7)  // Correct!
`

## State Management

### Downloaded File Tracking

The application tracks downloaded files by comparing local file system with Drive files:

1. **During Sync**: 
   - Fetches all files from Google Drive
   - Checks if each file exists in the local Downloads directory
   - Skips existing files to avoid re-downloads

2. **During Search**:
   - Returns search results from Google Drive
   - Queries the local Downloads directory to determine status
   - Marks files as [Downloaded] or [Not Downloaded]

### Token Storage

- Tokens are stored separately from the application executable
- Location: %APPDATA%/GoogleDriveCli/token.json
- This directory is created automatically on first authentication
- Token refreshes are handled automatically by the Google API client library

## Troubleshooting

### "client_secret.json not found"
- Ensure the file is placed in the same directory as the executable
- Use the exact filename: client_secret.json (case-sensitive on some systems)

### Authentication Fails
- Verify your Google Cloud project has Drive API enabled
- Check that your OAuth 2.0 credentials are for a "Desktop application"
- Delete %APPDATA%/GoogleDriveCli/token.json to force re-authentication

### Slow Downloads
- Increase concurrency in FileService.DownloadFilesInParallelAsync (currently 5)
- Check your network connection
- Check your Google Drive API quota

### Upload Fails
- Verify the local file path is correct and readable
- Check that you have sufficient Google Drive quota
- Ensure the folder path doesn't contain invalid characters

## Project Structure

`
GoogleDriveCli/
+-- GoogleDriveCli.csproj       # Project file with package references
+-- Program.cs                   # CLI entry point and command handlers
+-- Services/
¦   +-- AuthService.cs           # OAuth2 authentication and token storage
¦   +-- DriveService.cs          # Google Drive API wrapper
¦   +-- FileService.cs           # Local file I/O and parallel sync logic
+-- README.md                    # This file
`

## Testing

The project includes local test scripts for quick functionality verification:

- **quick-test.ps1**: Fast validation of help and search commands
- **test-googledrivecli.ps1**: Comprehensive automated test suite
- **test-googledrivecli-interactive.ps1**: Interactive testing interface

To run tests:

`powershell
# Quick validation
.\quick-test.ps1

# Full test suite
.\test-googledrivecli.ps1

# Interactive testing
.\test-googledrivecli-interactive.ps1
`

Test scripts verify:
- CLI help displays correctly
- Search functionality works with valid credentials
- Credential auto-discovery works from project root
- Error handling for missing credentials

## Dependencies

- **Google.Apis.Drive.v3** (v1.74.0): Official Google Drive API client
- **Google.Apis.Auth** (v1.74.0): OAuth2 authentication support
- **.NET 8.0**: Target framework

## License

This project is provided as-is for educational and demonstration purposes.

## Support

For issues with Google Drive API integration, refer to the [official Google Drive API documentation](https://developers.google.com/drive/api/guides/about-sdk).

For .NET-related questions, consult the [Microsoft .NET documentation](https://docs.microsoft.com/en-us/dotnet/).
