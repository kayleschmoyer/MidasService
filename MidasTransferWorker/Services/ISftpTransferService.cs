using System.Threading;
using System.Threading.Tasks;

namespace MidasTransferWorker.Services
{
    public interface ISftpTransferService
    {
        /// <summary>
        /// Upload the specified file path to the configured remote folder. Returns true when uploaded successfully or skipped due to existing remote file.
        /// </summary>
        Task<UploadResult> UploadFileAsync(string localFilePath, CancellationToken cancellationToken);

        /// <summary>
        /// Verify the SFTP connection and remote folder (attempt to create remote folder if missing when supported).
        /// </summary>
        Task<ConnectionCheckResult> VerifyConnectionAsync(CancellationToken cancellationToken);
    }

    public class UploadResult
    {
        public bool Success { get; set; }
        public bool Skipped { get; set; }
        public string? RemotePath { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class ConnectionCheckResult
    {
        public bool Ok { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
