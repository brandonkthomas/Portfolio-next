using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Portfolio.Web.Tests;

public sealed class PhotosPageTests
{
    [Fact]
    public async Task Grid_requests_only_preview_variants_and_defers_full_size_candidates_to_the_lightbox()
    {
        await WithPhotosPageAsync(TwoFormatManifest, (_, html) =>
        {
            Assert.Contains("data-photo-trigger", html, StringComparison.Ordinal);
            Assert.Contains("href=\"/media/photos/sample.123456789abc.1280.jpg\"", html, StringComparison.Ordinal);
            Assert.Contains("src=\"/media/photos/sample.0123456789ab.640.jpg\"", html, StringComparison.Ordinal);
            Assert.Contains("srcset=\"/media/photos/sample.abcdef012345.640.webp\"", html, StringComparison.Ordinal);
            Assert.Contains("width=\"640\"", html, StringComparison.Ordinal);
            Assert.Contains("height=\"480\"", html, StringComparison.Ordinal);
            Assert.Contains(
                "data-lightbox-srcset=\"/media/photos/sample.abcdef012345.640.webp 640w, /media/photos/sample.fedcba987654.1280.webp 1280w\"",
                html,
                StringComparison.Ordinal);
            Assert.Contains(
                "data-lightbox-srcset=\"/media/photos/sample.0123456789ab.640.jpg 640w, /media/photos/sample.123456789abc.1280.jpg 1280w\"",
                html,
                StringComparison.Ordinal);
            Assert.DoesNotMatch("\\s(?:src|srcset)=\"[^\"]*\\.1280\\.", html);
            Assert.DoesNotContain(" sizes=", html, StringComparison.Ordinal);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Photos_view_references_the_fingerprinted_lightbox_module()
    {
        await WithPhotosPageAsync(TwoFormatManifest, async (client, html) =>
        {
            var match = Regex.Match(
                html,
                "<link rel=\"modulepreload\" href=\"(?<path>/assets/js/photos\\.[^\"]+\\.js)\" data-photo-module>");
            Assert.True(match.Success, "The photos view did not reference a fingerprinted lightbox module.");

            var mainIndex = html.IndexOf("data-portfolio-main", StringComparison.Ordinal);
            Assert.True(mainIndex >= 0 && match.Index > mainIndex, "The lightbox module must be inside the replaced main region.");

            using var response = await client.GetAsync(match.Groups["path"].Value);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        });
    }

    [Fact]
    public async Task Empty_photos_view_does_not_reference_the_lightbox_module()
    {
        await WithPhotosPageAsync(EmptyManifest, (_, html) =>
        {
            Assert.Contains("class=\"empty-state\"", html, StringComparison.Ordinal);
            Assert.DoesNotContain("data-photo-module", html, StringComparison.Ordinal);
            return Task.CompletedTask;
        });
    }

    private static async Task WithPhotosPageAsync(string manifest, Func<HttpClient, string, Task> assert)
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"portfolio-photos-page-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var manifestPath = Path.Combine(temporaryDirectory, "photos.v1.json");

        try
        {
            await File.WriteAllTextAsync(manifestPath, manifest);

            using var baseFactory = new WebApplicationFactory<Program>();
            using var factory = baseFactory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["PhotoCatalog:ManifestPath"] = manifestPath
                    }));
            });
            using var client = factory.CreateClient();

            await assert(client, await client.GetStringAsync("/photos"));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private const string EmptyManifest = """
        {
          "schemaVersion": 1,
          "contentRevision": "photos-empty",
          "publishedAt": "2026-09-12T12:00:00Z",
          "entries": []
        }
        """;

    private const string TwoFormatManifest = """
        {
          "schemaVersion": 1,
          "contentRevision": "photos-two-format",
          "publishedAt": "2026-09-12T12:00:00Z",
          "entries": [
            {
              "id": "sample",
              "publishedAt": "2026-09-12T12:00:00Z",
              "altText": "A sample photo",
              "caption": null,
              "variants": [
                {
                  "url": "/media/photos/sample.123456789abc.1280.jpg",
                  "width": 1280,
                  "height": 960,
                  "mediaType": "image/jpeg"
                },
                {
                  "url": "/media/photos/sample.0123456789ab.640.jpg",
                  "width": 640,
                  "height": 480,
                  "mediaType": "image/jpeg"
                },
                {
                  "url": "/media/photos/sample.abcdef012345.640.webp",
                  "width": 640,
                  "height": 480,
                  "mediaType": "image/webp"
                },
                {
                  "url": "/media/photos/sample.fedcba987654.1280.webp",
                  "width": 1280,
                  "height": 960,
                  "mediaType": "image/webp"
                }
              ]
            }
          ]
        }
        """;
}
