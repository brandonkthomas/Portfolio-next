namespace Portfolio.Web.Configuration;

/// <summary>Configures the external photo manifest and its bounded refresh behavior.</summary>
public sealed class PhotoCatalogOptions
{
    public const string SectionName = "PhotoCatalog";

    /// <summary>Gets the manifest path, resolved relative to the application content root when needed.</summary>
    public string ManifestPath { get; init; } = "Content/photos.v1.json";

    /// <summary>Gets the fallback interval used to recover from missed filesystem notifications.</summary>
    public TimeSpan ReconciliationInterval { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets the delay used to combine a burst of filesystem notifications into one reload.</summary>
    public TimeSpan ChangeDebounce { get; init; } = TimeSpan.FromMilliseconds(250);
}
