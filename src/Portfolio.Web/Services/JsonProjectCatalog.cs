using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Portfolio.Web.Content;
using Portfolio.Web.Content.Projects;

namespace Portfolio.Web.Services;

/// <summary>
/// Loads/validates/orders project records from versioned JSON catalog
/// </summary>
public sealed partial class JsonProjectCatalog : IProjectCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Creates the immutable catalog after loading and validation succeed.</summary>
    private JsonProjectCatalog(IReadOnlyList<ProjectRecord> projects)
    {
        Projects = projects;
    }

    /// <summary>Gets validated projects in display order.</summary>
    public IReadOnlyList<ProjectRecord> Projects { get; }

    /// <summary>Deserializes and validates a project catalog so malformed content fails startup.</summary>
    /// <param name="path">The project catalog JSON file.</param>
    /// <param name="webRootPath">The static web root used to verify that referenced icon files exist.</param>
    public static JsonProjectCatalog Load(string path, string webRootPath)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<ProjectCatalogDocument>(stream, JsonOptions)
            ?? throw new InvalidDataException($"Project catalog '{path}' is empty.");

        var errors = Validate(document, webRootPath);
        if (errors.Count > 0)
        {
            throw new CatalogValidationException("Project catalog", errors);
        }

        var projects = document.Projects
            .OrderBy(project => project.Order)
            .ThenBy(project => project.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new JsonProjectCatalog(projects);
    }

    /// <summary>Collects schema and record errors so one failure reports all content problems.</summary>
    private static List<string> Validate(ProjectCatalogDocument document, string webRootPath)
    {
        var errors = new List<string>();

        if (document.SchemaVersion != 1)
        {
            errors.Add($"Unsupported schemaVersion '{document.SchemaVersion}'. Expected 1.");
        }

        var duplicateSlugs = document.Projects
            .GroupBy(project => project.Slug, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var slug in duplicateSlugs)
        {
            errors.Add($"Duplicate project slug '{slug}'.");
        }

        foreach (var project in document.Projects)
        {
            var label = string.IsNullOrWhiteSpace(project.Slug) ? "<missing slug>" : project.Slug;

            if (!SlugPattern().IsMatch(project.Slug))
            {
                errors.Add($"Project '{label}' has an invalid slug.");
            }

            ValidateText(project.Title, 100, $"Project '{label}' title", errors);
            ValidateText(project.Summary, 300, $"Project '{label}' summary", errors);

            if (project.Order < 0)
            {
                errors.Add($"Project '{label}' order must be non-negative.");
            }

            if (project.Tags.Count == 0 || project.Tags.Any(tag => string.IsNullOrWhiteSpace(tag)))
            {
                errors.Add($"Project '{label}' must have non-empty tags.");
            }

            if (project.Tags.Distinct(StringComparer.OrdinalIgnoreCase).Count() != project.Tags.Count)
            {
                errors.Add($"Project '{label}' contains duplicate tags.");
            }

            ValidateIcon(project.Icon, $"Project '{label}' icon", webRootPath, errors);
            ValidateOptionalHttpUrl(project.SourceUrl, $"Project '{label}' sourceUrl", errors);
            ValidateOptionalLiveUrl(project.LiveUrl, $"Project '{label}' liveUrl", errors);

            foreach (var download in project.Downloads)
            {
                ValidateText(download.Label, 80, $"Project '{label}' download label", errors);
                ValidateOptionalHttpUrl(download.Url, $"Project '{label}' download URL", errors, required: true);

                if (download.Platform is not null && !ProjectDownload.SupportedPlatforms.Contains(download.Platform))
                {
                    errors.Add($"Project '{label}' download platform '{download.Platform}' must be windows, macos, or linux.");
                }
            }
        }

        return errors;
    }

    /// <summary>Requires user-facing catalog text to be present and within its length limit.</summary>
    private static void ValidateText(string value, int maximumLength, string label, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
        {
            errors.Add($"{label} must contain 1 to {maximumLength} characters.");
        }
    }

    /// <summary>Restricts icons to existing image assets in the web root so markup never references a missing file.</summary>
    private static void ValidateIcon(ProjectIcon? icon, string label, string webRootPath, ICollection<string> errors)
    {
        if (icon is null)
        {
            return;
        }

        if (!IconPathPattern().IsMatch(icon.Path))
        {
            errors.Add($"{label} path must be a PNG, SVG, or WebP file under /assets/.");
            return;
        }

        var expectedRoot = Path.GetFullPath(webRootPath) + Path.DirectorySeparatorChar;
        var filePath = Path.GetFullPath(Path.Combine(
            webRootPath,
            icon.Path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));

        if (!filePath.StartsWith(expectedRoot, StringComparison.Ordinal) || !File.Exists(filePath))
        {
            errors.Add($"{label} file '{icon.Path}' is missing or outside the web root.");
        }
    }

    /// <summary>Accepts only absolute HTTP(S) links so catalog URLs are safe to render.</summary>
    private static void ValidateOptionalHttpUrl(
        string? value,
        string label,
        ICollection<string> errors,
        bool required = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                errors.Add($"{label} is required.");
            }

            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add($"{label} must be an absolute HTTP(S) URL.");
        }
    }

    /// <summary>Allows a public HTTP(S) destination or a simple path on this site.</summary>
    private static void ValidateOptionalLiveUrl(string? value, string label, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (value.StartsWith('/') && LocalDemoPathPattern().IsMatch(value))
        {
            return;
        }

        ValidateOptionalHttpUrl(value, label, errors);
    }

    /// <summary>Provides the compiled pattern for lowercase icon asset paths without dot segments or queries.</summary>
    [GeneratedRegex("^/assets/(?:[a-z0-9]+(?:-[a-z0-9]+)*/)+[a-z0-9]+(?:-[a-z0-9]+)*\\.(?:png|svg|webp)$", RegexOptions.CultureInvariant)]
    private static partial Regex IconPathPattern();

    /// <summary>Provides the compiled pattern used to enforce stable URL-safe project slugs.</summary>
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    // No protocol-relative URL, query, fragment, backslash, dot segment, or encoded redirect.
    [GeneratedRegex("^/[a-z0-9]+(?:[-_][a-z0-9]+)*(?:/[a-z0-9]+(?:[-_][a-z0-9]+)*)*/?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LocalDemoPathPattern();
}
