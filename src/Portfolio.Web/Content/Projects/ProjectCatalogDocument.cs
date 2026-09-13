using Portfolio.Web.Models;

namespace Portfolio.Web.Content.Projects;

/// <summary>
/// Versioned project JSON document consumed by the project catalog
/// </summary>
public sealed record ProjectCatalogDocument
{
    /// <summary>Gets the schema version required for safe deserialization changes.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>Gets the project records validated and ordered by the catalog service.</summary>
    public IReadOnlyList<ProjectRecord> Projects { get; init; } = [];
}

/// <summary>
/// 1 project's content + optional destinations
/// </summary>
public sealed record ProjectRecord
{
    /// <summary>Gets the stable URL-safe project identifier.</summary>
    public string Slug { get; init; } = string.Empty;

    /// <summary>Gets the project display title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the optional decorative icon rendered before the title.</summary>
    public ProjectIcon? Icon { get; init; }

    /// <summary>
    /// Gets the project display caption.
    /// </summary>
    public string? Meta { get; init; } = string.Empty;

    /// <summary>Gets the project summary supplied by the content source.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Gets the explicit display order.</summary>
    public int Order { get; init; }

    /// <summary>Gets the project technology or category labels.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Gets the optional source-code URL.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>Gets an optional live URL, including a site-relative demo path.</summary>
    public string? LiveUrl { get; init; }

    /// <summary>Gets optional downloadable project artifacts.</summary>
    public IReadOnlyList<ProjectDownload> Downloads { get; init; } = [];
}

/// <summary>
/// Decorative project mark served from the application's static assets
/// </summary>
public sealed record ProjectIcon
{
    /// <summary>Gets the site-relative static asset path, such as /assets/webp/projects/webamp.webp.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Gets whether a single-color mark inverts with the dark theme like other monochrome site icons.</summary>
    public bool InvertInDarkTheme { get; init; }
}

/// <summary>
/// 1 labeled download associated with a project
/// </summary>
public sealed record ProjectDownload
{
    /// <summary>Gets the platform or artifact label shown with the link.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>Gets the absolute download URL.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Gets the optional platform (windows, macos, linux); a download without one is offered to every client.</summary>
    public string? Platform { get; init; }

    /// <summary>Gets the platform tokens accepted in catalog JSON.</summary>
    public static IReadOnlySet<string> SupportedPlatforms { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "windows",
        "macos",
        "linux"
    };

    /// <summary>Returns whether this download should be offered to a client on the given platform.</summary>
    public bool IsAvailableOn(ClientPlatform platform) => Platform switch
    {
        null => true,
        "windows" => platform == ClientPlatform.Windows,
        "macos" => platform == ClientPlatform.MacOS,
        "linux" => platform == ClientPlatform.Linux,
        _ => false
    };
}
