using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ECGridOsSafeWorkbench;

internal sealed class EcGridOsClient
{
    private readonly HttpClient _http;

    public EcGridOsClient(HttpClient http)
    {
        _http = http;
    }

    private static string BuildCallUrl(string serviceUrl, string callName)
    {
        serviceUrl = (serviceUrl ?? string.Empty).Trim();

        if (serviceUrl.EndsWith("/" + callName, StringComparison.OrdinalIgnoreCase))
            return serviceUrl;

        if (serviceUrl.EndsWith(".asmx", StringComparison.OrdinalIgnoreCase))
            return serviceUrl + "/" + callName;

        return serviceUrl.TrimEnd('/') + "/" + callName;
    }

    private async Task<string> PostForXmlAsync(
        string serviceUrl,
        string callName,
        IEnumerable<KeyValuePair<string, string>> formValues,
        CancellationToken ct)
    {
        var url = BuildCallUrl(serviceUrl, callName);
        using var content = new FormUrlEncodedContent(formValues);
        using var response = await _http.PostAsync(url, content, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (LooksLikeHtml(body))
            throw new InvalidOperationException($"{callName} returned HTML instead of ECGrid XML. Check the Service URL or call name.\n\n{Shorten(body)}");

        if (!response.IsSuccessStatusCode)
        {
            var friendly = BuildFriendlyServerError(callName, response, body);
            throw new InvalidOperationException(friendly);
        }

        return body;
    }

    private static string BuildFriendlyServerError(string callName, HttpResponseMessage response, string body)
    {
        var status = $"{callName} failed: {(int)response.StatusCode} {response.ReasonPhrase}.";
        var lower = body ?? string.Empty;

        if (lower.IndexOf("AccessDenied", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            var hint = callName.Equals("MailboxListEx", StringComparison.OrdinalIgnoreCase)
                ? " This session likely does not have permission to list mailboxes for the whole network. Mailbox-level sessions are commonly blocked from that operation."
                : " ECGrid denied this operation for the current SessionID/API key.";

            return status + hint + "\n\n" + Shorten(body);
        }

        var fault = TryReadFaultString(body);
        if (!string.IsNullOrWhiteSpace(fault))
            return status + " " + fault + "\n\n" + Shorten(body);

        return status + "\n\n" + Shorten(body);
    }

    private static string? TryReadFaultString(string body)
    {
        try
        {
            var doc = XDocument.Parse(body);
            var node = doc.Descendants().FirstOrDefault(x => x.Name.LocalName.Equals("faultstring", StringComparison.OrdinalIgnoreCase));
            return node?.Value?.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string Shorten(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        const int max = 4000;
        return text.Length <= max ? text : text.Substring(0, max) + "\n...";
    }

    private static bool LooksLikeHtml(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return false;

        var trimmed = body.TrimStart();
        return trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<SessionInfo> WhoAmIAsync(string serviceUrl, string sessionId, CancellationToken ct)
    {
        var xml = await PostForXmlAsync(
            serviceUrl,
            "WhoAmI",
            new[] { new KeyValuePair<string, string>("SessionID", sessionId ?? string.Empty) },
            ct).ConfigureAwait(false);

        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new InvalidOperationException("WhoAmI returned empty XML.");

        return new SessionInfo
        {
            ECGridOSVersion = ReadStr(root, "ECGridOSVersion"),
            SessionID = ReadStr(root, "SessionID"),
            UserID = ReadInt(root, "UserID"),
            LoginName = ReadStr(root, "LoginName"),
            FirstName = ReadStr(root, "FirstName"),
            LastName = ReadStr(root, "LastName"),
            Company = ReadStr(root, "Company"),
            EMail = ReadStr(root, "EMail"),
            AuthLevel = ReadStr(root, "AuthLevel"),
            NetworkID = ReadInt(root, "NetworkID"),
            MailboxID = ReadInt(root, "MailboxID"),
            TimeOut = (short)ReadInt(root, "TimeOut"),
            OpenSessions = (short)ReadInt(root, "OpenSessions"),
            Ip = ReadStr(root, "ip")
        };
    }

    public async Task<long> ParcelUploadAAsync(string serviceUrl, string sessionId, string fileName, string contentBase64, CancellationToken ct)
    {
        var xml = await PostForXmlAsync(
            serviceUrl,
            "ParcelUploadA",
            new[]
            {
                new KeyValuePair<string, string>("SessionID", sessionId ?? string.Empty),
                new KeyValuePair<string, string>("FileName", fileName ?? string.Empty),
                new KeyValuePair<string, string>("ContentBase64", contentBase64 ?? string.Empty)
            },
            ct).ConfigureAwait(false);

        return ParseLongResult(xml, "ParcelUploadA");
    }

    public async Task<long> ParcelUploadExAAsync(string serviceUrl, string sessionId, int networkId, int mailboxId, string fileName, string contentBase64, CancellationToken ct)
    {
        var xml = await PostForXmlAsync(
            serviceUrl,
            "ParcelUploadExA",
            new[]
            {
                new KeyValuePair<string, string>("SessionID", sessionId ?? string.Empty),
                new KeyValuePair<string, string>("NetworkID", networkId.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("MailboxID", mailboxId.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("FileName", fileName ?? string.Empty),
                new KeyValuePair<string, string>("ContentBase64", contentBase64 ?? string.Empty)
            },
            ct).ConfigureAwait(false);

        return ParseLongResult(xml, "ParcelUploadExA");
    }

    public async Task<List<MailboxRecord>> MailboxListExAsync(string serviceUrl, string sessionId, int networkId, string? nameFilter, CancellationToken ct)
    {
        var xml = await PostForXmlAsync(
            serviceUrl,
            "MailboxListEx",
            new[]
            {
                new KeyValuePair<string, string>("SessionID", sessionId ?? string.Empty),
                new KeyValuePair<string, string>("NetworkID", networkId.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("Name", nameFilter ?? string.Empty)
            },
            ct).ConfigureAwait(false);

        var doc = XDocument.Parse(xml);
        return doc.Descendants().Where(x => x.Name.LocalName == "MailboxIDInfo")
            .Select(x => new MailboxRecord
            {
                MailboxID = ReadInt(x, "MailboxID"),
                NetworkID = ReadInt(x, "NetworkID"),
                Name = ReadStr(x, "Name"),
                Status = ReadStr(x, "Status"),
                Description = ReadStr(x, "Description"),
                OwnerLoginName = ReadStr(x, "LoginName"),
                OwnerEMail = ReadStr(x, "EMail")
            })
            .OrderBy(x => x.Name)
            .ToList();
    }

    public async Task<List<ParcelRecord>> ParcelInBoxExShortAsync(string serviceUrl, string sessionId, int networkId, int mailboxId, CancellationToken ct)
    {
        var xml = await PostForXmlAsync(
            serviceUrl,
            "ParcelInBoxExShort",
            new[]
            {
                new KeyValuePair<string, string>("SessionID", sessionId ?? string.Empty),
                new KeyValuePair<string, string>("NetworkID", networkId.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("MailboxID", mailboxId.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("ECGridIDFrom", "0"),
                new KeyValuePair<string, string>("ECGridIDTo", "0"),
                new KeyValuePair<string, string>("Status", "InBoxReady")
            },
            ct).ConfigureAwait(false);

        var doc = XDocument.Parse(xml);
        return ParseParcelList(doc);
    }

    public async Task<List<ParcelRecord>> ParcelOutBoxErrorExAsync(string serviceUrl, string sessionId, int networkId, int mailboxId, DateTime beginUtc, DateTime endUtc, CancellationToken ct)
    {
        var xml = await PostForXmlAsync(
            serviceUrl,
            "ParcelOutBoxErrorEx",
            new[]
            {
                new KeyValuePair<string, string>("SessionID", sessionId ?? string.Empty),
                new KeyValuePair<string, string>("NetworkID", networkId.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("MailboxID", mailboxId.ToString(CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("BeginDate", beginUtc.ToString("o", CultureInfo.InvariantCulture)),
                new KeyValuePair<string, string>("EndDate", endUtc.ToString("o", CultureInfo.InvariantCulture))
            },
            ct).ConfigureAwait(false);

        var doc = XDocument.Parse(xml);
        return ParseParcelList(doc);
    }

    public async Task<DownloadedFileInfo> ParcelDownloadNoUpdateAAsync(string serviceUrl, string sessionId, long parcelId, CancellationToken ct)
    {
        var xml = await PostForXmlAsync(
            serviceUrl,
            "ParcelDownloadNoUpdateA",
            new[]
            {
                new KeyValuePair<string, string>("SessionID", sessionId ?? string.Empty),
                new KeyValuePair<string, string>("ParcelID", parcelId.ToString(CultureInfo.InvariantCulture))
            },
            ct).ConfigureAwait(false);

        var doc = XDocument.Parse(xml);
        var root = doc.Root ?? throw new InvalidOperationException("ParcelDownloadNoUpdateA returned empty XML.");
        var base64 = ReadStr(root, "ContentBase64String");

        return new DownloadedFileInfo
        {
            ParcelID = ParseLong(ReadStr(root, "ParcelID")),
            FileName = ReadStr(root, "FileName"),
            FileDate = ReadDate(root, "FileDate"),
            Bytes = ReadInt(root, "Bytes"),
            Standard = ReadStr(root, "Standard"),
            ContentBase64String = base64,
            ContentBytes = string.IsNullOrWhiteSpace(base64) ? Array.Empty<byte>() : Convert.FromBase64String(base64),
            RouterArchive = ReadBool(root, "routerArchive")
        };
    }

    public async Task<bool> ParcelDownloadConfirmAsync(string serviceUrl, string sessionId, long parcelId, CancellationToken ct)
    {
        var xml = await PostForXmlAsync(
            serviceUrl,
            "ParcelDownloadConfirm",
            new[]
            {
                new KeyValuePair<string, string>("SessionID", sessionId ?? string.Empty),
                new KeyValuePair<string, string>("ParcelID", parcelId.ToString(CultureInfo.InvariantCulture))
            },
            ct).ConfigureAwait(false);

        var doc = XDocument.Parse(xml);
        var val = doc.Root?.Value?.Trim();
        return string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> ParcelDownloadResetAsync(string serviceUrl, string sessionId, long parcelId, CancellationToken ct)
    {
        var xml = await PostForXmlAsync(
            serviceUrl,
            "ParcelDownloadReset",
            new[]
            {
                new KeyValuePair<string, string>("SessionID", sessionId ?? string.Empty),
                new KeyValuePair<string, string>("ParcelID", parcelId.ToString(CultureInfo.InvariantCulture))
            },
            ct).ConfigureAwait(false);

        var doc = XDocument.Parse(xml);
        var val = doc.Root?.Value?.Trim();
        return string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static List<ParcelRecord> ParseParcelList(XDocument doc)
    {
        return doc.Descendants().Where(x => x.Name.LocalName == "ParcelIDInfo")
            .Select(x => new ParcelRecord
            {
                ParcelID = ParseLong(ReadStr(x, "ParcelID")),
                FileName = ReadStr(x, "FileName"),
                ParcelDate = ReadDate(x, "ParcelDate"),
                StatusCode = ReadStr(x, "StatusCode"),
                StatusMessage = ReadStr(x, "StatusMessage"),
                Valid = ReadStr(x, "Valid"),
                Direction = ReadStr(x, "Direction"),
                MailbagControlID = ReadStr(x, "MailbagControlID"),
                NetworkIDFrom = TryReadInt(x, "NetworkIDFrom"),
                NetworkNameFrom = ReadStr(x, "NetworkNameFrom"),
                MailboxIDFrom = TryReadInt(x, "MailboxIDFrom"),
                MailboxNameFrom = ReadStr(x, "MailboxNameFrom"),
                NetworkIDTo = TryReadInt(x, "NetworkIDTo"),
                NetworkNameTo = ReadStr(x, "NetworkNameTo"),
                MailboxIDTo = TryReadInt(x, "MailboxIDTo"),
                MailboxNameTo = ReadStr(x, "MailboxNameTo"),
                ActualBytes = TryReadInt(x, "ActualBytes"),
                Acknowledgment = ReadStr(x, "Acknowledgment")
            })
            .OrderByDescending(x => x.ParcelDate ?? DateTime.MinValue)
            .ToList();
    }

    private static long ParseLongResult(string xml, string callName)
    {
        var doc = XDocument.Parse(xml);
        var val = doc.Root?.Value?.Trim();
        if (!long.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            throw new InvalidOperationException($"{callName} did not return a valid ParcelID. Raw: {xml}");
        return parsed;
    }

    private static string ReadStr(XElement parent, string elementName)
    {
        var el = parent.DescendantsAndSelf().FirstOrDefault(x =>
            x.Name.LocalName.Equals(elementName, StringComparison.OrdinalIgnoreCase));
        return el?.Value?.Trim() ?? string.Empty;
    }

    private static int ReadInt(XElement parent, string elementName)
    {
        var s = ReadStr(parent, elementName);
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static int? TryReadInt(XElement parent, string elementName)
    {
        var s = ReadStr(parent, elementName);
        return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static long ParseLong(string? s)
    {
        return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0L;
    }

    private static DateTime? ReadDate(XElement parent, string elementName)
    {
        var s = ReadStr(parent, elementName);
        return DateTime.TryParse(
            s,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var v)
            ? v
            : null;
    }

    private static bool ReadBool(XElement parent, string elementName)
    {
        var s = ReadStr(parent, elementName);
        return bool.TryParse(s, out var v) && v;
    }
}
