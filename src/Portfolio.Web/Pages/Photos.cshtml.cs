using Microsoft.Extensions.Options;
using Portfolio.Web.Configuration;
using Portfolio.Web.Content.Photos;
using Portfolio.Web.Models;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages;

/// <summary>
/// Backs Photos page w/ validated catalog records + page-specific metadata
/// </summary>
/// <param name="options">Provides the public origin used for canonical URLs.</param>
/// <param name="photoCatalog">Provides validated photo records for server-side rendering.</param>
public sealed class PhotosModel(
    IOptions<PortfolioOptions> options,
    IPhotoCatalog photoCatalog) : PortfolioPageModel(options)
{
    /// <summary>Gets all photo records rendered by the view.</summary>
    public IReadOnlyList<PhotoRecord> Photos { get; private set; } = [];

    /// <summary>Loads all photos and prepares metadata for server-side rendering.</summary>
    public void OnGet()
    {
        Photos = photoCatalog.Photos;
        SetMetadata(new PageMetadata(
            NavigationKey: "photos",
            DocumentTitle: "Photos | brandonthomas.net",
            Description: "Photos | Brandon Thomas",
            CanonicalPath: "/photos"));
    }

    /// <summary>Formats one media type's variants as a width-described srcset candidate list.</summary>
    public static string FormatSrcset(IEnumerable<PhotoVariant> variants) =>
        string.Join(", ", variants.Select(variant => $"{variant.Url} {variant.Width}w"));
}
