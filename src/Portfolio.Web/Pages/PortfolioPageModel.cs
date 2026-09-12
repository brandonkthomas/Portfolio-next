using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using Portfolio.Web.Configuration;
using Portfolio.Web.Models;

namespace Portfolio.Web.Pages;

/// <summary>
/// Requires portfolio pages to supply metadata + builds canonical URLs from configured public origin
/// </summary>
/// <param name="options">Provides the public origin used for canonical URLs.</param>
public abstract class PortfolioPageModel(IOptions<PortfolioOptions> options) : PageModel
{
    private PageMetadata? _metadata;
    private readonly Uri _publicBaseUri = new(options.Value.PublicBaseUrl);

    /// <summary>Gets the metadata required by the shared layouts.</summary>
    public PageMetadata Metadata => _metadata
        ?? throw new InvalidOperationException("Page metadata must be set before rendering.");

    /// <summary>Gets the absolute canonical URL derived from the configured public origin.</summary>
    public Uri CanonicalUrl => new(_publicBaseUri, Metadata.CanonicalPath);

    /// <summary>Stores page metadata before Razor renders the shared layouts.</summary>
    protected void SetMetadata(PageMetadata value)
    {
        _metadata = value;
    }
}
