using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portfolio.Web.Content.Photos;
using Portfolio.Web.Services;

namespace Portfolio.Web.Tests;

public sealed class RoutesTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory
        .WithWebHostBuilder(builder => builder.UseEnvironment("Production"))
        .CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

    [Fact]
    public async Task Shared_stylesheet_is_served_as_a_static_asset()
    {
        using var response = await _client.GetAsync("/css/portfolio.css");
        var css = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("color-scheme: light dark", css, StringComparison.Ordinal);
        Assert.Contains("light-dark(", css, StringComparison.Ordinal);
        Assert.Contains("html[data-theme-transition]", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Portfolio_document_references_a_fingerprinted_asset_path()
    {
        using var documentResponse = await _client.GetAsync("/");
        var html = await documentResponse.Content.ReadAsStringAsync();
        var match = Regex.Match(html, "href=\"(?<path>/css/portfolio\\.[^\"]+\\.css)\"");

        Assert.True(match.Success, "The portfolio document did not reference a fingerprinted stylesheet path.");
        Assert.DoesNotContain("?v=", match.Groups["path"].Value, StringComparison.Ordinal);

        using var assetResponse = await _client.GetAsync(match.Groups["path"].Value);

        Assert.Equal(HttpStatusCode.OK, assetResponse.StatusCode);
        Assert.Equal("text/css", assetResponse.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/projects")]
    [InlineData("/projects/")]
    [InlineData("/photos")]
    [InlineData("/photos/")]
    public async Task Portfolio_document_is_revalidated_and_has_the_security_policy(string path)
    {
        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.Private);
        Assert.True(response.Headers.CacheControl?.NoCache);
        Assert.True(response.Headers.CacheControl?.MustRevalidate);
        Assert.Equal(TimeSpan.Zero, response.Headers.CacheControl?.MaxAge);
        Assert.Equal("no-store", GetHeader(response, "Cloudflare-CDN-Cache-Control"));
        Assert.Contains("default-src 'self'", GetHeader(response, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", GetHeader(response, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Equal("strict-origin-when-cross-origin", GetHeader(response, "Referrer-Policy"));
        Assert.Equal("DENY", GetHeader(response, "X-Frame-Options"));
        Assert.Equal("nosniff", GetHeader(response, "X-Content-Type-Options"));
    }

    [Fact]
    public async Task Portfolio_shell_includes_the_icon_theme_button_and_prepaint_script()
    {
        using var response = await _client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-portfolio-shell-version=\"2\"", html, StringComparison.Ordinal);
        Assert.Contains("<meta name=\"color-scheme\" content=\"light dark\">", html, StringComparison.Ordinal);
        Assert.Contains("data-theme-button", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Toggle color theme\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("theme-icon-system", html, StringComparison.Ordinal);
        Assert.DoesNotContain("name=\"theme\"", html, StringComparison.Ordinal);
        Assert.Contains(">info</a>", html, StringComparison.Ordinal);
        Assert.Contains("data-navigation-order=\"0\"", html, StringComparison.Ordinal);
        Assert.Contains("data-navigation-order=\"1\"", html, StringComparison.Ordinal);
        Assert.Contains("data-navigation-order=\"2\"", html, StringComparison.Ordinal);
        Assert.Contains("class=\"portfolio-view-content\"", html, StringComparison.Ordinal);
        Assert.Equal(3, CountOccurrences(html, "class=\"navigation-indicator\""));
        Assert.Contains("/assets/svg/bt-logo-boxed", html, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(html, "/assets/svg/external-link-nobox"));

        var menuIndex = html.IndexOf("data-menu", StringComparison.Ordinal);
        var themeButtonIndex = html.IndexOf("data-theme-button", StringComparison.Ordinal);
        var menuEndIndex = html.IndexOf("</aside>", StringComparison.Ordinal);
        Assert.True(menuIndex >= 0 && themeButtonIndex > menuIndex && themeButtonIndex < menuEndIndex);

        var themeScriptIndex = html.IndexOf("/assets/js/theme", StringComparison.Ordinal);
        var stylesheetIndex = html.IndexOf("/css/portfolio", StringComparison.Ordinal);
        Assert.True(themeScriptIndex >= 0 && themeScriptIndex < stylesheetIndex);
    }

    [Fact]
    public async Task Projects_render_supplied_icons_for_applicable_technology_tags()
    {
        using var response = await _client.GetAsync("/projects");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-portfolio-view=\"projects\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#tag-icon-dotnet\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#tag-icon-typescript\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#tag-icon-threejs\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#tag-icon-css\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#tag-icon-c-sharp\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#tag-icon-windows\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#tag-icon-bash\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#tag-icon-terminal\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/assets/svg/project-tags/", html, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string expected)
    {
        var count = 0;
        var startIndex = 0;

        while ((startIndex = value.IndexOf(expected, startIndex, StringComparison.Ordinal)) >= 0)
        {
            count++;
            startIndex += expected.Length;
        }

        return count;
    }

    private static string GetHeader(HttpResponseMessage response, string name) =>
        string.Join(", ", response.Headers.GetValues(name));

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoint_is_healthy(string path)
    {
        using var response = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        Assert.Equal("no-store", GetHeader(response, "Cache-Control"));
        Assert.Equal("no-store", GetHeader(response, "Cloudflare-CDN-Cache-Control"));
    }

    [Fact]
    public async Task Unknown_route_returns_the_not_found_page_with_a_real_404()
    {
        using var response = await _client.GetAsync("/not-a-real-route");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("<h1>Page not found</h1>", html, StringComparison.Ordinal);
        Assert.Equal("no-store", GetHeader(response, "Cache-Control"));
        Assert.Equal("no-store", GetHeader(response, "Cloudflare-CDN-Cache-Control"));
        Assert.Contains("default-src 'self'", GetHeader(response, "Content-Security-Policy"), StringComparison.Ordinal);
    }
}
