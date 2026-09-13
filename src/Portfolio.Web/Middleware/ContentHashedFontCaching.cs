using System.Text.RegularExpressions;
using Microsoft.Net.Http.Headers;

namespace Portfolio.Web.Middleware;

/// <summary>
/// Marks esbuild's content-hashed stylesheet fonts immutable on their static-asset endpoints
/// </summary>
/// <remarks>
/// MapStaticAssets only marks its own fingerprinted routes immutable, and its short-circuited endpoints bypass the
/// response policy middleware. The stylesheet references esbuild's hashed names directly, so those routes are
/// wrapped here; every other static asset keeps its framework-generated headers.
/// </remarks>
public static partial class ContentHashedFontCaching
{
    private const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    /// <summary>Endpoint convention for <c>MapStaticAssets</c>.</summary>
    public static void Apply(EndpointBuilder endpoint)
    {
        if (endpoint is not RouteEndpointBuilder route
            || route.RequestDelegate is not { } next
            || !ContentHashedFontRoute().IsMatch(route.RoutePattern.RawText ?? string.Empty))
        {
            return;
        }

        route.RequestDelegate = context =>
        {
            context.Response.OnStarting(static state =>
            {
                var response = (HttpResponse)state;
                if (response.StatusCode is StatusCodes.Status200OK or StatusCodes.Status304NotModified)
                {
                    response.Headers[HeaderNames.CacheControl] = ImmutableCacheControl;
                }

                return Task.CompletedTask;
            }, context.Response);

            return next(context);
        };
    }

    [GeneratedRegex(@"^/?assets/fonts/[a-z0-9-]+-[A-Z0-9]{8}\.woff2$", RegexOptions.CultureInvariant)]
    private static partial Regex ContentHashedFontRoute();
}
