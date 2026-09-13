using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Portfolio.Web.Content.Photos;
using Portfolio.Web.Services;

namespace Portfolio.Web.Tests;

public sealed class RoutesTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["PhotoCatalog:ManifestPath"] = Path.Combine(
                        AppContext.BaseDirectory, "Fixtures", "empty-photos.v1.json")
                }));
        })
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
        Assert.Matches(@"color-scheme:\s*light dark", css);
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
        Assert.Contains("script-src 'self' https://static.cloudflareinsights.com;", GetHeader(response, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Contains("connect-src 'self' https://cloudflareinsights.com;", GetHeader(response, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Contains("script-src-attr 'none'", GetHeader(response, "Content-Security-Policy"), StringComparison.Ordinal);
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
        Assert.Equal(3, CountOccurrences(html, "/assets/svg/external-link-nobox"));
        Assert.Contains("href=\"https://github.com/brandonkthomas\"", html, StringComparison.Ordinal);

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
        Assert.Contains("href=\"#tag-icon-swift\"", html, StringComparison.Ordinal);
        Assert.Contains("<symbol id=\"tag-icon-swift\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/assets/svg/project-tags/", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Projects_render_fingerprinted_catalog_icons()
    {
        using var response = await _client.GetAsync("/projects");
        var html = await response.Content.ReadAsStringAsync();

        var webAmpIcon = Regex.Match(
            html,
            "<img class=\"project-icon\"\\s+src=\"(?<path>/assets/webp/projects/webamp\\.[^\"]+\\.webp)\"");
        Assert.True(webAmpIcon.Success, "The projects page did not reference a fingerprinted WebAmp icon.");
        Assert.Matches(
            "<img class=\"project-icon monochrome-icon\"\\s+src=\"/assets/svg/bt-logo-boxed\\.[^\"]+\\.svg\"",
            html);
        Assert.Matches(
            "<img class=\"project-icon\"\\s+src=\"/assets/webp/projects/swift-bible\\.[^\"]+\\.webp\"",
            html);

        // Projects without catalog artwork share one fingerprinted placeholder icon.
        var placeholderPattern = "<img class=\"project-icon\" src=\"/assets/svg/project-placeholder\\.[^\"]+\\.svg\"";
        Assert.Equal(4, Regex.Matches(html, placeholderPattern).Count);

        using var iconResponse = await _client.GetAsync(webAmpIcon.Groups["path"].Value);
        Assert.Equal(HttpStatusCode.OK, iconResponse.StatusCode);
        Assert.Equal("image/webp", iconResponse.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Portfolio_document_links_fingerprinted_favicons()
    {
        using var response = await _client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        var favicon = Regex.Match(html, "<link rel=\"icon\" href=\"(?<path>/favicon\\.[^\"]+\\.ico)\" sizes=\"any\">");
        var touchIcon = Regex.Match(html, "<link rel=\"apple-touch-icon\" href=\"(?<path>/apple-touch-icon\\.[^\"]+\\.png)\">");
        Assert.True(favicon.Success, "The document did not link a fingerprinted favicon.");
        Assert.True(touchIcon.Success, "The document did not link a fingerprinted apple-touch icon.");

        foreach (var path in new[] { favicon.Groups["path"].Value, touchIcon.Groups["path"].Value, "/favicon.ico" })
        {
            using var iconResponse = await _client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, iconResponse.StatusCode);
        }
    }

    [Theory]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36", null, true)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:140.0) Gecko/20100101 Firefox/140.0", null, true)]
    [InlineData("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/27.0 Safari/605.1.15", null, false)]
    [InlineData("Mozilla/5.0 (Linux; Android 16; Pixel 9) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Mobile Safari/537.36", null, false)]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36", "\"Windows\"", true)]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36", "\"macOS\"", false)]
    [InlineData("", null, false)]
    public async Task Windows_download_is_offered_only_to_windows_clients(string userAgent, string? platformHint, bool expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/projects");
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        if (platformHint is not null)
        {
            request.Headers.TryAddWithoutValidation("Sec-CH-UA-Platform", platformHint);
        }

        using var response = await _client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expected, html.Contains("Download Spectrometer for Windows", StringComparison.Ordinal));
        Assert.Contains("User-Agent", response.Headers.Vary);
        Assert.Contains("Sec-CH-UA-Platform", response.Headers.Vary);
    }

    [Fact]
    public async Task Stylesheet_fonts_are_self_hosted_and_immutable()
    {
        using var documentResponse = await _client.GetAsync("/");
        var html = await documentResponse.Content.ReadAsStringAsync();
        var csp = GetHeader(documentResponse, "Content-Security-Policy");

        Assert.DoesNotContain("fonts.googleapis.com", html, StringComparison.Ordinal);
        Assert.DoesNotContain("fonts.gstatic.com", html, StringComparison.Ordinal);
        Assert.Contains("font-src 'self';", csp, StringComparison.Ordinal);
        Assert.Contains("style-src 'self';", csp, StringComparison.Ordinal);

        var stylesheetPath = Regex.Match(html, "href=\"(?<path>/css/portfolio\\.[^\"]+\\.css)\"").Groups["path"].Value;
        var css = await _client.GetStringAsync(stylesheetPath);
        var fontPaths = Regex.Matches(css, @"url\(""?(?<path>/assets/fonts/mukta-mahee-(?:400|700)-[A-Z0-9]{8}\.woff2)""?\)")
            .Select(match => match.Groups["path"].Value)
            .Distinct()
            .ToArray();
        Assert.Equal(2, fontPaths.Length);

        foreach (var fontPath in fontPaths)
        {
            using var fontResponse = await _client.GetAsync(fontPath);
            Assert.Equal(HttpStatusCode.OK, fontResponse.StatusCode);
            Assert.Equal("font/woff2", fontResponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal("public, max-age=31536000, immutable", GetHeader(fontResponse, "Cache-Control"));
        }
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/projects")]
    [InlineData("/photos")]
    public async Task Rendered_text_stays_within_the_font_subset(string path)
    {
        var html = WebUtility.HtmlDecode(await _client.GetStringAsync(path));
        // Must match the unicode ranges documented in Styles/foundation/fonts.css.
        var unsupported = html
            .Where(character => character is not ('\n' or '\r' or '\t')
                && character is not (>= '\u0020' and <= '\u007E')
                && character is not ('\u00A0' or '\u00A9' or '\u00B7' or '\u2013' or '\u2014' or '\u2018'
                    or '\u2019' or '\u201C' or '\u201D' or '\u2022' or '\u2026'))
            .Distinct()
            .Select(character => $"U+{(int)character:X4}")
            .ToArray();

        Assert.True(unsupported.Length == 0, $"{path} renders characters outside the font subset: {string.Join(", ", unsupported)}");
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
