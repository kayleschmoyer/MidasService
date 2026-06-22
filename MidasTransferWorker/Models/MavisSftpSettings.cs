using System;

namespace MidasTransferWorker.Models
{
    public class MavisSftpSettings
    {
        public string XmlFolder { get; set; } = @"C:\HighwayData";
        public string Host { get; set; } = "ftp5.mavistrie.net";
        public int Port { get; set; } = 22;
        public string Username { get; set; } = "vast_pos";
        public string Password { get; set; } = string.Empty; // do NOT hardcode real password in source
        public string RemoteFolder { get; set; } = "/MidasVAST_Data_Highway/Production/IN";
        public TimeSpan NightlyTime { get; set; } = TimeSpan.FromHours(22); // 10:00 PM
        public int SaveRetentionDays { get; set; } = 30;
        // How long to keep failed/errored files in the Quarantine folder before deleting them.
        public int QuarantineRetentionDays { get; set; } = 30;
        // EnableNightlyAutomaticTransfer removed: the service always runs and enforces scheduled transfers
        public bool OverwriteRemoteFiles { get; set; } = false; // default skip existing

        // Expected SFTP server host key fingerprint(s) used to defend against man-in-the-middle
        // attacks. Accepts "SHA256:<base64>" (preferred) or colon-separated MD5 hex. When empty,
        // the host key is NOT verified and a warning is logged on every connection.
        public string[] HostKeyFingerprints { get; set; } = Array.Empty<string>();
    }
}