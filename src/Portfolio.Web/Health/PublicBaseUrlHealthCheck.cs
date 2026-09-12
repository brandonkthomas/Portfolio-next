using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Portfolio.Web.Configuration;

namespace Portfolio.Web.Health;

/// <summary>
/// Health check for base URL -- primarily used by CloudFlare
/// </summary>
/// <param name="options"></param>
public sealed class PublicBaseUrlHealthCheck(IOptions<PortfolioOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var isValid = PortfolioOptions.IsValidPublicBaseUrl(options.Value.PublicBaseUrl);
        var result = isValid
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("The public base URL is invalid.");

        return Task.FromResult(result);
    }
}
