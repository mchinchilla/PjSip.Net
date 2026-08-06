using System.Text;

namespace PjSip.Net.Internal;

/// <summary>
/// Strips secrets from a raw SIP message before it is written to a log.
///
/// Diagnosing an SDP negotiation failure needs to show WHICH m-lines carry an
/// <c>a=crypto</c> attribute and which suite each one names — that is exactly what
/// tells apart "the peer offered plain RTP/AVP" from "the peer offered RTP/SAVP but
/// forgot the key". It never needs the SRTP master key itself, and writing that key
/// to a plaintext log file would hand an attacker the media it protects.
/// </summary>
internal static class SipMessageRedactor
{
    private const string Placeholder = "<redacted>";
    private const string InlineToken = "inline:";

    /// <summary>Header values that carry a digest response or shared secret.</summary>
    private static readonly string[] SecretHeaders =
    [
        "authorization",
        "proxy-authorization"
    ];

    /// <summary>
    /// Returns <paramref name="message"/> with SRTP key material and auth credentials
    /// replaced by a placeholder. Line structure (including CRLF) and every other header
    /// and SDP attribute are preserved verbatim, so the result still reads as SIP.
    /// </summary>
    public static string Redact(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        // Split on '\n' only, then handle the '\r' per line, so a message using bare
        // LF (some test fixtures) round-trips just as well as a wire-format CRLF one.
        var lines = message.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var hasCr = line.EndsWith('\r');
            if (hasCr) line = line[..^1];

            lines[i] = hasCr ? RedactLine(line) + "\r" : RedactLine(line);
        }

        return string.Join('\n', lines);
    }

    private static string RedactLine(string line)
    {
        // SDES: a=crypto:<tag> <suite> inline:<key>|<lifetime>|<mki> [session-params]
        // Keep the tag and the suite — those are the whole diagnostic value — drop the key.
        if (line.StartsWith("a=crypto:", StringComparison.OrdinalIgnoreCase))
            return RedactInlineKeys(line);

        // MIKEY carries the key inside the attribute value itself; nothing worth keeping.
        if (line.StartsWith("a=key-mgmt:", StringComparison.OrdinalIgnoreCase))
            return "a=key-mgmt:" + Placeholder;

        var colon = line.IndexOf(':');
        if (colon > 0)
        {
            var name = line.AsSpan(0, colon).Trim();
            foreach (var secret in SecretHeaders)
            {
                if (name.Equals(secret, StringComparison.OrdinalIgnoreCase))
                    return string.Concat(line.AsSpan(0, colon + 1), " ", Placeholder);
            }
        }

        return line;
    }

    /// <summary>
    /// Replaces every <c>inline:</c> key parameter in a crypto attribute. A single
    /// attribute may list more than one (RFC 4568 allows repeated key-params), so this
    /// walks the whole line rather than stopping at the first match.
    /// </summary>
    private static string RedactInlineKeys(string line)
    {
        var sb = new StringBuilder(line.Length);
        var pos = 0;

        while (true)
        {
            var idx = line.IndexOf(InlineToken, pos, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) break;

            var valueStart = idx + InlineToken.Length;
            // Key params run to the next space (session-params follow) or end of line.
            var valueEnd = line.IndexOf(' ', valueStart);
            if (valueEnd < 0) valueEnd = line.Length;

            sb.Append(line, pos, valueStart - pos).Append(Placeholder);
            pos = valueEnd;
        }

        // Nothing matched: hand back the original instance rather than a copy.
        if (pos == 0) return line;

        sb.Append(line, pos, line.Length - pos);
        return sb.ToString();
    }
}
