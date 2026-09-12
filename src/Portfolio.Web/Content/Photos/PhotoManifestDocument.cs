namespace Portfolio.Web.Content.Photos;

/// <summary>
/// Versioned photo manifest consumed by the photo catalog
/// </summary>
public sealed record PhotoManifestDocument
{
    /// <summary>Gets the schema version required for safe deserialization changes.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>Gets the publisher-provided identifier for this content revision.</summary>
    public string ContentRevision { get; init; } = string.Empty;

    /// <summary>Gets when this manifest revision was published.</summary>
    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>Gets photo records in publisher-defined display order.</summary>
    public IReadOnlyList<PhotoRecord> Entries { get; init; } = [];
}

/// <summary>
/// 1 published photo and its responsive image variants
/// </summary>
public sealed record PhotoRecord
{
    /// <summary>Gets the stable URL-safe photo identifier.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets when the photo was published.</summary>
    public DateTimeOffset PublishedAt { get; init; }

    /// <summary>Gets the alternative text supplied by the content source.</summary>
    public string AltText { get; init; } = string.Empty;

    /// <summary>Gets the optional caption supplied by the content source.</summary>
    public string? Caption { get; init; }

    /// <summary>Gets the available responsive image files for this photo.</summary>
    public IReadOnlyList<PhotoVariant> Variants { get; init; } = [];
}

/// <summary>
/// Represents 1 sized/encoded file for a photo
/// </summary>
public sealed record PhotoVariant
{
    /// <summary>Gets the content-hashed public path used by image markup.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>Gets the intrinsic pixel width used to prevent layout shift.</summary>
    public int Width { get; init; }

    /// <summary>Gets the intrinsic pixel height used to prevent layout shift.</summary>
    public int Height { get; init; }

    /// <summary>Gets the image media type used to validate supported formats.</summary>
    public string MediaType { get; init; } = string.Empty;
}
