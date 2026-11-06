using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Portfolio.Web.Configuration;
using Portfolio.Web.Models;

namespace Portfolio.Web.Pages;

/// <summary>
/// Backs the unlisted Contact page; an optional topic segment pre-fills the email without changing the page
/// </summary>
/// <param name="options">Provides the public origin used for canonical URLs.</param>
public sealed class ContactModel(IOptions<PortfolioOptions> options) : PortfolioPageModel(options)
{
    public const string EmailAddress = "me@brandonthomas.net";

    /// <summary>Email templates keyed by the route topic segment.</summary>
    private static readonly Dictionary<string, (string Subject, string Body)> Templates =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["swiftbible"] = (
                "SwiftBible Feedback",
                """
                Feedback type (bug, feature request, other):

                Summary:

                Steps to reproduce (bugs only):
                1.
                2.
                3.

                Expected result:

                Actual result:

                Device:
                iOS version:
                SwiftBible version:
                """)
        };

    /// <summary>Gets the mailto URL the email button opens.</summary>
    public string MailtoUrl { get; private set; } = $"mailto:{EmailAddress}";

    /// <summary>Prepares metadata and, for a known topic, a pre-filled email template.</summary>
    public IActionResult OnGet(string? topic)
    {
        if (topic is not null)
        {
            if (!Templates.TryGetValue(topic, out var template))
            {
                return NotFound();
            }

            // RFC 6068: line breaks in a mailto body are percent-encoded CRLF
            var body = template.Body.ReplaceLineEndings("\r\n");
            MailtoUrl = $"mailto:{EmailAddress}?subject={Uri.EscapeDataString(template.Subject)}&body={Uri.EscapeDataString(body)}";
        }

        SetMetadata(new PageMetadata(
            NavigationKey: "contact",
            DocumentTitle: "Contact | brandonthomas.net",
            Description: "Contact | Brandon Thomas",
            CanonicalPath: "/contact"));
        return Page();
    }
}
