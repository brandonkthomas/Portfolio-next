using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Portfolio.Web.Tests;

public sealed class PhotoCatalogReloadTests
{
    [Fact]
    public async Task Atomic_manifest_activation_is_visible_to_the_next_document_request()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"portfolio-photo-reload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var manifestPath = Path.Combine(temporaryDirectory, "photos.v1.json");

        try
        {
            await File.WriteAllTextAsync(manifestPath, EmptyManifest);

            using var baseFactory = new WebApplicationFactory<Program>();
            using var factory = baseFactory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, configuration) =>
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["PhotoCatalog:ManifestPath"] = manifestPath,
                        ["PhotoCatalog:ChangeDebounce"] = "00:00:00.050",
                        ["PhotoCatalog:ReconciliationInterval"] = "00:00:30"
                    }));
            });
            using var client = factory.CreateClient();

            var initialHtml = await client.GetStringAsync("/photos");
            Assert.DoesNotContain("Added by atomic activation", initialHtml, StringComparison.Ordinal);

            var replacementPath = Path.Combine(temporaryDirectory, "photos.next.json");
            await File.WriteAllTextAsync(replacementPath, UpdatedManifest);
            File.Move(replacementPath, manifestPath, overwrite: true);

            var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            string updatedHtml;
            do
            {
                await Task.Delay(50);
                updatedHtml = await client.GetStringAsync("/photos");
            }
            while (!updatedHtml.Contains("Added by atomic activation", StringComparison.Ordinal)
                && DateTimeOffset.UtcNow < deadline);

            Assert.Contains("Added by atomic activation", updatedHtml, StringComparison.Ordinal);
            Assert.Contains("/media/photos/added.0123456789ab.640.jpg", updatedHtml, StringComparison.Ordinal);
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
          "publishedAt": "2026-09-10T12:00:00Z",
          "entries": []
        }
        """;

    private const string UpdatedManifest = """
        {
          "schemaVersion": 1,
          "contentRevision": "photos-updated",
          "publishedAt": "2026-09-10T12:01:00Z",
          "entries": [
            {
              "id": "added",
              "publishedAt": "2026-09-10T12:01:00Z",
              "altText": "Added by atomic activation",
              "caption": null,
              "variants": [
                {
                  "url": "/media/photos/added.0123456789ab.640.jpg",
                  "width": 640,
                  "height": 480,
                  "mediaType": "image/jpeg"
                }
              ]
            }
          ]
        }
        """;
}
