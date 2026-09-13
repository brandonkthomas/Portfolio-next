namespace Portfolio.Web.Models;

/// <summary>
/// Operating system reported by the requesting browser; used only to choose platform-specific downloads
/// </summary>
public enum ClientPlatform
{
    Unknown,
    Windows,
    MacOS,
    Linux
}

/// <summary>
/// Detects the client platform from the default low-entropy client hint, falling back to the user agent
/// </summary>
public static class ClientPlatformDetector
{
    /// <summary>Headers whose values change the platform detected for a response.</summary>
    public const string VaryHeaders = "User-Agent, Sec-CH-UA-Platform";

    /// <summary>Detects the platform for one request.</summary>
    public static ClientPlatform Detect(HttpRequest request)
    {
        // Chromium sends Sec-CH-UA-Platform by default as a quoted token and it survives user-agent reduction.
        var hint = request.Headers["Sec-CH-UA-Platform"].ToString().Trim().Trim('"');
        if (hint.Length > 0)
        {
            return hint switch
            {
                "Windows" => ClientPlatform.Windows,
                "macOS" => ClientPlatform.MacOS,
                "Linux" => ClientPlatform.Linux,
                _ => ClientPlatform.Unknown
            };
        }

        // Safari and Firefox send no client hints. Mobile tokens are checked first because Android reports Linux.
        // iPadOS in desktop mode reports Macintosh and cannot be distinguished from macOS server-side.
        var userAgent = request.Headers.UserAgent.ToString();
        if (userAgent.Contains("Windows NT", StringComparison.Ordinal))
        {
            return ClientPlatform.Windows;
        }

        if (userAgent.Contains("Android", StringComparison.Ordinal)
            || userAgent.Contains("iPhone", StringComparison.Ordinal)
            || userAgent.Contains("iPad", StringComparison.Ordinal))
        {
            return ClientPlatform.Unknown;
        }

        if (userAgent.Contains("Macintosh", StringComparison.Ordinal))
        {
            return ClientPlatform.MacOS;
        }

        return userAgent.Contains("Linux", StringComparison.Ordinal) ? ClientPlatform.Linux : ClientPlatform.Unknown;
    }
}
