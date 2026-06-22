using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MidasTransferWorker.Services
{
    public class FileTransferStateStore : ITransferStateStore
    {
        private readonly ILogger<FileTransferStateStore> _logger;
        private readonly string _dir;
        private readonly string _stateFile;

        public FileTransferStateStore(ILogger<FileTransferStateStore> logger)
        {
            _logger = logger;
            _dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MidasTransferService");
            Directory.CreateDirectory(_dir);
            _stateFile = Path.Combine(_dir, "state.json");
        }

        public string StateFilePath => _stateFile;

        public async Task<DateTime?> GetLastSuccessfulUtcAsync()
        {
            try
            {
                if (!File.Exists(_stateFile)) return null;
                var txt = await File.ReadAllTextAsync(_stateFile).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(txt);
                if (doc.RootElement.TryGetProperty("LastSuccessfulUtc", out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.String && DateTime.TryParse(prop.GetString(), out var dt))
                    {
                        return DateTime.SpecifyKind(dt.ToUniversalTime(), DateTimeKind.Utc);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read state file {File}", _stateFile);
            }

            return null;
        }

        public async Task SetLastSuccessfulUtcAsync(DateTime utc)
        {
            try
            {
                var obj = new { LastSuccessfulUtc = utc.ToString("o") };
                var txt = JsonSerializer.Serialize(obj);
                await File.WriteAllTextAsync(_stateFile, txt).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write state file {File}", _stateFile);
                throw;
            }
        }
    }
}
