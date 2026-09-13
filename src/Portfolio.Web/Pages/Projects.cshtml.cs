using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Portfolio.Web.Configuration;
using Portfolio.Web.Content.Projects;
using Portfolio.Web.Models;
using Portfolio.Web.Services;

namespace Portfolio.Web.Pages;

/// <summary>
/// Backs Projects page w/ validated catalog records + page-specific metadata
/// </summary>
/// <param name="options">Provides the public origin used for canonical URLs.</param>
/// <param name="projectCatalog">Provides validated project records for server-side rendering.</param>
public sealed class ProjectsModel(
    IOptions<PortfolioOptions> options,
    IProjectCatalog projectCatalog) : PortfolioPageModel(options)
{
    /// <summary>Gets the ordered projects rendered by the view.</summary>
    public IReadOnlyList<ProjectRecord> Projects { get; private set; } = [];

    /// <summary>Gets the requesting client's platform, detected once per request.</summary>
    public ClientPlatform ClientPlatform { get; private set; }

    /// <summary>Returns the downloads offered to this request's platform, so the view never inspects the request.</summary>
    public IReadOnlyList<ProjectDownload> GetDownloads(ProjectRecord project) =>
        project.Downloads.Where(download => download.IsAvailableOn(ClientPlatform)).ToArray();

    /// <summary>Loads the project catalog and prepares metadata for server-side rendering.</summary>
    public void OnGet()
    {
        SetMetadata(new PageMetadata(
            NavigationKey: "projects",
            DocumentTitle: "Projects | brandonthomas.net",
            Description: "Projects | Brandon Thomas",
            CanonicalPath: "/projects"));
        Projects = projectCatalog.Projects;

        // The download list varies by platform. Portfolio HTML is never shared-cached, but Vary keeps any cache honest.
        ClientPlatform = ClientPlatformDetector.Detect(Request);
        Response.Headers.Append(HeaderNames.Vary, ClientPlatformDetector.VaryHeaders);
    }
}
