using Microsoft.Extensions.Options;
using Portfolio.Web.Configuration;
using Portfolio.Web.Models;

namespace Portfolio.Web.Pages;

/// <summary>
/// Backs Info page + supplies metadata required by PortfolioPageModel
/// </summary>
/// <param name="options">Provides the public origin used for canonical URLs</param>
public sealed class IndexModel(IOptions<PortfolioOptions> options) : PortfolioPageModel(options)
{
    /// <summary>
    /// Prepares Info page metadata for server-side rendering
    /// </summary>
    public void OnGet()
    {
        SetMetadata(new PageMetadata(
            NavigationKey: "about",
            DocumentTitle: "brandonthomas.net",
            Description: "Brandon Thomas",
            CanonicalPath: "/"));
    }
}
