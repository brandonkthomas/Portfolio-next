namespace Portfolio.Web.Services;

/// <summary>
/// Forces project validation at startup + records the initial content state
/// </summary>
/// <param name="projectCatalog">Provides the validated project catalog.</param>
/// <param name="photoCatalog">Provides the validated photo catalog.</param>
/// <param name="logger">Records the loaded catalog counts and revision.</param>
public sealed class CatalogStartupService(
    IProjectCatalog projectCatalog,
    IPhotoCatalog photoCatalog,
    ILogger<CatalogStartupService> logger) : IHostedService
{
    /// <summary>Logs catalog details after dependency injection has completed startup loading.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(photoCatalog.ContentRevision))
        {
            logger.LogWarning(
                "Loaded {ProjectCount} projects but no valid photo revision",
                projectCatalog.Projects.Count);
        }
        else
        {
            logger.LogInformation(
                "Loaded {ProjectCount} projects and photo revision {PhotoRevision} with {PhotoCount} photos",
                projectCatalog.Projects.Count,
                photoCatalog.ContentRevision,
                photoCatalog.Photos.Count);
        }

        return Task.CompletedTask;
    }

    /// <summary>Completes immediately because the in-memory catalogs require no shutdown work.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
