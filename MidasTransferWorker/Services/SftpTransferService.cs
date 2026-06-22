using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MidasTransferWorker.Models;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using Polly;
using Polly.Retry;

namespace MidasTransferWorker.Services
{
    public class SftpTransferService : ISftpTransferService, IDisposable
    {
        private readonly MavisSftpSettings _settings;
        private readonly ILogger<SftpTransferService> _logger;

        public SftpTransferService(IOptions<MavisSftpSettings> opts, ILogger<SftpTransferService> logger)
        {
            _settings = opts.Value;
            _logger = logger;
        }

        private SftpClient CreateClient()
        {
            var connInfo = new ConnectionInfo(_settings.Host, _settings.Port, _settings.Username,
                new PasswordAuthenticationMethod(_settings.Username, _settings.Password ?? string.Empty));
            return new SftpClient(connInfo);
        }

        public async Task<ConnectionCheckResult> VerifyConnectionAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var client = CreateClient();
                client.Connect();
                if (!client.IsConnected)
                {
                    return new ConnectionCheckResult { Ok = false, ErrorMessage = "Unable to connect to SFTP server." };
                }

                // try ensure remote folder exists or create it
                try
                {
                    if (!client.Exists(_settings.RemoteFolder))
                    {
                        client.CreateDirectory(_settings.RemoteFolder);
                        _logger.LogInformation("Created remote folder {RemoteFolder}", _settings.RemoteFolder);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not verify or create remote folder {RemoteFolder}", _settings.RemoteFolder);
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
            var fileName = Path.GetFileName(localFilePath);
            var remoteFinal = CombineRemote(_settings.RemoteFolder, fileName);
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
                    using var client = CreateClient();
                    client.Connect();
                    if (!client.IsConnected)
                    {
                        throw new Exception("Unable to connect to SFTP.");
                    }

                    // Check existence
                    if (client.Exists(remoteFinal))
                    {
                        if (_settings.OverwriteRemoteFiles)
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

                    using (var fileStream = File.OpenRead(localFilePath))
                    {
                        // Ensure remote folder exists
                        EnsureRemoteFolder(client, _settings.RemoteFolder);

                        fileStream.Seek(0, SeekOrigin.Begin);
                        // Upload synchronously inside a Task to avoid blocking callers
                        await Task.Run(() => client.UploadFile(fileStream, remoteTemp, true), ct);

                        // rename temp to final (overwrite if allowed)
                        if (client.Exists(remoteFinal))
                        {
                            client.DeleteFile(remoteFinal);
                        }
                        client.RenameFile(remoteTemp, remoteFinal);
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
            // naive transient detection: network/socket or simple IO on the connection
            return ex is System.Net.Sockets.SocketException || ex is TimeoutException || ex is SshException;
        }

        public void Dispose()
        {
        }
    }
}
