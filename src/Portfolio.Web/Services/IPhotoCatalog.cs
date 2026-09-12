using Portfolio.Web.Content.Photos;

namespace Portfolio.Web.Services;

/// <summary>
/// Exposes validated photo manifests w/o coupling pages to their storage format
/// </summary>
public interface IPhotoCatalog
{
    /// <summary>Gets the publisher-provided revision used to identify manifest content.</summary>
    string ContentRevision { get; }

    /// <summary>Gets when the manifest was published.</summary>
    DateTimeOffset PublishedAt { get; }

    /// <summary>Gets photos in manifest order.</summary>
    IReadOnlyList<PhotoRecord> Photos { get; }
}
