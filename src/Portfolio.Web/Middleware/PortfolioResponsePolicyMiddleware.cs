using Microsoft.Net.Http.Headers;

namespace Portfolio.Web.Middleware;

/// <summary>Applies the document security policy and explicit non-stale dynamic response caching.</summary>
public sealed class PortfolioResponsePolicyMiddleware(RequestDelegate next)
{
    private const string CloudflareCacheControl = "Cloudflare-CDN-Cache-Control";
    private const string DocumentCacheControl = "private, no-cache, max-age=0, must-revalidate";
    private const string ContentSecurityPolicy =
        "default-src 'self'; "
        + "base-uri 'none'; "
        + "connect-src 'self'; "
        + "font-src 'self' https://fonts.gstatic.com; "
        + "form-action 'self'; "
        + "frame-ancestors 'none'; "
        + "img-src 'self'; "
        + "object-src 'none'; "
        + "script-src 'self'; "
        + "script-src-attr 'none'; "
        + "style-src 'self' https://fonts.googleapis.com; "
        + "style-src-attr 'none'";

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            Apply((HttpContext)state);
            return Task.CompletedTask;
        }, context);

        await next(context);
    }

    private static void Apply(HttpContext context)
    {
        var response = context.Response;
        response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";

        if (IsNonStorable(context))
        {
            response.Headers[HeaderNames.CacheControl] = "no-store";
            response.Headers[CloudflareCacheControl] = "no-store";
        }
        else if (IsHtml(response.ContentType))
        {
            response.Headers[HeaderNames.CacheControl] = DocumentCacheControl;
            response.Headers[CloudflareCacheControl] = "no-store";
        }

        if (!IsHtml(response.ContentType))
        {
            return;
        }

        response.Headers[HeaderNames.ContentSecurityPolicy] = ContentSecurityPolicy;
        response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        response.Headers[HeaderNames.XFrameOptions] = "DENY";
        response.Headers["Permissions-Policy"] = "camera=(), geolocation=(), microphone=(), payment=(), usb=()";
    }

    private static bool IsNonStorable(HttpContext context) =>
        context.Response.StatusCode >= StatusCodes.Status300MultipleChoices
        || context.Request.Path.StartsWithSegments("/health");

    private static bool IsHtml(string? contentType) =>
        contentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) is true;
}
