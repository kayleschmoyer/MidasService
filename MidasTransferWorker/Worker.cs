using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MidasTransferWorker.Models;
using MidasTransferWorker.Services;

namespace MidasTransferWorker
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly ISftpTransferService _transferService;
        private readonly ITransferStateStore _stateStore;
        private readonly IFileRetentionService _retentionService;
        private readonly IOptionsMonitor<MavisSftpSettings> _settings;

        public Worker(ILogger<Worker> logger,
            ISftpTransferService transferService,
            ITransferStateStore stateStore,
            IFileRetentionService retentionService,
            IOptionsMonitor<MavisSftpSettings> settings)
        {
            _logger = logger;
            _transferService = transferService;
            _stateStore = stateStore;
            _retentionService = retentionService;
            _settings = settings;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("MidasTransferWorker started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var cfg = _settings.CurrentValue;


                    if (!Directory.Exists(cfg.XmlFolder))
                    {
                        _logger.LogError("Configured XML folder does not exist: {Folder}", cfg.XmlFolder);
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                        continue;
                    }

                    // Determine today's scheduled run in local time
                    var now = DateTime.Now;
                    var scheduledTodayLocal = now.Date + cfg.NightlyTime;

                    var lastSuccessfulUtc = await _stateStore.GetLastSuccessfulUtcAsync();
                    DateTime? lastSuccessfulLocal = lastSuccessfulUtc?.ToLocalTime();

                    bool shouldRunNow = false;
                    if (now >= scheduledTodayLocal)
                    {
                        // if last successful is null or earlier than today's scheduled time (local)
                        if (lastSuccessfulLocal == null || lastSuccessfulLocal < scheduledTodayLocal)
                        {
                            shouldRunNow = true;
                        }
                    }

                    if (shouldRunNow)
                    {
                        await RunTransferOnce(cfg, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // shutting down
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error in service loop");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }

            _logger.LogInformation("MidasTransferWorker stopping");
        }

        private async Task RunTransferOnce(MavisSftpSettings cfg, CancellationToken stoppingToken)
        {
            var start = DateTime.Now;
            int found = 0, uploaded = 0, skipped = 0, failed = 0;

            _logger.LogInformation("Starting nightly transfer run at {Start}", start);

            try
            {
                // verify connection
                var check = await _transferService.VerifyConnectionAsync(stoppingToken);
                if (!check.Ok)
                {
                    _logger.LogError("SFTP connection verification failed: {Reason}", check.ErrorMessage);
                    return;
                }

                // ensure processed / quarantine folders under Program Files (x86) are available
                var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                var installBase = Path.Combine(programFilesX86, "MAM Software", "MidasTransferService");
                var processedDir = Path.Combine(installBase, "Processed");
                var quarantineDir = Path.Combine(installBase, "Quarantine");
                try
                {
                    Directory.CreateDirectory(processedDir);
                    Directory.CreateDirectory(quarantineDir);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create install folders under Program Files x86, falling back to ProgramData");
                    var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                    installBase = Path.Combine(programData, "MidasTransferService");
                    processedDir = Path.Combine(installBase, "Processed");
                    quarantineDir = Path.Combine(installBase, "Quarantine");
                    try
                    {
                        Directory.CreateDirectory(processedDir);
                        Directory.CreateDirectory(quarantineDir);
                    }
                    catch (Exception inner)
                    {
                        _logger.LogError(inner, "Failed to create fallback processed/quarantine folders under ProgramData");
                        // as a last resort use XmlFolder subfolders
                        processedDir = Path.Combine(cfg.XmlFolder, "Processed");
                        quarantineDir = Path.Combine(cfg.XmlFolder, "Quarantine");
                        Directory.CreateDirectory(processedDir);
                        Directory.CreateDirectory(quarantineDir);
                    }
                }

                var files = Directory.EnumerateFiles(cfg.XmlFolder, "*.xml", SearchOption.TopDirectoryOnly).ToList();
                found = files.Count;

                foreach (var file in files)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    try
                    {
                        var res = await _transferService.UploadFileAsync(file, stoppingToken);
                        if (res.Success)
                        {
                            if (res.Skipped)
                            {
                                skipped++;
                                _logger.LogInformation("Skipped file (remote exists): {File}", file);
                            }
                            else
                            {
                                uploaded++;
                                // move to processed folder
                                try
                                {
                                    var dest = Path.Combine(processedDir, Path.GetFileName(file));
                                    // avoid overwrite
                                    if (File.Exists(dest))
                                    {
                                        dest = Path.Combine(processedDir, DateTime.Now.ToString("yyyyMMddHHmmss") + "_" + Path.GetFileName(file));
                                    }
                                    File.Move(file, dest);
                                    _logger.LogInformation("Moved uploaded file to processed folder: {Dest}", dest);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Failed to move uploaded file to processed folder: {File}", file);
                                }
                            }
                        }
                        else
                        {
                            failed++;
                            _logger.LogWarning("Failed to upload {File}: {Error}", file, res.ErrorMessage);
                            // move to quarantine for later inspection
                            try
                            {
                                var dest = Path.Combine(quarantineDir, Path.GetFileName(file));
                                if (File.Exists(dest))
                                {
                                    dest = Path.Combine(quarantineDir, DateTime.Now.ToString("yyyyMMddHHmmss") + "_" + Path.GetFileName(file));
                                }
                                File.Move(file, dest);
                                _logger.LogInformation("Moved failed file to quarantine: {Dest}", dest);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to move file to quarantine: {File}", file);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        _logger.LogWarning(ex, "Error while uploading file {File}", file);
                        // try move to quarantine on unexpected exception
                        try
                        {
                            var dest = Path.Combine(quarantineDir, Path.GetFileName(file));
                            if (File.Exists(dest))
                            {
                                dest = Path.Combine(quarantineDir, DateTime.Now.ToString("yyyyMMddHHmmss") + "_" + Path.GetFileName(file));
                            }
                            File.Move(file, dest);
                            _logger.LogInformation("Moved errored file to quarantine: {Dest}", dest);
                        }
                        catch (Exception moveEx)
                        {
                            _logger.LogWarning(moveEx, "Failed to move errored file to quarantine: {File}", file);
                        }
                    }
                }

                // Only persist success if there were no failures (you may choose different logic)
                if (failed == 0)
                {
                    await _stateStore.SetLastSuccessfulUtcAsync(DateTime.UtcNow);
                    _logger.LogInformation("Transfer run completed successfully, state persisted");

                    // apply retention to processed folder
                    var retention = await _retentionService.ApplyRetentionAsync(processedDir, cfg.SaveRetentionDays);
                    if (!string.IsNullOrEmpty(retention.Error))
                    {
                        _logger.LogWarning("Retention reported an error: {Error}", retention.Error);
                    }
                    else
                    {
                        _logger.LogInformation("Retention deleted {Count} files", retention.DeletedFiles);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Nightly transfer run failed with exception");
            }
            finally
            {
                var end = DateTime.Now;
                _logger.LogInformation("Nightly transfer summary: Start={Start}, End={End}, Found={Found}, Uploaded={Uploaded}, Skipped={Skipped}, Failed={Failed}", start, end, found, uploaded, skipped, failed);
            }
        }
    }
}
