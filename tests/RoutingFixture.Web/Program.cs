using System.Net;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.HttpOverrides;

// Fixture route configuration
const string fixturePathBase = "/projects/routing-fixture";

var builder = WebApplication.CreateBuilder(args);

// Proxy appsettings section (for use with router)
var knownProxyValue = builder.Configuration["RoutingFixture:KnownProxy"];

if (string.IsNullOrWhiteSpace(knownProxyValue)
    || !IPAddress.TryParse(knownProxyValue, out var knownProxy))
{
    throw new InvalidOperationException("RoutingFixture:KnownProxy must be an IP address.");
}

// Forwarded headers registration (for use with router)
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
        | ForwardedHeaders.XForwardedHost
        | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(knownProxy);
});

// Build application
var app = builder.Build();

// Forwarded headers middleware (for use with router)
app.UseForwardedHeaders();

// Preserve the registered public prefix in .NET PathBase
app.UsePathBase(fixturePathBase);

// Canonical fixture root
app.Use(async (context, next) =>
{
    if (context.Request.PathBase == fixturePathBase && !context.Request.Path.HasValue)
    {
        context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
        context.Response.Headers.Location = $"{fixturePathBase}/{context.Request.QueryString}";
        return;
    }

    await next(context);
});

// Static files
app.UseStaticFiles();

// Status code pages
app.UseStatusCodePages("text/plain", "Routing fixture: {0}");

// Fixture routes and endpoint handlers
app.MapGet("/", RenderRoot).WithName("fixture-root");
app.MapGet("/nested/document", RenderNested).WithName("fixture-nested");
app.MapGet("/api/status", (HttpContext context, LinkGenerator links) => Results.Json(new
{
    Status = "ok",
    PathBase = context.Request.PathBase.Value,
    Path = context.Request.Path.Value,
    Scheme = context.Request.Scheme,
    Host = context.Request.Host.Value,
    Query = context.Request.QueryString.Value,
    RootUrl = EnsureTrailingSlash(links.GetUriByName(context, "fixture-root"))
}));
app.MapGet("/redirect", (HttpContext context, LinkGenerator links) =>
    Results.Redirect(EnsureTrailingSlash(links.GetPathByName(context, "fixture-root"))));
app.MapPost("/submit", async (HttpContext context, LinkGenerator links) =>
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    var location = links.GetPathByName(
        context,
        "fixture-submitted",
        new { value = form["value"].ToString() });

    return Results.Redirect(RequirePath(location));
});
app.MapGet("/submitted", (string? value) => Results.Text($"Submitted: {value}", "text/plain"))
    .WithName("fixture-submitted");
app.MapGet("/health/live", () => Results.Text("Healthy", "text/plain"));

// Start application
app.Run();

// Fixture root document
static IResult RenderRoot(HttpContext context, LinkGenerator links)
{
    var encoder = HtmlEncoder.Default;
    var pathBase = context.Request.PathBase.Value ?? string.Empty;
    var nestedPath = RequirePath(links.GetPathByName(context, "fixture-nested"));
    var rootUrl = EnsureTrailingSlash(links.GetUriByName(context, "fixture-root"));

    return Results.Content($$"""
        <!doctype html>
        <html lang="en">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Routing fixture</title>
            <link rel="stylesheet" href="{{encoder.Encode(pathBase)}}/assets/fixture.css">
        </head>
        <body data-root-url="{{encoder.Encode(rootUrl)}}">
            <main>
                <h1>Routing fixture</h1>
                <a href="{{encoder.Encode(nestedPath)}}">Nested document</a>
                <img src="{{encoder.Encode(pathBase)}}/assets/fixture.svg" alt="" width="32" height="32">
                <form method="post" action="{{encoder.Encode(pathBase)}}/submit">
                    <label>Value <input name="value" value="fixture"></label>
                    <button type="submit">Submit</button>
                </form>
            </main>
            <script src="{{encoder.Encode(pathBase)}}/assets/fixture.js"></script>
        </body>
        </html>
        """, "text/html");
}

// Fixture nested document
static IResult RenderNested(HttpContext context, LinkGenerator links)
{
    var rootPath = HtmlEncoder.Default.Encode(
        EnsureTrailingSlash(links.GetPathByName(context, "fixture-root")));

    return Results.Content($$"""
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><title>Nested routing fixture</title></head>
        <body><main><h1>Nested routing fixture</h1><a href="{{rootPath}}">Fixture root</a></main></body>
        </html>
        """, "text/html");
}

// URL helpers
static string RequirePath(string? value) =>
    value ?? throw new InvalidOperationException("The routing fixture could not generate a required URL.");

static string EnsureTrailingSlash(string? value) => $"{RequirePath(value).TrimEnd('/')}/";
