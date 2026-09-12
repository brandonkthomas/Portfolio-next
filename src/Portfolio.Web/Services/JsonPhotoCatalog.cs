using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Portfolio.Web.Content;
using Portfolio.Web.Content.Photos;

namespace Portfolio.Web.Services;

/// <summary>
/// Loads/validates photo manifest + its responsive image variants
/// </summary>
public sealed partial class JsonPhotoCatalog : IPhotoCatalog
{
    private static readonly HashSet<string> SupportedMediaTypes =
        new(["image/avif", "image/jpeg", "image/webp"], StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Creates an immutable catalog and orders variants by format then width.</summary>
    private JsonPhotoCatalog(PhotoManifestDocument document)
    {
        ContentRevision = document.ContentRevision;
        PublishedAt = document.PublishedAt;
        Photos = document.Entries
            .Select(entry => entry with
            {
                Variants = entry.Variants
                    .OrderBy(variant => variant.MediaType, StringComparer.Ordinal)
                    .ThenBy(variant => variant.Width)
                    .ToArray()
            })
            .ToArray();
    }

    /// <summary>Gets the publisher-provided revision used to identify manifest content.</summary>
    public string ContentRevision { get; }

    /// <summary>Gets when the manifest was published.</summary>
    public DateTimeOffset PublishedAt { get; }

    /// <summary>Gets validated photos in manifest order.</summary>
    public IReadOnlyList<PhotoRecord> Photos { get; }

    /// <summary>Deserializes and validates a manifest so malformed content fails startup.</summary>
    public static JsonPhotoCatalog Load(string path, string webRootPath)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<PhotoManifestDocument>(stream, JsonOptions)
            ?? throw new InvalidDataException($"Photo manifest '{path}' is empty.");

        var errors = Validate(document, webRootPath);
        if (errors.Count > 0)
        {
            throw new CatalogValidationException("Photo manifest", errors);
        }

        return new JsonPhotoCatalog(document);
    }

    /// <summary>Collects manifest and photo errors so one failure reports all content problems.</summary>
    private static List<string> Validate(PhotoManifestDocument document, string webRootPath)
    {
        var errors = new List<string>();

        if (document.SchemaVersion != 1)
        {
            errors.Add($"Unsupported schemaVersion '{document.SchemaVersion}'. Expected 1.");
        }

        if (string.IsNullOrWhiteSpace(document.ContentRevision) || document.ContentRevision.Length > 100)
        {
            errors.Add("contentRevision must contain 1 to 100 characters.");
        }

        if (document.PublishedAt == default)
        {
            errors.Add("publishedAt is required.");
        }

        var duplicateIds = document.Entries
            .GroupBy(photo => photo.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var id in duplicateIds)
        {
            errors.Add($"Duplicate photo id '{id}'.");
        }

        var duplicateUrls = document.Entries
            .SelectMany(photo => photo.Variants)
            .GroupBy(variant => variant.Url, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var url in duplicateUrls)
        {
            errors.Add($"Duplicate photo variant URL '{url}'.");
        }

        foreach (var photo in document.Entries)
        {
            ValidatePhoto(photo, webRootPath, errors);
        }

        return errors;
    }

    /// <summary>Validates one photo and its variants before the record can be rendered.</summary>
    private static void ValidatePhoto(PhotoRecord photo, string webRootPath, ICollection<string> errors)
    {
        var label = string.IsNullOrWhiteSpace(photo.Id) ? "<missing id>" : photo.Id;

        if (!IdPattern().IsMatch(photo.Id))
        {
            errors.Add($"Photo '{label}' has an invalid id.");
        }

        if (photo.PublishedAt == default)
        {
            errors.Add($"Photo '{label}' publishedAt is required.");
        }

        if (string.IsNullOrWhiteSpace(photo.AltText) || photo.AltText.Length > 300)
        {
            errors.Add($"Photo '{label}' altText must contain 1 to 300 characters.");
        }

        if (photo.Caption?.Length > 500)
        {
            errors.Add($"Photo '{label}' caption cannot exceed 500 characters.");
        }

        if (photo.Variants.Count == 0)
        {
            errors.Add($"Photo '{label}' must define at least one variant.");
            return;
        }

        if (photo.Variants
            .Select(variant => (variant.MediaType.ToUpperInvariant(), variant.Width))
            .Distinct()
            .Count() != photo.Variants.Count)
        {
            errors.Add($"Photo '{label}' contains duplicate format/width variants.");
        }

        double? expectedAspectRatio = null;
        foreach (var variant in photo.Variants)
        {
            if (variant.Width <= 0 || variant.Height <= 0)
            {
                errors.Add($"Photo '{label}' variant dimensions must be positive.");
                continue;
            }

            var aspectRatio = variant.Width / (double)variant.Height;
            expectedAspectRatio ??= aspectRatio;
            if (Math.Abs(aspectRatio - expectedAspectRatio.Value) > 0.01)
            {
                errors.Add($"Photo '{label}' variants must use a consistent aspect ratio.");
            }

            if (!SupportedMediaTypes.Contains(variant.MediaType))
            {
                errors.Add($"Photo '{label}' variant mediaType '{variant.MediaType}' is unsupported.");
            }

            ValidateVariantUrl(label, variant.Url, variant.Width, webRootPath, errors);
        }
    }

    /// <summary>Restricts variant URLs and verifies fixture files remain inside the web root.</summary>
    private static void ValidateVariantUrl(
        string photoId,
        string url,
        int expectedWidth,
        string webRootPath,
        ICollection<string> errors)
    {
        var isAllowedRoot = url.StartsWith("/fixtures/photos/", StringComparison.Ordinal)
            || url.StartsWith("/media/photos/", StringComparison.Ordinal);
        var hasUnsafeSyntax = url.Contains("..", StringComparison.Ordinal)
            || url.Contains('\\')
            || url.Contains('?')
            || url.Contains('#');

        var filename = Path.GetFileName(url);
        var hashedPathMatch = HashedImagePathPattern().Match(url);
        var hasExpectedWidth = hashedPathMatch.Success
            && int.TryParse(hashedPathMatch.Groups["width"].Value, out var filenameWidth)
            && filenameWidth == expectedWidth;

        if (!isAllowedRoot || hasUnsafeSyntax || string.IsNullOrEmpty(filename) || !hasExpectedWidth)
        {
            errors.Add($"Photo '{photoId}' variant URL '{url}' is not an allowed content-hashed photo path.");
            return;
        }

        if (!url.StartsWith("/fixtures/photos/", StringComparison.Ordinal))
        {
            return;
        }

        var relativePath = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var expectedRoot = Path.GetFullPath(webRootPath) + Path.DirectorySeparatorChar;
        var filePath = Path.GetFullPath(Path.Combine(webRootPath, relativePath));

        if (!filePath.StartsWith(expectedRoot, StringComparison.Ordinal) || !File.Exists(filePath))
        {
            errors.Add($"Photo '{photoId}' fixture file '{url}' is missing or outside the web root.");
        }
    }

    /// <summary>Provides the compiled pattern used to enforce stable URL-safe photo identifiers.</summary>
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    /// <summary>Provides the compiled pattern used to require content-hashed image filenames.</summary>
    [GeneratedRegex(@"\.[0-9a-f]{12}\.(?<width>\d+)\.(?:avif|jpe?g|webp)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HashedImagePathPattern();
}
