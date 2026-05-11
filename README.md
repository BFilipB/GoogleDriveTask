# Google Drive CLI Manager

This is a command-line tool I built for managing Google Drive files directly from your terminal. You can download all your files in parallel, search for specific files, and upload new ones—all without leaving the command line.

## Prerequisites

- **.NET 8.0 or later** – [Download here](https://dotnet.microsoft.com/download)
- **Google account** with Google Drive access
- **Google Cloud project** with Drive API enabled (see setup below)

## Quick Start

1. Clone the repo:
```bash
git clone https://github.com/BFilipB/GoogleDriveTask.git
cd GoogleDriveTask
```

2. Build it:
```bash
dotnet build -c Release
```

3. Set up Google credentials (see **Google Drive API Setup** section below)

4. Run commands:
```bash
dotnet run --configuration Release -- sync
dotnet run --configuration Release -- search "my photos"
dotnet run --configuration Release -- upload "C:\Users\You\file.txt" "MyFolder"
```

Or if you prefer using the compiled executable directly:
```bash
cd bin/Release/net8.0
.\GoogleDriveCli.exe sync
.\GoogleDriveCli.exe search "my photos"
.\GoogleDriveCli.exe upload "C:\Users\You\file.txt" "MyFolder"
```

## Google Drive API Setup – Complete Step-by-Step

This is the most important part. You need to create a Google Cloud project and get OAuth credentials. Here's exactly how:

### Step 1: Create a Google Cloud Project

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. At the top, you'll see "Select a Project" dropdown. Click it.
3. Click the **+ CREATE PROJECT** button
4. Enter a project name (e.g., "GoogleDriveCli")
5. Click **CREATE**
6. Wait a few seconds for the project to be created, then you'll be taken to the project dashboard

### Step 2: Enable the Google Drive API

1. In the Google Cloud Console, go to **APIs & Services** > **Library** (left sidebar)
2. Search for "Google Drive API"
3. Click on **Google Drive API**
4. Click the blue **ENABLE** button
5. Wait for it to enable (a few seconds)

### Step 3: Create OAuth 2.0 Credentials

1. Go back to **APIs & Services** > **Credentials** (left sidebar)
2. Click **+ CREATE CREDENTIALS** button (top left)
3. Select **OAuth client ID**
4. If you see a warning about the OAuth consent screen, click **CONFIGURE CONSENT SCREEN**
   - Select **External** for user type
   - Click **CREATE**
   - Fill in the basic info (App name, your email, etc.)
   - Scroll down and add your email to the "Test users" section
   - Click **SAVE AND CONTINUE** through all steps
   - Don't worry about the optional fields; just skip them
   - Click **SAVE AND CONTINUE** on the final page
5. Go back to **Credentials** and click **+ CREATE CREDENTIALS** > **OAuth client ID** again
6. Choose **Desktop application**
7. Click **CREATE**
8. A dialog will pop up with your credentials. Click **DOWNLOAD JSON**
9. Save the file to your Downloads folder (you'll move it next)

### Step 4: Place the Credentials File

**Important:** The `client_secret.json` file must be in a specific location for the app to find it.

1. You just downloaded a JSON file from Google Cloud (it might be named `client_secret_xxxxx.json`)
2. **Rename it to exactly `client_secret.json`** (case-sensitive; lowercase "client_secret")
3. **Move it to the GoogleDriveCli project root** – that's the folder where you see:
   - `GoogleDriveCli.csproj`
   - `.gitignore`
   - `README.md`
   - `LICENSE`

So the final path should be: `C:\Users\YourUsername\source\repos\GoogleDriveCli\client_secret.json`

**Verify it's in the right place:**
```bash
dir client_secret.json
# or on Mac/Linux:
ls -la client_secret.json
```

If you see the file listed, you're good. If it says "not found", you put it in the wrong folder.

### Step 5: First Run and Authentication

1. Run the app:
```bash
dotnet run --configuration Release -- sync
```

2. The app will:
   - Detect that you don't have a saved token
   - Open your default browser to Google's login page
   - Ask you to log in to your Google account
   - Ask for permission to access your Google Drive
   - After you approve, the page will show "Authorization successful" and you can close the browser tab

3. The app saves your authentication token locally (see "Token Storage" below) so you don't have to log in again next time.

### Token Storage

After first login, your authentication token is stored at:
- **Windows:** `%APPDATA%\GoogleDriveCli\token.json`
- **Mac:** `~/.config/GoogleDriveCli/token.json`
- **Linux:** `~/.config/GoogleDriveCli/token.json`

You can delete this file anytime to force a re-authentication on the next run. The token is never sent anywhere – it's just used locally to talk to Google's API.

### Troubleshooting Google Setup

**"Can't find client_secret.json"**
- Make sure the file is named exactly `client_secret.json` (all lowercase)
- Make sure it's in the project root, not in a subfolder
- Make sure you didn't accidentally put it in `bin/` or `obj/`
- Try running from the project root: `dotnet run --configuration Release -- sync`

**"Google won't let me log in"**
- Did you enable the Google Drive API? (step 2)
- Did you add your email to the test users? (step 3)
- If it still fails, delete the token file and try again: `del %APPDATA%\GoogleDriveCli\token.json`

**"Access Denied" or "Insufficient Permissions"**
- The Drive API isn't enabled in your Google Cloud project. Go back to step 2.
- Your credentials might be wrong. Delete `%APPDATA%\GoogleDriveCli\token.json` and try again.

**"Invalid client" error**
- You're using the wrong JSON file. Make sure you downloaded the OAuth client credentials (not an API key or service account key).
- Delete your token file and delete `client_secret.json`, then download a fresh copy from Google Cloud.

## How I Built This

### Parallel Downloads

I went with `Parallel.ForEachAsync` with a `MaxDegreeOfParallelism` of 5. The reason I picked this approach: I wanted something that's bounded (doesn't spin up unlimited tasks), but doesn't feel like overkill. A semaphore or channel would work too, but `Parallel.ForEachAsync` is cleaner for this use case and handles thread pool management automatically. Five concurrent downloads keeps the network pipe full without hammering the API or your system.

The files download to a `Downloads/` folder in your current directory. If a file already exists locally, it gets skipped.

### Thread-Safe Statistics

For counting successful/failed downloads without race conditions, I used `Interlocked.Increment` and `Interlocked.Add`. These are lock-free atomic operations—no mutexes, no contention. Every thread increments the counters safely, and the final stats are always accurate. I track:
- Total files found
- Successful downloads
- Failed downloads
- Skipped (already exist)
- Total bytes downloaded
- Elapsed time

### Download Status Detection in Search

When you search for files, the app shows whether each one is already downloaded. I check the local `Downloads/` folder to see if the file exists—straightforward and fast. No manifest file needed for this; the filesystem is the source of truth. If you want to force a re-download, just delete the file locally and run sync again.

### The Downloads Folder

All synced files go into a `Downloads/` folder created in your current working directory. So if you run:

```bash
cd C:\Users\You\Projects
dotnet run --configuration Release -- sync
```

The files will download to `C:\Users\You\Projects\Downloads/`. If you run from a different directory, the `Downloads/` folder is created there instead.

The folder structure in your Drive is preserved. For example:
- `Google Drive/Documents/MyFile.txt` → `Downloads/Documents/MyFile.txt`
- `Google Drive/Photos/Vacation/pic.jpg` → `Downloads/Photos/Vacation/pic.jpg`

If the same folder structure already exists locally, new files are added and existing ones are skipped.

## Commands

### `sync`

Downloads everything from your Google Drive to the `Downloads/` folder. Runs up to 5 downloads in parallel. When it finishes, you get a summary:

```
Total Items:             150
Successful Downloads:    147
Failed Downloads:        1
Skipped (Already Exist): 2
Total Bytes Downloaded:  2.45 GB
Time Elapsed:            00:05:32
```

### `search [query]`

Searches your Drive by filename. Shows each result with its download status:

```
Found 3 result(s):

Name                      Type      Status
────────────────────────────────────────────
vacation_photos.zip       File      [Not Downloaded]
photos_2024.pdf          File      [Downloaded]
Photos                   Folder    [Not Downloaded]
```

### `upload [local_path] [drive_path]`

Uploads a file from your computer to Google Drive. If you specify a folder path that doesn't exist, it creates the whole hierarchy:

```bash
./GoogleDriveCli.exe upload "C:\Users\You\Documents\file.txt" "Backups/Documents/2024"
```

If the folders `Backups/Documents/2024` don't exist, they get created. If something goes wrong—bad path, permission issue, file not found—you get a clear error message.

## Error Handling

The app handles network hiccups gracefully. If Google's API rate-limits you (HTTP 429), or there's a transient network error, it retries automatically with exponential backoff. It won't crash on a bad file path or permission error—it'll tell you what went wrong and keep going.

## Architecture

The code is organized into services:

- `AuthService` – Handles OAuth login and token storage
- `DriveService` – Talks to Google Drive API, lists/searches/downloads/uploads files
- `FileService` – Handles local file operations and coordinates parallel downloads
- `RetryPolicy` – Exponential backoff retry logic for failed requests
- `LocalFileManifest` – Tracks downloaded files (optional manifest for future features)
- `SyncStatisticsCollector` – Collects stats in a thread-safe way
- `ConsoleStatisticsRenderer` – Pretty-prints output using Spectre.Console

Each service has a single job, so adding features or fixing bugs is straightforward.

## Project Structure

```
GoogleDriveCli/
├── Program.cs                 – Entry point, command dispatcher
├── GoogleDriveCli.csproj      – Project file (lists NuGet dependencies)
├── Services/
│   ├── AuthService.cs              – OAuth 2.0 login and token management
│   ├── DriveService.cs             – Google Drive API wrapper
│   ├── FileService.cs              – Local file operations, parallel downloads
│   ├── RetryPolicy.cs              – Retry logic with exponential backoff
│   ├── LocalFileManifest.cs        – Tracks which files have been downloaded
│   ├── SyncStatisticsCollector.cs  – Thread-safe stat collection
│   └── ConsoleStatisticsRenderer.cs – Spectre.Console formatting
├── Properties/
│   └── launchSettings.json    – Launch configuration
├── bin/
│   └── Release/net8.0/        – Compiled executable (after dotnet build)
├── LICENSE                    – MIT License
├── README.md                  – This file
└── client_secret.json         – [YOU PROVIDE THIS] Google OAuth credentials
```

## Troubleshooting

**Can't find `client_secret.json`**  
Make sure it's in the project root next to `GoogleDriveCli.csproj`. The error message will tell you where it's looking.

**Authentication fails**  
Delete `%APPDATA%/GoogleDriveCli/token.json` (or `~/.config/GoogleDriveCli/token.json` on Linux) and try again. You might also want to re-download your credentials file from Google Cloud if they're old.

**Rate limiting (HTTP 429)**  
The app retries automatically. If it happens a lot, you might be hitting Google's quotas. Just wait a bit and try again.

**"dotnet" command not found**  
You need to install .NET 8.0 or later. Download it from [dotnet.microsoft.com](https://dotnet.microsoft.com/download).

**Build fails with errors**  
Make sure you're in the project root (the folder with `GoogleDriveCli.csproj`). Try:
```bash
dotnet restore
dotnet build -c Release
```

**Downloaded files are incomplete or corrupted**  
This sometimes happens if:
- Your network connection dropped mid-download
- The file was deleted on Google Drive before it finished downloading
- You ran the program with very high concurrency (changed maxConcurrency in Program.cs)

Just delete the file locally and run sync again. The app checks for existing files and skips them, so you won't re-download files that are already complete.

**Want faster downloads?**  
Edit `Program.cs` and change `maxConcurrency: 5` to something higher (8, 10, etc.). But be aware—too high and Google might rate-limit you. Start with 5 and increase gradually if needed.

**Search shows files but says they're already downloaded when they're not**  
Delete the `Downloads/` folder and run sync again, or check manually that the folder structure is correct:
```bash
dir Downloads
```

**Upload keeps failing**  
- Make sure the local file exists: `dir "C:\path\to\file.txt"`
- Make sure you have write access to Google Drive
- If the folder path has special characters, try wrapping it in quotes: `upload "file.txt" "Folder/Sub-Folder"`

## Advanced Usage

### Change the Download Directory

By default, files download to `Downloads/` in your current directory. You can modify this in `Program.cs` line where it says:
```csharp
var fileService = new FileService("Downloads");
```

Change `"Downloads"` to any path you want, like `"C:\GoogleDriveBackup"`.

### Adjust Parallel Concurrency

To download faster (or slower), change the `maxConcurrency` parameter in `Program.cs`:
```csharp
var stats = await fileService.DownloadFilesInParallelAsync(
    allFiles,
    async (fileId, localPath) => await driveService.DownloadFileAsync(fileId, localPath),
    maxConcurrency: 5);  // Change 5 to something else (e.g., 8, 10, 3)
```

Higher = faster but more network load and risk of hitting Google's rate limits.
Lower = slower but safer for network and API quotas.

### Delete the Token to Force Re-Authentication

If you want to switch Google accounts or re-authenticate:
```bash
# Windows
del %APPDATA%\GoogleDriveCli\token.json

# Mac/Linux
rm ~/.config/GoogleDriveCli/token.json
```

Next time you run the app, you'll be prompted to log in again.

## License

MIT. Do what you want with it.
