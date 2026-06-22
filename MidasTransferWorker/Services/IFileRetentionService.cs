using System;
using System.Threading.Tasks;

namespace MidasTransferWorker.Services
{
    public interface IFileRetentionService
    {
        Task<RetentionResult> ApplyRetentionAsync(string folder, int retentionDays);
    }

    public class RetentionResult
    {
        public int DeletedFiles { get; set; }
        public string? Error { get; set; }
    }
}
