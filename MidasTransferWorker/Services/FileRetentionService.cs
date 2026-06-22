using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MidasTransferWorker.Services
{
    public class FileRetentionService : IFileRetentionService
    {
        private readonly ILogger<FileRetentionService> _logger;

        public FileRetentionService(ILogger<FileRetentionService> logger)
        {
            _logger = logger;
        }

        public async Task<RetentionResult> ApplyRetentionAsync(string folder, int retentionDays)
        {
            try
            {
                if (!Directory.Exists(folder))
                {
                    return new RetentionResult { DeletedFiles = 0, Error = $"Folder not found: {folder}" };
                }

                var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
                var files = Directory.EnumerateFiles(folder, "*.xml", SearchOption.TopDirectoryOnly).ToList();
                int deleted = 0;
                foreach (var f in files)
                {
                    try
                    {
                        var info = new FileInfo(f);
                        if (info.LastWriteTimeUtc < cutoff)
                        {
                            info.Delete();
                            deleted++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete file during retention: {File}", f);
                    }
                }

                return new RetentionResult { DeletedFiles = deleted };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retention failed for folder {Folder}", folder);
                return new RetentionResult { DeletedFiles = 0, Error = ex.Message };
            }
        }
    }
}
