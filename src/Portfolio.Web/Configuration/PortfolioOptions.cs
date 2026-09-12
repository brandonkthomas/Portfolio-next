namespace Portfolio.Web.Configuration;

/// <summary>
/// Configuration options
/// </summary>
public sealed class PortfolioOptions
{
    public const string SectionName = "Portfolio";

    public string PublicBaseUrl { get; init; } = string.Empty;

    public static bool IsValidPublicBaseUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var usesHttp = uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        var hasRootPath = uri.AbsolutePath == "/";

        return usesHttp
            && hasRootPath
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }
}
