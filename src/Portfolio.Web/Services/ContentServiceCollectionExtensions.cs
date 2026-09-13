namespace Portfolio.Web.Services;

/// <summary>
/// Registers content catalogs, validation, and refresh services w/ dependency injection
/// </summary>
public static class ContentServiceCollectionExtensions
{
    /// <summary>Loads projects at startup and registers the externally reloadable photo catalog.</summary>
    public static IServiceCollection AddPortfolioContent(
        this IServiceCollection services,
        IWebHostEnvironment environment)
    {
        var contentDirectory = Path.Combine(environment.ContentRootPath, "Content");

        services.AddSingleton<IProjectCatalog>(_ =>
            JsonProjectCatalog.Load(Path.Combine(contentDirectory, "projects.v1.json"), environment.WebRootPath));
        services.AddSingleton<ReloadingPhotoCatalog>();
        services.AddSingleton<IPhotoCatalog>(provider =>
            provider.GetRequiredService<ReloadingPhotoCatalog>());
        services.AddHostedService(provider =>
            provider.GetRequiredService<ReloadingPhotoCatalog>());
        services.AddHostedService<CatalogStartupService>();

        return services;
    }
}
