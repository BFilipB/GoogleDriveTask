using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GoogleDriveCli.Services
{
    public class AuthService
    {
        private static readonly string[] Scopes = { "https://www.googleapis.com/auth/drive" };
        private readonly string _credentialsPath;
        private readonly string _tokenPath;

        public AuthService(string credentialsPath)
        {
            _credentialsPath = credentialsPath;
            _tokenPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GoogleDriveCli",
                "token.json");
        }

        /// <summary>
        /// Authenticates with Google Drive using OAuth2 and returns a UserCredential.
        /// Stores the token locally to avoid re-authentication on subsequent runs.
        /// Implements retry logic for transient network failures.
        /// </summary>
        public async Task<UserCredential> AuthenticateAsync()
        {
            if (!File.Exists(_credentialsPath))
            {
                ConsoleStatisticsRenderer.RenderError(
                    $"client_secret.json not found at {Path.GetFullPath(_credentialsPath)}\n" +
                    "Please download your OAuth 2.0 credentials from Google Cloud Console and place it at that location.\n" +
                    "Setup instructions: https://console.cloud.google.com");
                throw new FileNotFoundException(
                    $"client_secret.json not found at {_credentialsPath}");
            }

            try
            {
                // Ensure token directory exists
                string? tokenDir = Path.GetDirectoryName(_tokenPath);
                if (tokenDir != null && !Directory.Exists(tokenDir))
                {
                    Directory.CreateDirectory(tokenDir);
                }

                UserCredential? credential = null;
                int attempts = 0;
                const int maxAttempts = 3;

                while (attempts < maxAttempts)
                {
                    try
                    {
                        using (var stream = new FileStream(_credentialsPath, FileMode.Open, FileAccess.Read))
                        {
                            credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                                GoogleClientSecrets.FromStream(stream).Secrets,
                                Scopes,
                                "user",
                                CancellationToken.None,
                                new FileDataStore(_tokenPath, true));
                        }
                        break; // Success
                    }
                    catch (Exception ex) when (attempts < maxAttempts - 1)
                    {
                        attempts++;
                        ConsoleStatisticsRenderer.RenderWarning($"Authentication attempt {attempts} failed: {ex.Message}. Retrying...");
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempts)));
                    }
                }

                if (credential == null)
                {
                    throw new InvalidOperationException("Failed to authenticate after multiple attempts");
                }

                ConsoleStatisticsRenderer.RenderSuccess("Successfully authenticated with Google Drive");
                return credential;
            }
            catch (Exception ex)
            {
                ConsoleStatisticsRenderer.RenderError($"Authentication failed: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Revokes the stored token and deletes it from disk.
        /// Useful for testing or forced re-authentication.
        /// </summary>
        public void RevokeToken()
        {
            try
            {
                if (File.Exists(_tokenPath))
                {
                    File.Delete(_tokenPath);
                    ConsoleStatisticsRenderer.RenderSuccess("Token revoked and deleted. Please authenticate again on the next command.");
                }
                else
                {
                    ConsoleStatisticsRenderer.RenderWarning("No stored token found to revoke.");
                }
            }
            catch (Exception ex)
            {
                ConsoleStatisticsRenderer.RenderError($"Failed to revoke token: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the token storage path for diagnostics.
        /// </summary>
        public string GetTokenPath()
        {
            return _tokenPath;
        }

        /// <summary>
        /// Checks if a valid token is already stored.
        /// </summary>
        public bool HasStoredToken()
        {
            return File.Exists(_tokenPath);
        }
    }
}

