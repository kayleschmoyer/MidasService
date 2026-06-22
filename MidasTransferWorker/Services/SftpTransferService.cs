using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MidasTransferWorker.Models;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Sftp;
using Polly;
using Polly.Retry;

namespace MidasTransferWorker.Services
{
    public class SftpTransferService : ISftpTransferService
    {
        private readonly IOptionsMonitor<MavisSftpSettings> _options;
        private readonly ILogger<SftpTransferService> _logger;

        public SftpTransferService(IOptionsMonitor<MavisSftpSettings> options, ILogger<SftpTransferService> logger)
        {
            // Resolve configuration on each use (via CurrentValue) so reloadOnChange edits to
            // host/credentials/remote folder take effect without restarting the service.
            _options = options;
            _logger = logger;
        }

        private SftpClient CreateClient(MavisSftpSettings settings)
        {
            var connInfo = new ConnectionInfo(settings.Host, settings.Port, settings.Username,
                new PasswordAuthenticationMethod(settings.Username, settings.Password ?? string.Empty));

            var client = new SftpClient(connInfo);

            // Verify the server's host key to defend against man-in-the-middle attacks.
            client.HostKeyReceived += (sender, e) => ValidateHostKey(settings, e);

            return client;
        }

        private void ValidateHostKey(MavisSftpSettings settings, HostKeyEventArgs e)
        {
            var configured = settings.HostKeyFingerprints?
                .Where(f => !string.IsNullOrWhiteSpace(f))
                .Select(NormalizeFingerprint)
                .ToList();

            if (configured == null || configured.Count == 0)
            {
                // No pin configured: we cannot authenticate the server. Allow the connection but warn
                // loudly so operators know to pin the fingerprint. (SHA256 logged so it can be copied.)
                _logger.LogWarning(
                    "SFTP host key is NOT being verified because no HostKeyFingerprints are configured. " +
                    "Server {Host}:{Port} presented key SHA256:{Sha256}. Add this to MavisSftp:HostKeyFingerprints to enable verification.",
                    settings.Host, settings.Port, e.FingerPrintSHA256);
                e.CanTrust = true;
                return;
            }

            var presentedSha256 = NormalizeFingerprint("SHA256:" + e.FingerPrintSHA256);
            var presentedMd5 = NormalizeFingerprint(BitConverter.ToString(e.FingerPrint).Replace("-", ":"));

            bool trusted = configured.Contains(presentedSha256) || configured.Contains(presentedMd5);
            e.CanTrust = trusted;

            if (!trusted)
            {
                _logger.LogError(
                    "SFTP host key verification FAILED for {Host}:{Port}. Presented SHA256:{Sha256} does not match any configured fingerprint. Connection rejected.",
                    settings.Host, settings.Port, e.FingerPrintSHA256);
            }
        }

        private static string NormalizeFingerprint(string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(fingerprint)) return string.Empty;

            var value = fingerprint.Trim();

            // Strip an algorithm prefix such as "SHA256:" or "MD5:".
            var colonPrefix = value.IndexOf(':');
            if (colonPrefix > 0 && colonPrefix <= 6 &&
                (value.StartsWith("SHA256", StringComparison.OrdinalIgnoreCase) ||
                 value.StartsWith("MD5", StringComparison.OrdinalIgnoreCase)))
            {
                value = value.Substring(colonPrefix + 1);
            }

            // SSH.NET reports SHA256 fingerprints without trailing base64 padding; normalise both ways.
            return value.TrimEnd('=').ToLowerInvariant();
        }

