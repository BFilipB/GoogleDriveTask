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
        /// </summary>
        public async Task<UserCredential> AuthenticateAsync()
        {
            if (!File.Exists(_credentialsPath))
            {
                throw new FileNotFoundException(
                    $"client_secret.json not found at {_credentialsPath}\n" +
                    "Please place your Google OAuth credentials file at that location.");
            }

            // Ensure token directory exists
            string? tokenDir = Path.GetDirectoryName(_tokenPath);
            if (tokenDir != null && !Directory.Exists(tokenDir))
            {
                Directory.CreateDirectory(tokenDir);
            }

            UserCredential credential;
            using (var stream = new FileStream(_credentialsPath, FileMode.Open, FileAccess.Read))
            {
                credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    Scopes,
                    "user",
                    CancellationToken.None,
                    new FileDataStore(_tokenPath, true));
            }

            return credential;
        }

        /// <summary>
        /// Revokes the stored token and deletes it from disk.
        /// </summary>
        public void RevokeToken()
        {
            if (File.Exists(_tokenPath))
            {
                File.Delete(_tokenPath);
                Console.WriteLine("Token revoked and deleted.");
            }
        }
    }
}

