using Microsoft.Extensions.Diagnostics.HealthChecks;
using Portfolio.Web.Services;

namespace Portfolio.Web.Health;

/// <summary>Reports readiness only after the process has loaded one valid photo manifest.</summary>
public sealed class PhotoCatalogHealthCheck(ReloadingPhotoCatalog catalog) : IHealthCheck
{
    /// <summary>Returns the current photo-catalog readiness state without reading the manifest.</summary>
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var result = catalog.HasValidCatalog
            ? HealthCheckResult.Healthy($"Photo revision {catalog.ContentRevision} is loaded.")
            : HealthCheckResult.Unhealthy("No valid photo manifest has been loaded.");

        return Task.FromResult(result);
    }
}