        public async Task<ConnectionCheckResult> VerifyConnectionAsync(CancellationToken cancellationToken)
        {
            var settings = _options.CurrentValue;
            try
            {
                using var client = CreateClient(settings);
                await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
                if (!client.IsConnected)
                {
                    return new ConnectionCheckResult { Ok = false, ErrorMessage = "Unable to connect to SFTP server." };
                }

                // try ensure remote folder exists or create it
                try
                {
                    if (!client.Exists(settings.RemoteFolder))
                    {
                        client.CreateDirectory(settings.RemoteFolder);
                        _logger.LogInformation("Created remote folder {RemoteFolder}", settings.RemoteFolder);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not verify or create remote folder {RemoteFolder}", settings.RemoteFolder);
                    return new ConnectionCheckResult { Ok = false, ErrorMessage = "Connected but failed to verify/create remote folder: " + ex.Message };
                }

                client.Disconnect();
                return new ConnectionCheckResult { Ok = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SFTP connection check failed (password is not logged)");
                return new ConnectionCheckResult { Ok = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<UploadResult> UploadFileAsync(string localFilePath, CancellationToken cancellationToken)
        {
            var settings = _options.CurrentValue;
            var fileName = Path.GetFileName(localFilePath);
            var remoteFinal = CombineRemote(settings.RemoteFolder, fileName);
            var remoteTemp = remoteFinal + ".part";

            try
            {
                // We'll create a retry policy with exponential backoff + jitter
                var jitterer = new Random();
                AsyncRetryPolicy retryPolicy = Policy
                    .Handle<Exception>(ex => IsTransient(ex))
                    .WaitAndRetryAsync(3, retryAttempt =>
                    {
                        var jitter = TimeSpan.FromMilliseconds(jitterer.Next(0, 1000));
                        return TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + jitter;
                    }, (ex, timeSpan, retryCount, ctx) =>
                    {
                        _logger.LogWarning(ex, "Transient SFTP upload error (attempt {Attempt}) for {File}", retryCount, localFilePath);
                    });

                bool success = false;
                bool skipped = false;

                await retryPolicy.ExecuteAsync(async ct =>
                {
                    // create a fresh client for each attempt to avoid reusing a broken connection
                    using var client = CreateClient(settings);
                    await client.ConnectAsync(ct).ConfigureAwait(false);
                    if (!client.IsConnected)
                    {
                        throw new SshConnectionException("Unable to connect to SFTP.");
                    }

                    // Check existence
                    if (client.Exists(remoteFinal))
                    {
                        if (settings.OverwriteRemoteFiles)
                        {
                            _logger.LogInformation("Remote file exists, will overwrite: {Remote}", remoteFinal);
                        }
                        else
                        {
                            _logger.LogInformation("Remote file exists, skipping: {Remote}", remoteFinal);
                            skipped = true;
                            return; // success for policy (no exception) -> no retry
                        }
                    }

                    // Ensure remote folder exists
                    EnsureRemoteFolder(client, settings.RemoteFolder);

                    try
                    {
                        using (var fileStream = File.OpenRead(localFilePath))
                        {
                            fileStream.Seek(0, SeekOrigin.Begin);
                            // SSH.NET's UploadFile is synchronous; run it off the calling thread.
                            await Task.Run(() => client.UploadFile(fileStream, remoteTemp, true), ct).ConfigureAwait(false);
                        }

                        // rename temp to final (overwrite if allowed)
                        if (client.Exists(remoteFinal))
                        {
                            client.DeleteFile(remoteFinal);
                        }
                        client.RenameFile(remoteTemp, remoteFinal);
                    }
                    catch
                    {
                        // Don't leave a partial ".part" file orphaned on the server.
                        TryDeleteRemote(client, remoteTemp);
                        throw;
                    }

                    success = true;
                }, cancellationToken);

                if (skipped)
                {
                    return new UploadResult { Success = true, Skipped = true, RemotePath = remoteFinal };
                }

                if (success)
                {
                    return new UploadResult { Success = true, RemotePath = remoteFinal };
                }

                return new UploadResult { Success = false, ErrorMessage = "Upload did not complete" };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Upload failed for {File} (password not logged)", localFilePath);
                return new UploadResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        private void TryDeleteRemote(SftpClient client, string remotePath)
        {
            try
            {
                if (client.IsConnected && client.Exists(remotePath))
                {
                    client.DeleteFile(remotePath);
                    _logger.LogInformation("Cleaned up orphaned temporary remote file {Remote}", remotePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up temporary remote file {Remote}", remotePath);
            }
        }

        private static string CombineRemote(string folder, string file)
        {
            if (string.IsNullOrEmpty(folder)) return file;
            return folder.TrimEnd('/') + "/" + file;
        }

        private void EnsureRemoteFolder(SftpClient client, string remoteFolder)
        {
            if (client.Exists(remoteFolder)) return;

            // create nested folders
            var parts = remoteFolder.Trim('/').Split('/');
            var path = "";
            foreach (var part in parts)
            {
                path += "/" + part;
                if (!client.Exists(path))
                {
                    client.CreateDirectory(path);
                }
            }
        }

        private bool IsTransient(Exception ex)
        {
            // Treat network/connection/IO disruptions as transient and retryable.
            return ex is System.Net.Sockets.SocketException
                || ex is TimeoutException
                || ex is SshConnectionException
                || ex is SshOperationTimeoutException
                || ex is ProxyException
                || ex is IOException
                || ex is ObjectDisposedException
                || ex is SshException;
        }
    }
}
