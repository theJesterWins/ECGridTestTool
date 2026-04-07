using System;

namespace ECGridOsSafeWorkbench;

internal sealed class AppSettings
{
    public string ServiceUrl { get; set; } = "https://os.ecgrid.io/v4.1/prod/ECGridOS.asmx";
    public string SessionId { get; set; } = string.Empty;
    public bool RememberSessionId { get; set; }
    public string FileName { get; set; } = "test.edi";
    public string MailboxFilter { get; set; } = string.Empty;
    public bool UseExplicitSendTarget { get; set; }
    public string SendNetworkId { get; set; } = string.Empty;
    public string SendMailboxId { get; set; } = string.Empty;
    public bool RequireDownloadConfirmation { get; set; } = true;
    public bool BlockCrossMailboxDownloads { get; set; } = true;
}

internal sealed class SessionInfo
{
    public string ECGridOSVersion { get; set; } = string.Empty;
    public string SessionID { get; set; } = string.Empty;
    public int UserID { get; set; }
    public string LoginName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string EMail { get; set; } = string.Empty;
    public string AuthLevel { get; set; } = string.Empty;
    public int NetworkID { get; set; }
    public int MailboxID { get; set; }
    public short TimeOut { get; set; }
    public short OpenSessions { get; set; }
    public string Ip { get; set; } = string.Empty;
}

internal sealed class MailboxRecord
{
    public int MailboxID { get; set; }
    public int NetworkID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string OwnerLoginName { get; set; } = string.Empty;
    public string OwnerEMail { get; set; } = string.Empty;
}

internal sealed class ParcelRecord
{
    public long ParcelID { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DateTime? ParcelDate { get; set; }
    public string StatusCode { get; set; } = string.Empty;
    public string StatusMessage { get; set; } = string.Empty;
    public string Valid { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string MailbagControlID { get; set; } = string.Empty;
    public int? NetworkIDFrom { get; set; }
    public string NetworkNameFrom { get; set; } = string.Empty;
    public int? MailboxIDFrom { get; set; }
    public string MailboxNameFrom { get; set; } = string.Empty;
    public int? NetworkIDTo { get; set; }
    public string NetworkNameTo { get; set; } = string.Empty;
    public int? MailboxIDTo { get; set; }
    public string MailboxNameTo { get; set; } = string.Empty;
    public int? ActualBytes { get; set; }
    public string Acknowledgment { get; set; } = string.Empty;
}

internal sealed class DownloadedFileInfo
{
    public long ParcelID { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DateTime? FileDate { get; set; }
    public int Bytes { get; set; }
    public string Standard { get; set; } = string.Empty;
    public string ContentBase64String { get; set; } = string.Empty;
    public byte[] ContentBytes { get; set; } = Array.Empty<byte>();
    public bool RouterArchive { get; set; }
}
