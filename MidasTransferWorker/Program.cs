using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Microsoft.Extensions.Configuration;
using MidasTransferWorker.Models;
using MidasTransferWorker.Services;

namespace MidasTransferWorker
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            // Log to a dedicated Windows Event Log channel ("MidasTransferWorker"), which appears
            // under Event Viewer > Applications and Services Logs. The log and its source are created
            // at install time by the MSI (WiX util:EventSource), so we do not manage them at runtime
            // (which would require admin rights). Console is kept for interactive/debug runs.
            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithProcessId()
                .WriteTo.Console()
                .WriteTo.EventLog(
                    source: "MidasTransferWorker",
                    logName: "MidasTransferWorker",
                    manageEventSource: false)
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
