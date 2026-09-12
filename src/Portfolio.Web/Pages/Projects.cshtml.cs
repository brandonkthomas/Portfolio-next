using Microsoft.Extensions.Options;
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

    /// <summary>Loads the project catalog and prepares metadata for server-side rendering.</summary>
    public void OnGet()
    {
        SetMetadata(new PageMetadata(
            NavigationKey: "projects",
            DocumentTitle: "Projects | brandonthomas.net",
            Description: "Brandon Thomas",
            CanonicalPath: "/projects"));
        Projects = projectCatalog.Projects;
    }
}
