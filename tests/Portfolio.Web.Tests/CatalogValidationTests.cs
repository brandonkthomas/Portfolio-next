using Portfolio.Web.Content;
using Portfolio.Web.Services;

namespace Portfolio.Web.Tests;

public sealed class CatalogValidationTests
{
    [Theory]
    [InlineData("/projects/webamp")]
    [InlineData("/webamp/")]
    [InlineData("https://example.com/demo")]
    public void Project_catalog_accepts_optional_local_or_external_demo_url(string liveUrl)
    {
        var catalogPath = CreateProjectCatalog(liveUrl);
        try
        {
            var project = Assert.Single(JsonProjectCatalog.Load(catalogPath, AppContext.BaseDirectory).Projects);
            Assert.Equal(liveUrl, project.LiveUrl);
            Assert.Null(project.SourceUrl);
        }
        finally
        {
            File.Delete(catalogPath);
        }
    }

    [Theory]
    [InlineData("//evil.example/demo")]
    [InlineData("/projects/../admin")]
    [InlineData("/projects/webamp?redirect=evil")]
    [InlineData("/projects/%2e%2e/admin")]
    [InlineData("javascript:alert(1)")]
    public void Project_catalog_rejects_unsafe_demo_url(string liveUrl)
    {
        var catalogPath = CreateProjectCatalog(liveUrl);
        try
        {
            Assert.Throws<CatalogValidationException>(() => JsonProjectCatalog.Load(catalogPath, AppContext.BaseDirectory));
        }
        finally
        {
            File.Delete(catalogPath);
        }
    }

    [Fact]
    public void Project_catalog_rejects_duplicate_slugs()
    {
        var catalogPath = CreateTemporaryFile(
            """
            {
              "schemaVersion": 1,
              "projects": [
                {
                  "slug": "duplicate",
                  "title": "First",
                  "summary": "First project.",
                  "order": 1,
                  "tags": ["Web"]
                },
                {
                  "slug": "duplicate",
                  "title": "Second",
                  "summary": "Second project.",
                  "order": 2,
                  "tags": ["Web"]
                }
              ]
            }
            """);

        try
        {
            var exception = Assert.Throws<CatalogValidationException>(() =>
                JsonProjectCatalog.Load(catalogPath, AppContext.BaseDirectory));

            Assert.Contains("Duplicate project slug 'duplicate'.", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(catalogPath);
        }
    }

    [Theory]
    [InlineData("windows", true)]
    [InlineData("linux", true)]
    [InlineData("Windows", false)]
    [InlineData("android", false)]
    public void Project_catalog_validates_download_platforms(string platform, bool valid)
    {
        var catalogPath = CreateTemporaryFile(
            $$"""
            {
              "schemaVersion": 1,
              "projects": [
                {
                  "slug": "tool",
                  "title": "Tool",
                  "summary": "A tool.",
                  "order": 1,
                  "tags": ["Windows"],
                  "downloads": [
                    { "label": "Installer", "url": "https://example.com/tool.exe", "platform": "{{platform}}" }
                  ]
                }
              ]
            }
            """);

        try
        {
            if (valid)
            {
                var download = Assert.Single(Assert.Single(JsonProjectCatalog.Load(catalogPath, AppContext.BaseDirectory).Projects).Downloads);
                Assert.Equal(platform, download.Platform);
            }
            else
            {
                var exception = Assert.Throws<CatalogValidationException>(() =>
                    JsonProjectCatalog.Load(catalogPath, AppContext.BaseDirectory));
                Assert.Contains("download platform", exception.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            File.Delete(catalogPath);
        }
    }

    [Fact]
    public void Project_catalog_accepts_an_existing_icon_asset()
    {
        var webRootPath = Directory.CreateTempSubdirectory("portfolio-project-root-").FullName;
        var iconDirectory = Directory.CreateDirectory(Path.Combine(webRootPath, "assets", "webp", "projects"));
        File.WriteAllBytes(Path.Combine(iconDirectory.FullName, "demo.webp"), [0]);
        var catalogPath = CreateProjectCatalogWithIcon("/assets/webp/projects/demo.webp");

        try
        {
            var project = Assert.Single(JsonProjectCatalog.Load(catalogPath, webRootPath).Projects);
            Assert.Equal("/assets/webp/projects/demo.webp", project.Icon?.Path);
            Assert.True(project.Icon?.InvertInDarkTheme);
        }
        finally
        {
            File.Delete(catalogPath);
            Directory.Delete(webRootPath, recursive: true);
        }
    }

    [Theory]
    [InlineData("/assets/webp/projects/missing.webp")]
    [InlineData("/assets/../appsettings.json")]
    [InlineData("/fixtures/photos/demo.webp")]
    [InlineData("https://example.com/icon.png")]
    [InlineData("/assets/webp/projects/demo.webp?v=1")]
    public void Project_catalog_rejects_missing_or_unsafe_icon_paths(string iconPath)
    {
        var webRootPath = Directory.CreateTempSubdirectory("portfolio-project-root-").FullName;
        var catalogPath = CreateProjectCatalogWithIcon(iconPath);

        try
        {
            var exception = Assert.Throws<CatalogValidationException>(() =>
                JsonProjectCatalog.Load(catalogPath, webRootPath));

            Assert.Contains("icon", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(catalogPath);
            Directory.Delete(webRootPath, recursive: true);
        }
    }

    [Fact]
    public void Photo_catalog_rejects_missing_fixture_files()
    {
        var webRootPath = Directory.CreateTempSubdirectory("portfolio-photo-root-").FullName;
        var catalogPath = CreateTemporaryFile(
            """
            {
              "schemaVersion": 1,
              "contentRevision": "invalid-test",
              "publishedAt": "2026-09-06T12:00:00Z",
              "entries": [
                {
                  "id": "missing-photo",
                  "publishedAt": "2026-09-06T12:00:00Z",
                  "altText": "A missing fixture image.",
                  "variants": [
                    {
                      "url": "/fixtures/photos/missing-photo.0123456789ab.640.jpg",
                      "width": 640,
                      "height": 480,
                      "mediaType": "image/jpeg"
                    }
                  ]
                }
              ]
            }
            """);

        try
        {
            var exception = Assert.Throws<CatalogValidationException>(() =>
                JsonPhotoCatalog.Load(catalogPath, webRootPath));

            Assert.Contains("fixture file", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(catalogPath);
            Directory.Delete(webRootPath);
        }
    }

    private static string CreateTemporaryFile(string contents)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, contents);
        return path;
    }

    private static string CreateProjectCatalogWithIcon(string iconPath) => CreateTemporaryFile(
        $$"""
        {
          "schemaVersion": 1,
          "projects": [
            {
              "slug": "demo",
              "title": "Demo",
              "icon": { "path": "{{iconPath}}", "invertInDarkTheme": true },
              "summary": "A demo.",
              "order": 1,
              "tags": ["Web"]
            }
          ]
        }
        """);

    private static string CreateProjectCatalog(string liveUrl) => CreateTemporaryFile(
        $$"""
        {
          "schemaVersion": 1,
          "projects": [
            {
              "slug": "webamp",
              "title": "WebAmp",
              "summary": "A demo.",
              "order": 1,
              "tags": ["Web"],
              "liveUrl": "{{liveUrl}}"
            }
          ]
        }
        """);
}
