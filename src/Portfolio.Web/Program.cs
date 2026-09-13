using System.Net;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Portfolio.Web.Configuration;
using Portfolio.Web.Health;
using Portfolio.Web.Middleware;
using Portfolio.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Proxy appsettings section (for use with router)
var knownProxyValue = builder.Configuration["Proxy:KnownProxy"];
if (!string.IsNullOrWhiteSpace(knownProxyValue)
    && !IPAddress.TryParse(knownProxyValue, out _))
{
    throw new InvalidOperationException("Proxy:KnownProxy must be an IP address.");
}

// Custom JSON logging
// builder.Logging.ClearProviders();
// builder.Logging.AddJsonConsole(options =>
// {
//     options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
//     options.UseUtcTimestamp = true;
// });

// Portfolio appsettings section + validation
builder.Services
    .AddOptions<PortfolioOptions>()
    .BindConfiguration(PortfolioOptions.SectionName)
    .Validate(
        options => PortfolioOptions.IsValidPublicBaseUrl(options.PublicBaseUrl),
        $"{PortfolioOptions.SectionName}:PublicBaseUrl must be an absolute HTTP(S) origin without a path, query, fragment, or credentials.")
    .ValidateOnStart();
builder.Services
    .AddOptions<PhotoCatalogOptions>()
    .BindConfiguration(PhotoCatalogOptions.SectionName)
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.ManifestPath),
        $"{PhotoCatalogOptions.SectionName}:ManifestPath is required.")
    .Validate(
        options => options.ReconciliationInterval >= TimeSpan.FromSeconds(30),
        $"{PhotoCatalogOptions.SectionName}:ReconciliationInterval must be at least 30 seconds.")
    .Validate(
        options => options.ChangeDebounce >= TimeSpan.FromMilliseconds(50),
        $"{PhotoCatalogOptions.SectionName}:ChangeDebounce must be at least 50 milliseconds.")
    .ValidateOnStart();

// Razor pages
// JSON catalog loading/validation/refresh
// Native health check registration
builder.Services.AddRazorPages();
builder.Services.AddPortfolioContent(builder.Environment);
builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live", "ready"])
    .AddCheck<PublicBaseUrlHealthCheck>("public_base_url", tags: ["ready"])
    .AddCheck<PhotoCatalogHealthCheck>("photo_catalog", tags: ["ready"]);

// Forwarded headers (for use with router)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedHost
        | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;

    if (IPAddress.TryParse(knownProxyValue, out var knownProxy))
    {
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        options.KnownProxies.Add(knownProxy);
    }
});

// Base URL
var app = builder.Build();

var portfolioOptions = app.Services.GetRequiredService<IOptions<PortfolioOptions>>().Value;
app.Logger.LogInformation(
    "Starting Portfolio.Web with public base URL {PublicBaseUrl}",
    portfolioOptions.PublicBaseUrl);

// Forwarded headers middleware (for use with router)
app.UseForwardedHeaders();

// Explicit document caching + security policy
app.UseMiddleware<PortfolioResponsePolicyMiddleware>();

// Exception pages
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error/500");
}

// Status code pages
app.UseStatusCodePagesWithReExecute("/error/{0}");

// Static files
// Custom health checks
var staticAssets = app.MapStaticAssets();
staticAssets.Add(ContentHashedFontCaching.Apply);
staticAssets.ShortCircuit();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
app.MapRazorPages().WithStaticAssets();

app.Run();

public partial class Program;
