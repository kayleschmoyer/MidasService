using System;
using System.Threading.Tasks;

namespace MidasTransferWorker.Services
{
    public interface ITransferStateStore
    {
        /// <summary>
        /// Get the last successful transfer date in UTC, or null if none.
        /// </summary>
        Task<DateTime?> GetLastSuccessfulUtcAsync();

        /// <summary>
        /// Persist the last successful transfer date in UTC.
        /// </summary>
        Task SetLastSuccessfulUtcAsync(DateTime utc);

        /// <summary>
        /// Path to the backing state file.
        /// </summary>
        string StateFilePath { get; }
    }
}
