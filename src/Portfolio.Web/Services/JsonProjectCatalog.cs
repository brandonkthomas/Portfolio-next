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
    public static JsonProjectCatalog Load(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<ProjectCatalogDocument>(stream, JsonOptions)
            ?? throw new InvalidDataException($"Project catalog '{path}' is empty.");

        var errors = Validate(document);
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
    private static List<string> Validate(ProjectCatalogDocument document)
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

            ValidateOptionalHttpUrl(project.SourceUrl, $"Project '{label}' sourceUrl", errors);
            ValidateOptionalHttpUrl(project.LiveUrl, $"Project '{label}' liveUrl", errors);

            foreach (var download in project.Downloads)
            {
                ValidateText(download.Label, 80, $"Project '{label}' download label", errors);
                ValidateOptionalHttpUrl(download.Url, $"Project '{label}' download URL", errors, required: true);
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

    /// <summary>Provides the compiled pattern used to enforce stable URL-safe project slugs.</summary>
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
