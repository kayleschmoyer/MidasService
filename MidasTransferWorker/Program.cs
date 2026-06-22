using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using System.IO;
using Microsoft.Extensions.Configuration;
using MidasTransferWorker.Models;
using MidasTransferWorker.Services;

namespace MidasTransferWorker
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            // configure Serilog for file + EventLog sinks with enrichment
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var logDir = Path.Combine(programData, "MidasTransferService", "logs");
            Directory.CreateDirectory(logDir);

            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithProcessId()
                .WriteTo.Console()
                .WriteTo.File(Path.Combine(logDir, "midas-transfer-.log"), rollingInterval: RollingInterval.Day)
                // The EventLog source is created at install time by the MSI installer
                // (WiX util:EventSource), so we do not manage it at runtime (which needs admin rights).
                .WriteTo.EventLog("MidasTransferWorker", manageEventSource: false)
                .MinimumLevel.Information()
                .CreateLogger();

            var builder = Host.CreateDefaultBuilder(args)
                .UseSerilog()
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.Configure<MavisSftpSettings>(hostContext.Configuration.GetSection("MavisSftp"));

                    services.AddSingleton<ISftpTransferService, SftpTransferService>();
                    services.AddSingleton<ITransferStateStore, FileTransferStateStore>();
                    services.AddSingleton<IFileRetentionService, FileRetentionService>();

                    services.AddHostedService<Worker>();
                })
                .UseWindowsService();

            var host = builder.Build();
            try
            {
                host.Run();
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
